using BenchmarkDotNet.Attributes;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BasicTest;

#pragma warning disable SERED001, SERED002 // Type is for evaluation purposes only

/// <summary>
/// The tests
/// </summary>
[Config(typeof(CustomConfig)), MemoryDiagnoser]
public class ParserBenchmarks : IDisposable
{
    private readonly MemoryStream _source;
    public ParserBenchmarks()
    {
        var data = File.ReadAllBytes(@"ReplayLog\127.0.0.1 6379 Interactive 0.out");
        _source = new MemoryStream(data, 0, data.Length, false, true);
    }

    public void Dispose() => _source.Dispose();


    [Benchmark(Baseline = true)]
    public async Task<int> LegacyParser()
    {
        _source.Position = 0;
        int count = 0;
        Action<RedisResult> callback = msg => count++;

        var total = await LoggingTunnel.ReplayAsync(_source, callback);
        if (total != count) throw new InvalidOperationException();
        return count;
    }

    [Benchmark]
    public async Task<int> NewParser()
    {
        _source.Position = 0;
        int count = 0;
        LoggingTunnel.Message callback = msg => count++;

        var total = await LoggingTunnel.ReplayAsync(_source, callback);
        if (total != count) throw new InvalidOperationException();
        return count;
    }
}
