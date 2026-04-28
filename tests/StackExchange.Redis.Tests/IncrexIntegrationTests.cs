using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class IncrexIntegrationTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact(Timeout = 5000)]
    public async Task StringIncrementIncrex_Int64_WithBoundsAndExpiry()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, 10);

        var result = await db.StringIncrementAsync(key, 2L, TimeSpan.FromSeconds(5), lowerBound: 0, upperBound: 20);

        Assert.Equal(12, result.Value);
        Assert.Equal(2, result.AppliedIncrement);
        Assert.Equal(12, (long)db.StringGet(key));
        Assert.True((await db.KeyTimeToLiveAsync(key)) > TimeSpan.Zero);
    }

    [Fact(Timeout = 5000)]
    public async Task StringIncrementIncrex_Double_WithAbsoluteExpiryAndEnx()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var key = Me();
        var when = DateTime.UtcNow.AddMinutes(30).AddMilliseconds(14);
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, 3.25, TimeSpan.FromMinutes(10));
        var beforeTtl = await db.KeyTimeToLiveAsync(key);

        var result = await db.StringIncrementAsync(key, 1.25, new Expiration(when, ExpirationFlags.ExpireIfNotExists), lowerBound: -1.5, upperBound: 9.5);

        Assert.Equal(4.5, result.Value);
        Assert.Equal(1.25, result.AppliedIncrement);
        Assert.Equal(4.5, (double)db.StringGet(key));
        var afterTtl = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(beforeTtl);
        Assert.NotNull(afterTtl);
        Assert.True(afterTtl <= beforeTtl);
        Assert.True(afterTtl > TimeSpan.FromMinutes(8));
    }

    [Fact(Timeout = 5000)]
    public async Task StringIncrementIncrex_SyncVersion_ParsesResult()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var result = db.StringIncrement(key, 3L, Expiration.Default);

        Assert.Equal(3, result.Value);
        Assert.Equal(3, result.AppliedIncrement);
    }

    [Fact(Timeout = 5000)]
    public async Task StringIncrementIncrex_SkipStillAppliesExpiry()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, 5);

        var result = await db.StringIncrementAsync(key, 1L, TimeSpan.FromSeconds(5), lowerBound: 10);

        Assert.Equal(5, result.Value);
        Assert.Equal(0, result.AppliedIncrement);
        Assert.True((await db.KeyTimeToLiveAsync(key)) > TimeSpan.Zero);
    }

    [Fact(Timeout = 5000)]
    public async Task StringIncrementIncrex_DefaultClearsExistingTtl()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, 5, TimeSpan.FromMinutes(5));

        var result = await db.StringIncrementAsync(key, 2L, Expiration.Default);

        Assert.Equal(7, result.Value);
        Assert.Equal(2, result.AppliedIncrement);
        Assert.Null(await db.KeyTimeToLiveAsync(key));
    }
}
