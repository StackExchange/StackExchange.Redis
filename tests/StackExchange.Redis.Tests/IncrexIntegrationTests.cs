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
        var intKey = (RedisKey)(Me() + ":int");
        var doubleKey = (RedisKey)(Me() + ":double");
        db.KeyDelete([intKey, doubleKey], CommandFlags.FireAndForget);

        var intResult = db.StringIncrement(intKey, 3L, Expiration.Default);
        var doubleResult = db.StringIncrement(doubleKey, 1.5, Expiration.Default);

        Assert.Equal(3, intResult.Value);
        Assert.Equal(3, intResult.AppliedIncrement);
        Assert.Equal(1.5, doubleResult.Value);
        Assert.Equal(1.5, doubleResult.AppliedIncrement);
    }

    [Fact(Timeout = 5000)]
    public async Task StringIncrementIncrex_DefaultRejectsWhenBoundExceeded()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var intKey = (RedisKey)(Me() + ":int");
        var doubleKey = (RedisKey)(Me() + ":double");
        db.KeyDelete([intKey, doubleKey], CommandFlags.FireAndForget);
        db.StringSet(intKey, 5);
        db.StringSet(doubleKey, 5.5);

        var intResult = await db.StringIncrementAsync(intKey, 1L, TimeSpan.FromSeconds(5), lowerBound: 10);
        var doubleResult = await db.StringIncrementAsync(doubleKey, 1.25, TimeSpan.FromSeconds(5), lowerBound: 10.25);

        Assert.Equal(5, intResult.Value);
        Assert.Equal(0, intResult.AppliedIncrement);
        Assert.Equal(5, (long)db.StringGet(intKey));
        Assert.Null(await db.KeyTimeToLiveAsync(intKey));

        Assert.Equal(5.5, doubleResult.Value);
        Assert.Equal(0, doubleResult.AppliedIncrement);
        Assert.Equal(5.5, (double)db.StringGet(doubleKey));
        Assert.Null(await db.KeyTimeToLiveAsync(doubleKey));
    }

    [Fact(Timeout = 5000)]
    public async Task StringIncrementIncrex_SaturateClampsToBound()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var intKey = (RedisKey)(Me() + ":int");
        var doubleKey = (RedisKey)(Me() + ":double");
        db.KeyDelete([intKey, doubleKey], CommandFlags.FireAndForget);
        db.StringSet(intKey, 8);
        db.StringSet(doubleKey, 8.25);

        var intResult = await db.StringIncrementAsync(intKey, 5L, TimeSpan.FromSeconds(5), upperBound: 10, options: IncrementOptions.Saturate);
        var doubleResult = await db.StringIncrementAsync(doubleKey, 5.5, TimeSpan.FromSeconds(5), upperBound: 10.5, options: IncrementOptions.Saturate);

        Assert.Equal(10, intResult.Value);
        Assert.Equal(2, intResult.AppliedIncrement);
        Assert.Equal(10, (long)db.StringGet(intKey));
        Assert.True((await db.KeyTimeToLiveAsync(intKey)) > TimeSpan.Zero);

        Assert.Equal(10.5, doubleResult.Value);
        Assert.Equal(2.25, doubleResult.AppliedIncrement);
        Assert.Equal(10.5, (double)db.StringGet(doubleKey));
        Assert.True((await db.KeyTimeToLiveAsync(doubleKey)) > TimeSpan.Zero);
    }

    [Fact(Timeout = 5000)]
    public async Task StringIncrementIncrex_DefaultRetainsExistingTtl()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var intKey = (RedisKey)(Me() + ":int");
        var doubleKey = (RedisKey)(Me() + ":double");
        db.KeyDelete([intKey, doubleKey], CommandFlags.FireAndForget);
        db.StringSet(intKey, 5, TimeSpan.FromMinutes(5));
        db.StringSet(doubleKey, 5.5, TimeSpan.FromMinutes(5));
        var beforeIntTtl = await db.KeyTimeToLiveAsync(intKey);
        var beforeDoubleTtl = await db.KeyTimeToLiveAsync(doubleKey);

        var intResult = await db.StringIncrementAsync(intKey, 2L, Expiration.Default);
        var doubleResult = await db.StringIncrementAsync(doubleKey, 2.25, Expiration.Default);

        Assert.Equal(7, intResult.Value);
        Assert.Equal(2, intResult.AppliedIncrement);
        var afterIntTtl = await db.KeyTimeToLiveAsync(intKey);
        Assert.NotNull(beforeIntTtl);
        Assert.NotNull(afterIntTtl);
        Assert.True(afterIntTtl <= beforeIntTtl);
        Assert.True(afterIntTtl > TimeSpan.FromMinutes(4));

        Assert.Equal(7.75, doubleResult.Value);
        Assert.Equal(2.25, doubleResult.AppliedIncrement);
        var afterDoubleTtl = await db.KeyTimeToLiveAsync(doubleKey);
        Assert.NotNull(beforeDoubleTtl);
        Assert.NotNull(afterDoubleTtl);
        Assert.True(afterDoubleTtl <= beforeDoubleTtl);
        Assert.True(afterDoubleTtl > TimeSpan.FromMinutes(4));
    }
}
