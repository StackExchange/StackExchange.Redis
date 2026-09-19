using System;
using System.Buffers;
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

    [GlobalSetup]
    public void Setup()
    {
        _cache = new RespClientCache();
        _context = new RespDatabaseContext(new RespContext().WithExecutor(new OneReplyExecutor()).WithCache(_cache));
        _key = new string('k', KeySize);

        // prime it, so every measured call is a hit
        _ = _context.Strings.GetAsync(_key, Readable).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => _cache.Dispose();

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

    /// <summary>Render the frame and throw it away: the rent, the write, and the hash, with no probe.</summary>
    /// <remarks>
    /// Stage 3 minus this is what the dictionary probe costs; this minus stage 1 is what writing and
    /// hashing twenty bytes costs. Measured this way round because a build-up decomposition here
    /// previously reported hashing as free - the JIT had deleted it, since nothing consumed the result.
    /// </remarks>
    [Benchmark(Description = "2. render only (no probe)")]
    public int RenderOnly()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{_key}");
        return frame.ArgCount;   // consumed, so the render cannot be optimised away
    }

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
