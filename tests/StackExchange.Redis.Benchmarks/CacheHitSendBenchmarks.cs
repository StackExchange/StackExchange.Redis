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





    /// <summary>Render, hash, and probe the dictionary - everything but reading the reply.</summary>
    [Benchmark(Description = "3. + cache probe")]
    public bool RenderHashProbe()
    {
        using var frame = _context.Raw.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        if (!_cache.TryGet(frame.AsLookupKey(Readable), 0, out var payload)) return false;

        payload.Dispose();   // TryGet retains on a hit; discarding it would leak a reference per call
        return true;
    }
    // (a build-up decomposition here measured hashing 20 bytes as free and reading one field as 7.5ns)

    /// <summary>Straight to the context, skipping the group accessor and its forwarding.</summary>
    [Benchmark(Description = "5. - group layer (context.SendAsync)")]
    public RedisValue NoGroupLayer()
        => _context.Raw.SendAsync<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable).GetAwaiter().GetResult();

    /// <summary>The synchronous twin: same work, no ValueTask to build or await.</summary>
    [Benchmark(Description = "6. - ValueTask (context.Send)")]
    public RedisValue NoValueTask()
        => _context.Raw.Send<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

    /// <summary>And with a handler that reads nothing, isolating what parsing the reply costs.</summary>
    [Benchmark(Description = "7. - reply parse (handler returns a constant)")]
    public int NoParse()
        => _context.Raw.Send($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, ConstantHandler.Instance);

    private sealed class ConstantHandler : IRespHandler<int>
    {
        public static readonly ConstantHandler Instance = new();
        public int Parse(ref RESPite.Messages.RespReader reader) => 1;
    }
}
