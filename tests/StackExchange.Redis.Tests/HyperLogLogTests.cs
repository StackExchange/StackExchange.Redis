using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class HyperLogLogTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task SingleKeyLength()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = "hll1";

        db.HyperLogLogAdd(key, "a");
        db.HyperLogLogAdd(key, "b");
        db.HyperLogLogAdd(key, "c");

        Assert.True(db.HyperLogLogLength(key) > 0);
    }

    [Fact]
    public async Task MultiKeyLength()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey[] keys = { "hll1", "hll2", "hll3" };

        db.HyperLogLogAdd(keys[0], "a");
        db.HyperLogLogAdd(keys[1], "b");
        db.HyperLogLogAdd(keys[2], "c");

        Assert.True(db.HyperLogLogLength(keys) > 0);
    }
}
