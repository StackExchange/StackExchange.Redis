using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class Resp2BitTests : BitTests
{
    public Resp2BitTests(ITestOutputHelper output, ProtocolDependentFixture fixture) : base(output, fixture, false) { }
}
public class Resp3BitTests : BitTests
{
    public Resp3BitTests(ITestOutputHelper output, ProtocolDependentFixture fixture) : base(output, fixture, true) { }
}
public abstract class BitTests : ProtocolFixedTestBase
{
    public BitTests(ITestOutputHelper output, ProtocolDependentFixture fixture, bool resp3) : base (output, fixture, resp3) { }

    [Fact]
    public void BasicOps()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSetBit(key, 10, true);
        Assert.True(db.StringGetBit(key, 10));
        Assert.False(db.StringGetBit(key, 11));
    }
}
