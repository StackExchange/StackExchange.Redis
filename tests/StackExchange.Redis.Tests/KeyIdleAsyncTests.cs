using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class KeyIdleAsyncTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    // Target the standalone secure server (6381) rather than the default primary (6379).
    // OBJECT IDLETIME is reset via the value's LRU access clock, but Redis deliberately
    // suppresses that update while a save/AOF/replication-sync child is active (copy-on-write
    // avoidance). The default primary has a replica attached, and every replica full-sync forks
    // such a child; if one overlaps a touch/read in these tests, the idle time isn't reset and the
    // test flakes (observed on Windows CI). 6381 has no replica and no persistence, so no fork ever
    // suppresses the reset and the behaviour is deterministic. Overriding GetConfiguration here also
    // opts these tests out of the shared connection fixture, giving each a dedicated 6381 connection.
    protected override string GetConfiguration()
        => TestConfig.Current.SecureServerAndPort + ",password=" + TestConfig.Current.SecurePassword;

    [Fact]
    public async Task IdleTimeAsync()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        var timer = Stopwatch.StartNew();
        await Task.Delay(2000).ForAwait();
        var idleTime = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime > TimeSpan.Zero, $"First check: {idleTime} should be > 0; elapsed: {timer.ElapsedMilliseconds}ms");

        db.StringSet(key, "new value2", flags: CommandFlags.FireAndForget);
        var idleTime2 = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime2 < idleTime, $"Second check: {idleTime2} should be < {idleTime}; elapsed: {timer.ElapsedMilliseconds}ms");

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
        var timer = Stopwatch.StartNew();
        await Task.Delay(2000).ForAwait();
        var idleTime = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime > TimeSpan.Zero, $"First check: {idleTime} should be > 0; elapsed: {timer.ElapsedMilliseconds}ms");

        Assert.True(await db.KeyTouchAsync(key).ForAwait(), $"Second check: should be True; elapsed: {timer.ElapsedMilliseconds}ms");
        var idleTime1 = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime1 < idleTime, $"Third check: {idleTime1} should be < {idleTime}; elapsed: {timer.ElapsedMilliseconds}ms");
    }
}
