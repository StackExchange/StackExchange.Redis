using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using StackExchange.Redis;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis.Benchmarks;

/// <summary>
/// A command served from the client-side cache: the path that completes <b>synchronously</b>.
/// </summary>
/// <remarks>
/// This is where per-call struct copies can actually show. On a miss the call ends in a network round
/// trip measured in microseconds, and a 40-byte copy is noise against it; on a hit there is no I/O at
/// all, so what remains is rendering the frame, hashing it, one dictionary probe, and whatever the
/// calling convention costs.
/// </remarks>
[MemoryDiagnoser]
public class CacheHitSendBenchmarks
{
    private sealed class OneReplyExecutor : RespExecutorBase
    {
        private readonly byte[] _reply = Encoding.UTF8.GetBytes("$5\r\nhello\r\n");

        public override int Database => 0;

        public override RespPayload Send(in RespRequest request) => RespPayload.Create(_reply);

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }
    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    /// <summary>
    /// Key sizes that bracket the builder's initial rent.
    /// </summary>
    /// <remarks>
    /// The builder rents <c>HeaderMax + 64 + (formattedCount * 24)</c>, which for a two-hole
    /// <c>$"{GET}{key}"</c> is <b>126 bytes whatever the key is</b> - the estimate counts holes, not
    /// content. A GET frame needs about <c>25 + key</c> bytes, so a key past roughly 96 overflows it and
    /// Ensure grows: a second rent, a copy, and two returns. These sizes sit either side of that.
    /// </remarks>
    [Params(8, 64, 128, 256)]
    public int KeySize { get; set; }

    private RedisKey _key;
    private RespClientCache _cache = null!;
    private RespDatabaseContext _context;

    // held for the whole run, so the stages below can start from an already-rendered, already-hashed key
    private RespRequestFrame _frame;
    private RespRequest _lookup;
    private RespRequest _otherCopy;            // same bytes, different buffer: what Equals really compares
    private RespPayload _payload = null!;      // the cached reply, one reference held by this class
    private byte[] _scratch = null!;
    private byte[] _keyBytes = null!;
    private IRespHandler<RedisValue> _handler = null!;
    private ConcurrentDictionary<RespRequest, object> _baselineDictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cache = new RespClientCache();
        _context = new RespDatabaseContext(new RespContext().WithExecutor(new OneReplyExecutor()).WithCache(_cache));
        _key = new string('k', KeySize);

        // prime it, so every measured call is a hit
        _ = _context.Strings.GetAsync(_key, Readable).GetAwaiter().GetResult();

        _frame = _context.Raw.Render($"{RedisCommand.GET}{_key}");
        _lookup = _frame.AsLookupKey(Readable);
        _otherCopy = _lookup.CopyForCacheKey();
        if (!_cache.TryGet(_lookup, 0, out var payload)) throw new InvalidOperationException("not primed");
        _payload = payload;
        _scratch = ArrayPool<byte>.Shared.Rent(64 + KeySize); // room for the largest key; c1 is the floor, not the builder's estimate
        _handler = RespHandlers.Inbuilt<RedisValue>.Require();
        _keyBytes = Encoding.UTF8.GetBytes((string)_key!);
        _baselineDictionary = new ConcurrentDictionary<RespRequest, object>();
        _baselineDictionary[_otherCopy] = _payload;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _payload.Release();
        _otherCopy.Dispose();
        _frame.Dispose();
        ArrayPool<byte>.Shared.Return(_scratch);
        _cache.Dispose();
    }

    /// <summary>The whole group-method path, served locally.</summary>
    [Benchmark(Baseline = true, Description = "4. full  db.Strings.GetAsync")]
    public RedisValue GroupGet() => _context.Strings.GetAsync(_key, Readable).GetAwaiter().GetResult();

    // ---- the same path, taken apart, so the total can be attributed rather than guessed at ----





    /// <summary>
    /// Just the pooled rent and return, at the size the builder asks for.
    /// </summary>
    /// <remarks>
    /// <b>An upper bound on what deferring the buffer could save</b>, not a prediction of the win. The
    /// builder rents <c>HeaderMax + 64 + literalLength + (formattedCount * 24)</c>, which for
    /// <c>$"{GET}{key}"</c> is 126 bytes and so the 128 bucket. If this is a small share of stage 3 there
    /// is nothing here worth rewriting the writer for; the idea only earns its keep if it is not.
    /// </remarks>
    [Benchmark(Description = "1. rent+return only (128B)")]
    public int RentReturn()
    {
        // the builder's own estimate, which does not depend on the key
        var array = ArrayPool<byte>.Shared.Rent(126);
        var length = array.Length;
        ArrayPool<byte>.Shared.Return(array);
        return length;
    }

    /// <summary>Render the frame and throw it away: the rent and the write, with no hash and no probe.</summary>
    /// <remarks>
    /// The hash is NOT in here: it is taken when the lookup key is made (<c>AsLookupKey</c>), which stage 2h
    /// adds. So this minus stage 1 is formatting, and stage 3 minus 2h is the probe. Measured this way round
    /// because a build-up decomposition here previously reported hashing as free - the JIT had deleted it,
    /// since nothing consumed the result.
    /// </remarks>
    [Benchmark(Description = "2. render only (no probe)")]
    public int RenderOnly()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{_key}");
        return frame.ArgCount;   // consumed, so the render cannot be optimised away
    }

    /// <summary>Render and make the lookup key, which is where the hash is taken; no probe.</summary>
    [Benchmark(Description = "2h. render + lookup key (hash)")]
    public int RenderAndHash()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{_key}");
        return frame.AsLookupKey(Readable).GetHashCode();   // consumed, so the hash cannot be optimised away
    }

    // ---- single components, from state prepared in Setup: each is one item of the hit path on its own ----

    /// <summary>Formatting alone: the frame written into a buffer that is already rented.</summary>
    [Benchmark(Description = "c1. format only (pre-rented)")]
    public int FormatOnly()
    {
        // GET + key as RESP, by hand: the builder has no entry point that takes a caller's buffer, so this is
        // the floor for writing these bytes rather than the builder's own cost - compare it with 2 minus 1
        var span = _scratch.AsSpan();
        int written = 0;
        "*2\r\n$3\r\nGET\r\n"u8.CopyTo(span);
        written += 13;
        span[written++] = (byte)'$';
        Utf8Formatter.TryFormat(KeySize, span.Slice(written), out var digits);
        written += digits;
        span[written++] = (byte)'\r';
        span[written++] = (byte)'\n';
        _keyBytes.CopyTo(span.Slice(written));
        written += _keyBytes.Length;
        span[written++] = (byte)'\r';
        span[written++] = (byte)'\n';
        return written;
    }

    /// <summary>The hash alone, over the rendered bytes.</summary>
    [Benchmark(Description = "c2. hash only")]
    public int HashOnly() => RedisValue.GetHashCode(_lookup.Span);

    /// <summary>Equality alone: two keys with the same bytes in different buffers, as a real probe compares.</summary>
    [Benchmark(Description = "c3. equals only")]
    public bool EqualsOnly() => _lookup.Equals(_otherCopy);

    /// <summary>A plain concurrent-dictionary probe on the same key: the hash is cached, so this is bucket + Equals.</summary>
    /// <remarks>
    /// The floor for "find the entry". The cache's own probe (c5) minus this is everything the cache adds on a hit:
    /// the database in the key, the validity test, the TTL clock read, and the retain/release.
    /// </remarks>
    [Benchmark(Description = "c4. dictionary probe floor")]
    public bool DictionaryProbe() => _baselineDictionary.TryGetValue(_lookup, out _);

    /// <summary>The cache's probe on a pre-hashed key: lookup, validity, TTL, retain - and the release.</summary>
    [Benchmark(Description = "c5. cache probe (pre-hashed)")]
    public bool CacheProbe()
    {
        if (!_cache.TryGet(_lookup, 0, out var payload)) return false;
        payload.Release();
        return true;
    }

    /// <summary>The clock read that the TTL test makes on every hit.</summary>
    [Benchmark(Description = "c6. Stopwatch.GetTimestamp")]
    public long ClockRead() => Stopwatch.GetTimestamp();

    /// <summary>
    /// The coarse alternative for the TTL test: no timer of our own, ~1-4ms resolution on Linux, ~15.6ms on Windows.
    /// </summary>
    [Benchmark(Description = "c6b. Environment.TickCount64")]
#if NET
    public long CoarseClockRead() => Environment.TickCount64;
#else
    public long CoarseClockRead() => Environment.TickCount;
#endif

    /// <summary>The reference taken and dropped on every hit, single-threaded (contention is CacheHitScaling's job).</summary>
    [Benchmark(Description = "c7. retain + release")]
    public bool RetainRelease()
    {
        if (!_payload.TryRetain()) return false;
        _payload.Release();
        return true;
    }

    /// <summary>Parsing the cached reply alone, from the retained payload, exactly as the executor does on a hit.</summary>
    [Benchmark(Description = "c8. parse only")]
    public RedisValue ParseOnly() => RespExecutor.Parse(_handler, _payload);

    /// <summary>Render, hash, and probe the dictionary - everything but reading the reply.</summary>
    [Benchmark(Description = "3. + cache probe")]
    public bool RenderHashProbe()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{_key}");
        if (!_cache.TryGet(frame.AsLookupKey(Readable), 0, out var payload)) return false;

        payload.Dispose();   // TryGet retains on a hit; discarding it would leak a reference per call
        return true;
    }
    // (a build-up decomposition here measured hashing 20 bytes as free and reading one field as 7.5ns)

    /// <summary>Straight to the context, skipping the group accessor and its forwarding.</summary>
    [Benchmark(Description = "5. - group layer (context.SendAsync)")]
    public RedisValue NoGroupLayer()
        => _context.Raw.SendAsync<RedisValue>($"{RedisCommand.GET}{_key}", Readable).GetAwaiter().GetResult();

    /// <summary>The synchronous twin: same work, no ValueTask to build or await.</summary>
    [Benchmark(Description = "6. - ValueTask (context.Send)")]
    public RedisValue NoValueTask()
        => _context.Raw.Send<RedisValue>($"{RedisCommand.GET}{_key}", Readable);

    /// <summary>And with a handler that reads nothing, isolating what parsing the reply costs.</summary>
    [Benchmark(Description = "7. - reply parse (handler returns a constant)")]
    public int NoParse()
        => _context.Raw.Send($"{RedisCommand.GET}{_key}", Readable, ConstantHandler.Instance);

    private sealed class ConstantHandler : IRespHandler<int>
    {
        public static readonly ConstantHandler Instance = new();
        public int Parse(ref RESPite.Messages.RespReader reader) => 1;
    }
}
