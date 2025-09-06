using RESPite.StackExchange.Redis;
using StackExchange.Redis;

namespace RESPite.Benchmark;

public sealed class BridgeBenchmark(string[] args) : OldCoreBenchmarkBase(args)
{
    public override string ToString() => "bridge SE.Redis";
    protected override IConnectionMultiplexer Create(int port)
    {
        var obj = new RespMultiplexer();
        obj.Connect($"127.0.0.1:{Port}");
        return obj;
    }
}
