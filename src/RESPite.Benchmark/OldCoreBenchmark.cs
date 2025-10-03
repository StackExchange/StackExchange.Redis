using StackExchange.Redis;

namespace RESPite.Benchmark;

public sealed class OldCoreBenchmark(string[] args) : OldCoreBenchmarkBase(args)
{
    public override string ToString() => "legacy SE.Redis";
    protected override IConnectionMultiplexer Create(int port) => ConnectionMultiplexer.Connect($"127.0.0.1:{Port}");
}
