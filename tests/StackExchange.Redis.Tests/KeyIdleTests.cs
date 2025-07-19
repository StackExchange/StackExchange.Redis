using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class KeyIdleTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task IdleTime()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        await Task.Delay(2000).ForAwait();
        var idleTime = db.KeyIdleTime(key);
        Assert.True(idleTime > TimeSpan.Zero);

        db.StringSet(key, "new value2", flags: CommandFlags.FireAndForget);
        var idleTime2 = db.KeyIdleTime(key);
        Assert.True(idleTime2 < idleTime);

        db.KeyDelete(key);
        var idleTime3 = db.KeyIdleTime(key);
        Assert.Null(idleTime3);
    }

    [Fact]
    public async Task TouchIdleTime()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_1);

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        await Task.Delay(2000).ForAwait();
        var idleTime = db.KeyIdleTime(key);
        Assert.True(idleTime > TimeSpan.Zero, "First check");

        Assert.True(db.KeyTouch(key), "Second check");
        var idleTime1 = db.KeyIdleTime(key);
        Assert.True(idleTime1 < idleTime, "Third check");
    }

    [Fact]
    public async Task IdleTimeAsync()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        await Task.Delay(2000).ForAwait();
        var idleTime = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime > TimeSpan.Zero, "First check");

        db.StringSet(key, "new value2", flags: CommandFlags.FireAndForget);
        var idleTime2 = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime2 < idleTime, "Second check");

        db.KeyDelete(key);
        var idleTime3 = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.Null(idleTime3);
    }

    [Fact]
    public async Task TouchIdleTimeAsync()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_1);

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        await Task.Delay(2000).ForAwait();
        var idleTime = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime > TimeSpan.Zero, "First check");

        Assert.True(await db.KeyTouchAsync(key).ForAwait(), "Second check");
        var idleTime1 = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime1 < idleTime, "Third check");
    }
}
