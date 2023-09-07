using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class BitTests : TestBase
{
    public BitTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

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
