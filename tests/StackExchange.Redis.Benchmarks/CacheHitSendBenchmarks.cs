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
    [Benchmark(Description = "db.Strings.GetAsync - cache hit")]
    public RedisValue GroupGet() => _context.Strings.GetAsync("k", Readable).GetAwaiter().GetResult();
}
