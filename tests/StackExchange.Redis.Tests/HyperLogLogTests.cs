using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class Resp2HyperLogLogTests : HyperLogLogTests
{
    public Resp2HyperLogLogTests(ITestOutputHelper output, ProtocolDependentFixture fixture) : base(output, fixture, false) { }
}
public class Resp3HyperLogLogTests : HyperLogLogTests
{
    public Resp3HyperLogLogTests(ITestOutputHelper output, ProtocolDependentFixture fixture) : base(output, fixture, true) { }
}
public abstract class HyperLogLogTests : ProtocolFixedTestBase
{
    public HyperLogLogTests(ITestOutputHelper output, ProtocolDependentFixture fixture, bool resp3) : base(output, fixture, resp3) { }

    [Fact]
    public void SingleKeyLength()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = "hll1";

        db.HyperLogLogAdd(key, "a");
        db.HyperLogLogAdd(key, "b");
        db.HyperLogLogAdd(key, "c");

        Assert.True(db.HyperLogLogLength(key) > 0);
    }

    [Fact]
    public void MultiKeyLength()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey[] keys = { "hll1", "hll2", "hll3" };

        db.HyperLogLogAdd(keys[0], "a");
        db.HyperLogLogAdd(keys[1], "b");
        db.HyperLogLogAdd(keys[2], "c");

        Assert.True(db.HyperLogLogLength(keys) > 0);
    }
}
