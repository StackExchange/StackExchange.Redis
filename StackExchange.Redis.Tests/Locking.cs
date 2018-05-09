using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Locking : TestBase
    {
        protected override string GetConfiguration() => TestConfig.Current.MasterServerAndPort;
        public Locking(ITestOutputHelper output) : base (output) { }

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
            using (var c1 = Create(testMode))
            using (var c2 = Create(testMode))
            {
                void cb(object obj)
                {
                    try
                    {
                        var conn = (IDatabase)obj;
                        conn.Multiplexer.ErrorMessage += delegate { Interlocked.Increment(ref errorCount); };

                        for (int i = 0; i < 1000; i++)
                        {
                            conn.LockTakeAsync("abc", "def", TimeSpan.FromSeconds(5));
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
                ThreadPool.QueueUserWorkItem(cb, c1.GetDatabase(db));
                ThreadPool.QueueUserWorkItem(cb, c2.GetDatabase(db));
                evt.WaitOne(8000);
            }
            Assert.Equal(0, Interlocked.CompareExchange(ref errorCount, 0, 0));
            Assert.Equal(0, bgErrorCount);
        }

        [Fact]
        public void TestOpCountByVersionLocal_UpLevel()
        {
            using (var conn = Create())
            {
                TestLockOpCountByVersion(conn, 1, false);
                TestLockOpCountByVersion(conn, 1, true);

                //TestManualLockOpCountByVersion(conn, 5, false);
                //TestManualLockOpCountByVersion(conn, 3, true);
            }
        }
        //[Test]
        //public void TestOpCountByVersionLocal_DownLevel()
        //{
        //    using (var conn = GetUnsecuredConnection(open: false))
        //    {
        //        conn.SetServerVersion(new Version(2, 6, 0), ServerType.Master);
        //        TestLockOpCountByVersion(conn, 5, false);
        //        TestLockOpCountByVersion(conn, 3, true);
        //        //TestManualLockOpCountByVersion(conn, 5, false);
        //        //TestManualLockOpCountByVersion(conn, 3, true);
        //    }
        //}

        //[Test]
        //public void TestOpCountByVersionRemote()
        //{
        //    using (var conn = GetRemoteConnection(open: false))
        //    {
        //        TestLockOpCountByVersion(conn, 1, false);
        //        TestLockOpCountByVersion(conn, 1, true);
        //        //TestManualLockOpCountByVersion(conn, 1, false);
        //        //TestManualLockOpCountByVersion(conn, 1, true);
        //    }
        //}

        private void TestLockOpCountByVersion(ConnectionMultiplexer conn, int expectedOps, bool existFirst)
        {
            const int DB = 0, LockDuration = 30;
            RedisKey Key = Me();

            var db = conn.GetDatabase(DB);
            db.KeyDelete(Key);
            RedisValue newVal = "us:" + Guid.NewGuid().ToString();
            RedisValue expectedVal = newVal;
            if (existFirst)
            {
                expectedVal = "other:" + Guid.NewGuid().ToString();
                db.StringSet(Key, expectedVal, TimeSpan.FromSeconds(LockDuration));
            }
            long countBefore = GetServer(conn).GetCounters().Interactive.OperationCount;

            var taken = db.LockTake(Key, newVal, TimeSpan.FromSeconds(LockDuration));

            long countAfter = GetServer(conn).GetCounters().Interactive.OperationCount;
            var valAfter = db.StringGet(Key);

            Assert.Equal(!existFirst, taken);
            Assert.Equal(expectedVal, valAfter);
            Assert.Equal(expectedOps, countAfter - countBefore);
            // note we get a ping from GetCounters
        }

        private ConnectionMultiplexer Create(TestMode mode)
        {
            switch (mode)
            {
                case TestMode.MultiExec:
                    return Create();
                case TestMode.NoMultiExec:
                    return Create(disabledCommands: new[] { "multi", "exec" });
                case TestMode.Twemproxy:
                    return Create(proxy: Proxy.Twemproxy);
                default:
                    throw new NotSupportedException(mode.ToString());
            }
        }

        [Theory, MemberData(nameof(TestModes))]
        public void TakeLockAndExtend(TestMode mode)
        {
            bool withTran = mode == TestMode.MultiExec;
            using (var conn = Create(mode))
            {
                RedisValue right = Guid.NewGuid().ToString(),
                    wrong = Guid.NewGuid().ToString();

                int DB = mode == TestMode.Twemproxy ? 0 : 7;
                RedisKey Key = "lock-key";

                var db = conn.GetDatabase(DB);

                db.KeyDelete(Key);

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
                Assert.True(conn.Wait(t1), "1");
                Assert.False(conn.Wait(t1b), "1b");
                Assert.Equal(right, conn.Wait(t2));
                if (withTran) Assert.False(conn.Wait(t3), "3");
                Assert.Equal(right, conn.Wait(t4));
                if (withTran) Assert.False(conn.Wait(t5), "5");
                Assert.Equal(right, conn.Wait(t6));
                var ttl = conn.Wait(t7).Value.TotalSeconds;
                Assert.True(ttl > 0 && ttl <= 20, "7");
                Assert.True(conn.Wait(t8), "8");
                Assert.Equal(right, conn.Wait(t9));
                ttl = conn.Wait(t10).Value.TotalSeconds;
                Assert.True(ttl > 50 && ttl <= 60, "10");
                Assert.True(conn.Wait(t11), "11");
                Assert.Null((string)conn.Wait(t12));
                Assert.True(conn.Wait(t13), "13");
            }
        }

        //public void TestManualLockOpCountByVersion(RedisConnection conn, int expected, bool existFirst)
        //{
        //    const int DB = 0, LockDuration = 30;
        //    const string Key = "TestManualLockOpCountByVersion";
        //    conn.Wait(conn.Open());
        //    conn.Keys.Remove(DB, Key);
        //    var newVal = "us:" + CreateUniqueName();
        //    string expectedVal = newVal;
        //    if (existFirst)
        //    {
        //        expectedVal = "other:" + CreateUniqueName();
        //        conn.Strings.Set(DB, Key, expectedVal, LockDuration);
        //    }
        //    int countBefore = conn.GetCounters().MessagesSent;

        //    var tran = conn.CreateTransaction();
        //    tran.AddCondition(Condition.KeyNotExists(DB, Key));
        //    tran.Strings.Set(DB, Key, newVal, LockDuration);
        //    var taken = conn.Wait(tran.Execute());

        //    int countAfter = conn.GetCounters().MessagesSent;
        //    var valAfter = conn.Wait(conn.Strings.GetString(DB, Key));
        //    Assert.Equal(!existFirst, taken, "lock taken (manual)");
        //    Assert.Equal(expectedVal, valAfter, "taker (manual)");
        //    Assert.Equal(expected, (countAfter - countBefore) - 1, "expected ops (including ping) (manual)");
        //    // note we get a ping from GetCounters
        //}

        [Theory, MemberData(nameof(TestModes))]
        public void TestBasicLockNotTaken(TestMode testMode)
        {
            using (var conn = Create(testMode))
            {
                int errorCount = 0;
                conn.ErrorMessage += delegate { Interlocked.Increment(ref errorCount); };
                Task<bool> taken = null;
                Task<RedisValue> newValue = null;
                Task<TimeSpan?> ttl = null;

                const int LOOP = 50;
                var db = conn.GetDatabase(0);
                for (int i = 0; i < LOOP; i++)
                {
                    db.KeyDeleteAsync("lock-not-exists");
                    taken = db.LockTakeAsync("lock-not-exists", "new-value", TimeSpan.FromSeconds(10));
                    newValue = db.StringGetAsync("lock-not-exists");
                    ttl = db.KeyTimeToLiveAsync("lock-not-exists");
                }
                Assert.True(conn.Wait(taken), "taken");
                Assert.Equal("new-value", (string)conn.Wait(newValue));
                var ttlValue = conn.Wait(ttl).Value.TotalSeconds;
                Assert.True(ttlValue >= 8 && ttlValue <= 10, "ttl");

                Assert.Equal(0, errorCount);
            }
        }

        [Theory, MemberData(nameof(TestModes))]
        public void TestBasicLockTaken(TestMode testMode)
        {
            using (var conn = Create(testMode))
            {
                var db = conn.GetDatabase(0);
                db.KeyDelete("lock-exists");
                db.StringSet("lock-exists", "old-value", TimeSpan.FromSeconds(20));
                var taken = db.LockTakeAsync("lock-exists", "new-value", TimeSpan.FromSeconds(10));
                var newValue = db.StringGetAsync("lock-exists");
                var ttl = db.KeyTimeToLiveAsync("lock-exists");

                Assert.False(conn.Wait(taken), "taken");
                Assert.Equal("old-value", (string)conn.Wait(newValue));
                var ttlValue = conn.Wait(ttl).Value.TotalSeconds;
                Assert.True(ttlValue >= 18 && ttlValue <= 20, "ttl");
            }
        }
    }
}
