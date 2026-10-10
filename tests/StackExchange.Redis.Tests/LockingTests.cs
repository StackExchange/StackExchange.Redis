using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class LockingTests(ITestOutputHelper output) : TestBase(output)
{
    public enum TestMode
    {
        MultiExec,
        NoMultiExec,
        Twemproxy,
    }

    public static IEnumerable<TheoryDataRow<TestMode>> TestModes()
    {
        yield return new(TestMode.MultiExec);
        yield return new(TestMode.NoMultiExec);
        yield return new(TestMode.Twemproxy);
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
            void Inner(object? obj)
            {
                try
                {
                    var conn = (IDatabase?)obj!;
                    conn.Multiplexer.ErrorMessage += (sender, e) => Interlocked.Increment(ref errorCount);

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
            ThreadPool.QueueUserWorkItem(Inner, GetDatabase(conn1, db));
            ThreadPool.QueueUserWorkItem(Inner, GetDatabase(conn2, db));
            evt.WaitOne(8000);
        }
        Assert.Equal(0, Volatile.Read(ref errorCount));
        Assert.Equal(0, bgErrorCount);
    }

    [Fact]
    public async Task TestOpCountByVersionLocal_UpLevel()
    {
        await using var conn = Create(shared: false);

        TestLockOpCountByVersion(conn, 1, false);
        TestLockOpCountByVersion(conn, 1, true);
    }

    private void TestLockOpCountByVersion(IConnectionMultiplexer conn, int expectedOps, bool existFirst)
    {
        if (!CountsMultiplexerOps)
        {
            Assert.Skip("This database's core is not the multiplexer's own, so its counters do not move.");
        }

        const int LockDuration = 30;
        RedisKey key = Me();

        var db = GetDatabase(conn);
        db.KeyDelete(key, CommandFlags.FireAndForget);
        RedisValue newVal = "us:" + Guid.NewGuid().ToString();
        RedisValue expectedVal = newVal;
        if (existFirst)
        {
            expectedVal = "other:" + Guid.NewGuid().ToString();
            db.StringSet(key, expectedVal, TimeSpan.FromSeconds(LockDuration), flags: CommandFlags.FireAndForget);
        }
        long countBefore = GetServer(conn).GetCounters().Interactive.OperationCount;

        var taken = db.LockTake(key, newVal, TimeSpan.FromSeconds(LockDuration));

        long countAfter = GetServer(conn).GetCounters().Interactive.OperationCount;
        var valAfter = db.StringGet(key);

        Assert.Equal(!existFirst, taken);
        Assert.Equal(expectedVal, valAfter);
        // note we get a ping from GetCounters
        Assert.True(countAfter - countBefore >= expectedOps, $"({countAfter} - {countBefore}) >= {expectedOps}");
    }

    /// <summary>Whether operations issued by this database are counted by the multiplexer.</summary>
    /// <remarks>
    /// <b>Not a statement about the new core, which IS counted</b> - <c>ServerEndPoint.GetCounters</c> folds
    /// its op, socket and queue counts in alongside the bridge's, because under the engine flag those are
    /// where the commands actually are. It is a statement about the suites that reach the new core through a
    /// core of their OWN, built beside the multiplexer's rather than being it: nothing has told the
    /// multiplexer that core exists, so nothing can count it. Those suites exist to exercise the surface
    /// without the flag set, and this is the one assertion that cannot survive the arrangement.
    /// </remarks>
    protected virtual bool CountsMultiplexerOps => true;

    /// <summary>Whether the database under test can run against a <see cref="Proxy.Twemproxy"/> connection.</summary>
    /// <remarks>
    /// False for the new core, which builds its own connections and has no proxy handling yet - a real
    /// gap, and one this suite is the right place to notice, but not a defect in the locks it is testing.
    /// </remarks>
    protected virtual bool SupportsProxy => true;

    private IConnectionMultiplexer Create(TestMode mode)
    {
        if (mode == TestMode.Twemproxy && !SupportsProxy)
        {
            Assert.Skip("This database implementation does not support proxy connections.");
        }

        return mode switch
        {
            TestMode.MultiExec => Create(),
            TestMode.NoMultiExec => Create(disabledCommands: ["multi", "exec"]),
            TestMode.Twemproxy => Create(proxy: Proxy.Twemproxy),
            _ => throw new NotSupportedException(mode.ToString()),
        };
    }

    [Theory, MemberData(nameof(TestModes))]
    public async Task TakeLockAndExtend(TestMode testMode)
    {
        await using var conn = Create(testMode);

        RedisValue right = Guid.NewGuid().ToString(),
            wrong = Guid.NewGuid().ToString();

        int dbId = testMode == TestMode.Twemproxy ? 0 : 7;
        RedisKey key = Me() + testMode;

        var db = GetDatabase(conn, dbId);

        db.KeyDelete(key, CommandFlags.FireAndForget);

        bool withTran = testMode == TestMode.MultiExec;
        var t1 = db.LockTakeAsync(key, right, TimeSpan.FromSeconds(20));
        var t1b = db.LockTakeAsync(key, wrong, TimeSpan.FromSeconds(10));
        var t2 = db.LockQueryAsync(key);
        var t3 = withTran ? db.LockReleaseAsync(key, wrong) : null;
        var t4 = db.LockQueryAsync(key);
        var t5 = withTran ? db.LockExtendAsync(key, wrong, TimeSpan.FromSeconds(60)) : null;
        var t6 = db.LockQueryAsync(key);
        var t7 = db.KeyTimeToLiveAsync(key);
        var t8 = db.LockExtendAsync(key, right, TimeSpan.FromSeconds(60));
        var t9 = db.LockQueryAsync(key);
        var t10 = db.KeyTimeToLiveAsync(key);
        var t11 = db.LockReleaseAsync(key, right);
        var t12 = db.LockQueryAsync(key);
        var t13 = db.LockTakeAsync(key, wrong, TimeSpan.FromSeconds(10));

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
        var ttl = (await t7)!.Value.TotalSeconds;
        Assert.True(ttl > 0 && ttl <= 20, "7");
        Assert.True(await t8, "8");
        Assert.Equal(right, await t9);
        ttl = (await t10)!.Value.TotalSeconds;
        Assert.True(ttl > 50 && ttl <= 60, "10");
        Assert.True(await t11, "11");
        Assert.Null((string?)await t12);
        Assert.True(await t13, "13");
    }

    [Theory, MemberData(nameof(TestModes))]
    public async Task TestBasicLockNotTaken(TestMode testMode)
    {
        await using var conn = Create(testMode);

        int errorCount = 0;
        conn.ErrorMessage += (sender, e) => Interlocked.Increment(ref errorCount);
        Task<bool>? taken = null;
        Task<RedisValue>? newValue = null;
        Task<TimeSpan?>? ttl = null;

        const int LOOP = 50;
        var db = GetDatabase(conn);
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
        var ttlValue = (await ttl!)!.Value.TotalSeconds;
        Assert.True(ttlValue >= 8 && ttlValue <= 10, "ttl");

        Assert.Equal(0, errorCount);
    }

    [Theory, MemberData(nameof(TestModes))]
    public async Task TestBasicLockTaken(TestMode testMode)
    {
        await using var conn = Create(testMode);

        var db = GetDatabase(conn);
        var key = Me() + testMode;
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "old-value", TimeSpan.FromSeconds(20), flags: CommandFlags.FireAndForget);
        var taken = db.LockTakeAsync(key, "new-value", TimeSpan.FromSeconds(10));
        var newValue = db.StringGetAsync(key);
        var ttl = db.KeyTimeToLiveAsync(key);

        Assert.False(await taken, "taken");
        Assert.Equal("old-value", await newValue);
        var ttlValue = (await ttl)!.Value.TotalSeconds;
        Assert.True(ttlValue >= 18 && ttlValue <= 20, "ttl");
    }
}
