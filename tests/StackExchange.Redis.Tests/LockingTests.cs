using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class LockingTests : TestBase
{
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort;
    public LockingTests(ITestOutputHelper output) : base (output) { }

    public enum TestMode
    {
        MultiExec,
        NoMultiExec,
        Twemproxy
    }

    public static IEnumerable<object[]> TestModes()
    {
        yield return new object[] { TestMode.MultiExec };
        yield return new object[] { TestMode.NoMultiExec };
        yield return new object[] { TestMode.Twemproxy };
    }

    [Theory, MemberData(nameof(TestModes))]
    public void AggressiveParallel(TestMode testMode)
    {
        int count = 2;
        int errorCount = 0;
        int bgErrorCount = 0;
        var evt = new ManualResetEvent(false);
        var key = Me() + testMode;
        using (var conn1 = Create(testMode))
        using (var conn2 = Create(testMode))
        {
            void cb(object? obj)
            {
                try
                {
                    var conn = (IDatabase?)obj!;
                    conn.Multiplexer.ErrorMessage += delegate { Interlocked.Increment(ref errorCount); };

                    for (int i = 0; i < 1000; i++)
                    {
                        conn.LockTakeAsync(key, "def", TimeSpan.FromSeconds(5));
                    }
                    conn.Ping();
                    if (Interlocked.Decrement(ref count) == 0) evt.Set();
                }
                catch
                {
                    Interlocked.Increment(ref bgErrorCount);
                }
            }
            int db = testMode == TestMode.Twemproxy ? 0 : 2;
            ThreadPool.QueueUserWorkItem(cb, conn1.GetDatabase(db));
            ThreadPool.QueueUserWorkItem(cb, conn2.GetDatabase(db));
            evt.WaitOne(8000);
        }
        Assert.Equal(0, Interlocked.CompareExchange(ref errorCount, 0, 0));
        Assert.Equal(0, bgErrorCount);
    }

    [Fact]
    public void TestOpCountByVersionLocal_UpLevel()
    {
        using var conn = Create(shared: false);

        TestLockOpCountByVersion(conn, 1, false);
        TestLockOpCountByVersion(conn, 1, true);
    }

    private void TestLockOpCountByVersion(IConnectionMultiplexer conn, int expectedOps, bool existFirst)
    {
        const int LockDuration = 30;
        RedisKey Key = Me();

        var db = conn.GetDatabase();
        db.KeyDelete(Key, CommandFlags.FireAndForget);
        RedisValue newVal = "us:" + Guid.NewGuid().ToString();
        RedisValue expectedVal = newVal;
        if (existFirst)
        {
            expectedVal = "other:" + Guid.NewGuid().ToString();
            db.StringSet(Key, expectedVal, TimeSpan.FromSeconds(LockDuration), flags: CommandFlags.FireAndForget);
        }
        long countBefore = GetServer(conn).GetCounters().Interactive.OperationCount;

        var taken = db.LockTake(Key, newVal, TimeSpan.FromSeconds(LockDuration));

        long countAfter = GetServer(conn).GetCounters().Interactive.OperationCount;
        var valAfter = db.StringGet(Key);

        Assert.Equal(!existFirst, taken);
        Assert.Equal(expectedVal, valAfter);
        // note we get a ping from GetCounters
        Assert.True(countAfter - countBefore >= expectedOps, $"({countAfter} - {countBefore}) >= {expectedOps}");
    }

    private IConnectionMultiplexer Create(TestMode mode) => mode switch
    {
        TestMode.MultiExec => Create(),
        TestMode.NoMultiExec => Create(disabledCommands: new[] { "multi", "exec" }),
        TestMode.Twemproxy => Create(proxy: Proxy.Twemproxy),
        _ => throw new NotSupportedException(mode.ToString()),
    };

    [Theory, MemberData(nameof(TestModes))]
    public async Task TakeLockAndExtend(TestMode testMode)
    {
        using var conn = Create(testMode);

        RedisValue right = Guid.NewGuid().ToString(),
            wrong = Guid.NewGuid().ToString();

        int DB = testMode == TestMode.Twemproxy ? 0 : 7;
        RedisKey Key = Me() + testMode;

        var db = conn.GetDatabase(DB);

        db.KeyDelete(Key, CommandFlags.FireAndForget);

        bool withTran = testMode == TestMode.MultiExec;
        var t1 = db.LockTakeAsync(Key, right, TimeSpan.FromSeconds(20));
        var t1b = db.LockTakeAsync(Key, wrong, TimeSpan.FromSeconds(10));
        var t2 = db.LockQueryAsync(Key);
        var t3 = withTran ? db.LockReleaseAsync(Key, wrong) : null;
        var t4 = db.LockQueryAsync(Key);
        var t5 = withTran ? db.LockExtendAsync(Key, wrong, TimeSpan.FromSeconds(60)) : null;
        var t6 = db.LockQueryAsync(Key);
        var t7 = db.KeyTimeToLiveAsync(Key);
        var t8 = db.LockExtendAsync(Key, right, TimeSpan.FromSeconds(60));
        var t9 = db.LockQueryAsync(Key);
        var t10 = db.KeyTimeToLiveAsync(Key);
        var t11 = db.LockReleaseAsync(Key, right);
        var t12 = db.LockQueryAsync(Key);
        var t13 = db.LockTakeAsync(Key, wrong, TimeSpan.FromSeconds(10));

        Assert.NotEqual(default(RedisValue), right);
        Assert.NotEqual(default(RedisValue), wrong);
        Assert.NotEqual(right, wrong);
        Assert.True(await t1, "1");
        Assert.False(await t1b, "1b");
        Assert.Equal(right, await t2);
        if (withTran) Assert.False(await t3!, "3");
        Assert.Equal(right, await t4);
        if (withTran) Assert.False(await t5!, "5");
        Assert.Equal(right, await t6);
        var ttl = (await t7).Value.TotalSeconds;
        Assert.True(ttl > 0 && ttl <= 20, "7");
        Assert.True(await t8, "8");
        Assert.Equal(right, await t9);
        ttl = (await t10).Value.TotalSeconds;
        Assert.True(ttl > 50 && ttl <= 60, "10");
        Assert.True(await t11, "11");
        Assert.Null((string?)await t12);
        Assert.True(await t13, "13");
    }

    [Theory, MemberData(nameof(TestModes))]
    public async Task TestBasicLockNotTaken(TestMode testMode)
    {
        using var conn = Create(testMode);

        int errorCount = 0;
        conn.ErrorMessage += delegate { Interlocked.Increment(ref errorCount); };
        Task<bool>? taken = null;
        Task<RedisValue>? newValue = null;
        Task<TimeSpan?>? ttl = null;

        const int LOOP = 50;
        var db = conn.GetDatabase();
        var key = Me() + testMode;
        for (int i = 0; i < LOOP; i++)
        {
            _ = db.KeyDeleteAsync(key);
            taken = db.LockTakeAsync(key, "new-value", TimeSpan.FromSeconds(10));
            newValue = db.StringGetAsync(key);
            ttl = db.KeyTimeToLiveAsync(key);
        }
        Assert.True(await taken!, "taken");
        Assert.Equal("new-value", await newValue!);
        var ttlValue = (await ttl!).Value.TotalSeconds;
        Assert.True(ttlValue >= 8 && ttlValue <= 10, "ttl");

        Assert.Equal(0, errorCount);
    }

    [Theory, MemberData(nameof(TestModes))]
    public async Task TestBasicLockTaken(TestMode testMode)
    {
        using var conn = Create(testMode);

        var db = conn.GetDatabase();
        var key = Me() + testMode;
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "old-value", TimeSpan.FromSeconds(20), flags: CommandFlags.FireAndForget);
        var taken = db.LockTakeAsync(key, "new-value", TimeSpan.FromSeconds(10));
        var newValue = db.StringGetAsync(key);
        var ttl = db.KeyTimeToLiveAsync(key);

        Assert.False(await taken, "taken");
        Assert.Equal("old-value", await newValue);
        var ttlValue = (await ttl).Value.TotalSeconds;
        Assert.True(ttlValue >= 18 && ttlValue <= 20, "ttl");
    }
}
