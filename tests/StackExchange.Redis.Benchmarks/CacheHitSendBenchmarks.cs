using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using StackExchange.Redis;
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
    private sealed class OneReplyExecutor : IRespExecutor
    {
        private readonly byte[] _reply = Encoding.UTF8.GetBytes("$5\r\nhello\r\n");

        public int Database => 0;

        public RespPayload Send(in RespRequest request) => RespPayload.Create(_reply);

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private RespClientCache _cache = null!;
    private RespDatabaseContext _context;

    [GlobalSetup]
    public void Setup()
    {
        _cache = new RespClientCache();
        _context = new RespDatabaseContext(new RespContext().WithExecutor(new OneReplyExecutor()).WithCache(_cache));

        // prime it, so every measured call is a hit
        _ = _context.Strings.GetAsync("k", Readable).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => _cache.Dispose();

    /// <summary>The whole group-method path, served locally.</summary>
    [Benchmark(Baseline = true, Description = "4. full  db.Strings.GetAsync")]
    public RedisValue GroupGet() => _context.Strings.GetAsync("k", Readable).GetAwaiter().GetResult();

    // ---- the same path, taken apart, so the total can be attributed rather than guessed at ----

    /// <summary>Just composing the frame: rent, write RESP, compute the slot and key marks, return it.</summary>
    [Benchmark(Description = "1. render only")]
    public int RenderOnly()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        return frame.ArgCount;
    }

    /// <summary>The same, consuming nothing from the frame - isolating what reading a field costs.</summary>
    [Benchmark(Description = "1b. render, consume nothing")]
    public int RenderConsumeNothing()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        return 0;
    }

    /// <summary>Render and read Slot instead of ArgCount, in case the member matters.</summary>
    [Benchmark(Description = "1c. render, read Slot")]
    public int RenderReadSlot()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        return frame.Slot;
    }

    /// <summary>Render, then borrow it as a lookup key - which is where the payload is hashed.</summary>
    [Benchmark(Description = "2. + AsLookupKey (hashes the bytes)")]
    public int RenderAndHash()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        return frame.AsLookupKey(Readable).GetHashCode();
    }

    /// <summary>Render, hash, and probe the dictionary - everything but reading the reply.</summary>
    [Benchmark(Description = "3. + cache probe")]
    public bool RenderHashProbe()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        return _cache.TryGet(frame.AsLookupKey(Readable), 0, out _);
    }
}
