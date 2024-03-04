using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class ExpiryTests : TestBase
{
    public ExpiryTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

    private static string[]? GetMap(bool disablePTimes) => disablePTimes ? (new[] { "pexpire", "pexpireat", "pttl" }) : null;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestBasicExpiryTimeSpan(bool disablePTimes)
    {
        using var conn = Create(disabledCommands: GetMap(disablePTimes));

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        var a = db.KeyTimeToLiveAsync(key);
        db.KeyExpire(key, TimeSpan.FromHours(1), CommandFlags.FireAndForget);
        var b = db.KeyTimeToLiveAsync(key);
        db.KeyExpire(key, (TimeSpan?)null, CommandFlags.FireAndForget);
        var c = db.KeyTimeToLiveAsync(key);
        db.KeyExpire(key, TimeSpan.FromHours(1.5), CommandFlags.FireAndForget);
        var d = db.KeyTimeToLiveAsync(key);
        db.KeyExpire(key, TimeSpan.MaxValue, CommandFlags.FireAndForget);
        var e = db.KeyTimeToLiveAsync(key);
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var f = db.KeyTimeToLiveAsync(key);

        Assert.Null(await a);
        var time = await b;
        Assert.NotNull(time);
        Assert.True(time > TimeSpan.FromMinutes(59.9) && time <= TimeSpan.FromMinutes(60));
        Assert.Null(await c);
        time = await d;
        Assert.NotNull(time);
        Assert.True(time > TimeSpan.FromMinutes(89.9) && time <= TimeSpan.FromMinutes(90));
        Assert.Null(await e);
        Assert.Null(await f);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestExpiryOptions(bool disablePTimes)
    {
        using var conn = Create(disabledCommands: GetMap(disablePTimes), require: RedisFeatures.v7_0_0_rc1);

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key);
        db.StringSet(key, "value");

        // The key has no expiry
        Assert.False(await db.KeyExpireAsync(key, TimeSpan.FromHours(1), ExpireWhen.HasExpiry));
        Assert.True(await db.KeyExpireAsync(key, TimeSpan.FromHours(1), ExpireWhen.HasNoExpiry));

        // The key has an existing expiry
        Assert.True(await db.KeyExpireAsync(key, TimeSpan.FromHours(1), ExpireWhen.HasExpiry));
        Assert.False(await db.KeyExpireAsync(key, TimeSpan.FromHours(1), ExpireWhen.HasNoExpiry));

        // Set only when the new expiry is greater than current one
        Assert.True(await db.KeyExpireAsync(key, TimeSpan.FromHours(1.5), ExpireWhen.GreaterThanCurrentExpiry));
        Assert.False(await db.KeyExpireAsync(key, TimeSpan.FromHours(0.5), ExpireWhen.GreaterThanCurrentExpiry));

        // Set only when the new expiry is less than current one
        Assert.True(await db.KeyExpireAsync(key, TimeSpan.FromHours(0.5), ExpireWhen.LessThanCurrentExpiry));
        Assert.False(await db.KeyExpireAsync(key, TimeSpan.FromHours(1.5), ExpireWhen.LessThanCurrentExpiry));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task TestBasicExpiryDateTime(bool disablePTimes, bool utc)
    {
        using var conn = Create(disabledCommands: GetMap(disablePTimes));

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var now = utc ? DateTime.UtcNow : DateTime.Now;
        var serverTime = GetServer(conn).Time();
        Log("Server time: {0}", serverTime);
        var offset = DateTime.UtcNow - serverTime;

        Log("Now (local time): {0}", now);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        var a = db.KeyTimeToLiveAsync(key);
        db.KeyExpire(key, now.AddHours(1), CommandFlags.FireAndForget);
        var b = db.KeyTimeToLiveAsync(key);
        db.KeyExpire(key, (DateTime?)null, CommandFlags.FireAndForget);
        var c = db.KeyTimeToLiveAsync(key);
        db.KeyExpire(key, now.AddHours(1.5), CommandFlags.FireAndForget);
        var d = db.KeyTimeToLiveAsync(key);
        db.KeyExpire(key, DateTime.MaxValue, CommandFlags.FireAndForget);
        var e = db.KeyTimeToLiveAsync(key);
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var f = db.KeyTimeToLiveAsync(key);

        Assert.Null(await a);
        var timeResult = await b;
        Assert.NotNull(timeResult);
        TimeSpan time = timeResult.Value;

        // Adjust for server time offset, if any when checking expectations
        time -= offset;

        Log("Time: {0}, Expected: {1}-{2}", time, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(60));
        Assert.True(time >= TimeSpan.FromMinutes(59));
        Assert.True(time <= TimeSpan.FromMinutes(60.1));
        Assert.Null(await c);

        timeResult = await d;
        Assert.NotNull(timeResult);
        time = timeResult.Value;

        Assert.True(time >= TimeSpan.FromMinutes(89));
        Assert.True(time <= TimeSpan.FromMinutes(90.1));
        Assert.Null(await e);
        Assert.Null(await f);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeyExpiryTime(bool disablePTimes)
    {
        using var conn = Create(disabledCommands: GetMap(disablePTimes), require: RedisFeatures.v7_0_0_rc1);

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var expireTime = DateTime.UtcNow.AddHours(1);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        db.KeyExpire(key, expireTime, CommandFlags.FireAndForget);

        var time = db.KeyExpireTime(key);
        Assert.NotNull(time);
        Assert.Equal(expireTime, time!.Value, TimeSpan.FromSeconds(30));

        // Without associated expiration time
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        time = db.KeyExpireTime(key);
        Assert.Null(time);

        // Non existing key
        db.KeyDelete(key, CommandFlags.FireAndForget);
        time = db.KeyExpireTime(key);
        Assert.Null(time);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyExpiryTimeAsync(bool disablePTimes)
    {
        using var conn = Create(disabledCommands: GetMap(disablePTimes), require: RedisFeatures.v7_0_0_rc1);

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var expireTime = DateTime.UtcNow.AddHours(1);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        db.KeyExpire(key, expireTime, CommandFlags.FireAndForget);

        var time = await db.KeyExpireTimeAsync(key);
        Assert.NotNull(time);
        Assert.Equal(expireTime, time.Value, TimeSpan.FromSeconds(30));

        // Without associated expiration time
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        time = await db.KeyExpireTimeAsync(key);
        Assert.Null(time);

        // Non existing key
        db.KeyDelete(key, CommandFlags.FireAndForget);
        time = await db.KeyExpireTimeAsync(key);
        Assert.Null(time);
    }
}
