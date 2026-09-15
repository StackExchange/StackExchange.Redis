using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis.Benchmarks;

// The invalidation path, which under CLIENT TRACKING BCAST is fed EVERY key touched on the server -
// almost none of which this client has cached. So the number that matters is Invalidate_Miss.
[Config(typeof(CustomConfig))]
[MemoryDiagnoser]
public class ClientCacheBenchmarks
{
    private readonly RespClientCache _cache = new();
    private byte[] _hit = null!;
    private byte[] _miss = null!;
    private byte[] _lookupFrame = null!;

    [Params(1, 1000, 100_000)]
    public int CachedKeys { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var ctx = new RespContext();
        for (var i = 0; i < CachedKeys; i++)
        {
            var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)("key:" + i)}");
            if (_cache.TryBeginFill(ref frame, 0, out var fill))
            {
                var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"));
                _cache.TryComplete(fill, payload);
                payload.Release();
            }
        }

        _hit = Encoding.UTF8.GetBytes("key:0");
        _miss = Encoding.UTF8.GetBytes("some:key:this:client:never:read");
        _lookupFrame = Encoding.UTF8.GetBytes("unused");
        _ = _lookupFrame;
    }

    /// <summary>The broadcasting flood: a key we do not have. This is the common case by a wide margin.</summary>
    [Benchmark(Baseline = true)]
    public bool Invalidate_Miss() => _cache.OnInvalidate(_miss);

    /// <summary>A key we do have - one hash, one bucket read, one store.</summary>
    [Benchmark]
    public bool Invalidate_Hit() => _cache.OnInvalidate(_hit);
}
