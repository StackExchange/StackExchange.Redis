using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using static StackExchange.Redis.RedisValue;

namespace StackExchange.Redis.Tests.Issues;

public class Issue1103Tests : TestBase
{
    public Issue1103Tests(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData(142205255210238005UL, (int)StorageType.Int64)]
    [InlineData(ulong.MaxValue, (int)StorageType.UInt64)]
    [InlineData(ulong.MinValue, (int)StorageType.Int64)]
    [InlineData(0x8000000000000000UL, (int)StorageType.UInt64)]
    [InlineData(0x8000000000000001UL, (int)StorageType.UInt64)]
    [InlineData(0x7FFFFFFFFFFFFFFFUL, (int)StorageType.Int64)]
    public void LargeUInt64StoredCorrectly(ulong value, int storageType)
    {
        using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        RedisValue typed = value;

        // only need UInt64 for 64-bits
        Assert.Equal((StorageType)storageType, typed.Type);
        db.StringSet(key, typed);
        var fromRedis = db.StringGet(key);

        Log($"{fromRedis.Type}: {fromRedis}");
        Assert.Equal(StorageType.Raw, fromRedis.Type);
        Assert.Equal(value, (ulong)fromRedis);
        Assert.Equal(value.ToString(CultureInfo.InvariantCulture), fromRedis.ToString());

        var simplified = fromRedis.Simplify();
        Log($"{simplified.Type}: {simplified}");
        Assert.Equal((StorageType)storageType, typed.Type);
        Assert.Equal(value, (ulong)simplified);
        Assert.Equal(value.ToString(CultureInfo.InvariantCulture), fromRedis.ToString());
    }

    [Fact]
    public void UnusualRedisValueOddities() // things we found while doing this
    {
        RedisValue x = 0, y = "0";
        Assert.Equal(x, y);
        Assert.Equal(y, x);

        y = "-0";
        Assert.Equal(x, y);
        Assert.Equal(y, x);

        y = "-"; // this is the oddness; this used to return true
        Assert.NotEqual(x, y);
        Assert.NotEqual(y, x);

        y = "+";
        Assert.NotEqual(x, y);
        Assert.NotEqual(y, x);

        y = ".";
        Assert.NotEqual(x, y);
        Assert.NotEqual(y, x);
    }
}
