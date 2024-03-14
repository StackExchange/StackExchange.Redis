using BenchmarkDotNet.Attributes;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;
using System;
using System.Threading.Tasks;

namespace BasicTest;

#pragma warning disable SERED001, SERED002 // Type is for evaluation purposes only

/// <summary>
/// The tests
/// </summary>
[Config(typeof(CustomConfig)), MemoryDiagnoser]
public class ParserBenchmarks
{

    [Benchmark(Baseline = true)]
    public async Task<int> LegacyParser()
    {
        int count = 0;
        Action<RedisResult, RedisResult> callback = (req, resp) => count++;

        var total = await LoggingTunnel.ReplayAsync(@"C:\Code\RedisLog", callback);
        if (total != count) throw new InvalidOperationException();
        return count;
    }

    [Benchmark]
    public async Task<int> NewParser()
    {
        int count = 0;
        LoggingTunnel.MessagePair callback = (req, resp) => count++;

        var total = await LoggingTunnel.ReplayAsync(@"C:\Code\RedisLog", callback);
        if (total != count) throw new InvalidOperationException();
        return count;
    }
}
