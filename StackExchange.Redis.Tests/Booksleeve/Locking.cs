using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class Locking : BookSleeveTestBase
    {
        public Locking(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void AggressiveParallel()
        {
            int count = 2;
            int errorCount = 0;

            var evt = new ManualResetEvent(false);
            using (var c1 = GetUnsecuredConnection(waitForOpen: true))
            using (var c2 = GetUnsecuredConnection(waitForOpen: true))
            {
                void cb(object obj)
                {
                    var conn = (ConnectionMultiplexer)obj;
                    conn.InternalError += (o, e) =>
                    {
                        Output.WriteLine("Error: " + e.Exception);
                        Interlocked.Increment(ref errorCount);
                    };
                    conn.ErrorMessage += (o, e) =>
                    {
                        Output.WriteLine("Message: " + e.Message);
                        Interlocked.Increment(ref errorCount);
                    };
                    var db = conn.GetDatabase(2);
                    for (int i = 0; i < 1000; i++)
                    {
                        db.LockTake("abc", "def", TimeSpan.FromSeconds(5));
                    }
                    db.Ping();
                    conn.Close(false);
                    if (Interlocked.Decrement(ref count) == 0) evt.Set();
                }
                ThreadPool.QueueUserWorkItem(cb, c1);
                ThreadPool.QueueUserWorkItem(cb, c2);
                evt.WaitOne(8000);
            }
            Assert.Equal(0, Interlocked.CompareExchange(ref errorCount, 0, 0));
        }

        [Fact]
        public void TestOpCountByVersionLocal_UpLevel()
        {
            using (var conn = GetUnsecuredConnection(open: false))
            {
                TestLockOpCountByVersion(conn, 1, false);
                TestLockOpCountByVersion(conn, 1, true);
                //TestManualLockOpCountByVersion(conn, 5, false);
                //TestManualLockOpCountByVersion(conn, 3, true);
            }
        }
        //[Fact]
        //public void TestOpCountByVersionLocal_DownLevel()
        //{
        //    var config = new ConfigurationOptions
        //    {
        //        EndPoints = { { LocalHost } },
        //        DefaultVersion = new Version(2, 6, 0),
        //        CommandMap = CommandMap.Create(
        //            new HashSet<string> { "info" }, false)
        //    };
        //    using (var conn = ConnectionMultiplexer.Connect(config))
        //    {
        //        TestLockOpCountByVersion(conn, 5, false);
        //        TestLockOpCountByVersion(conn, 3, true);
        //        //TestManualLockOpCountByVersion(conn, 5, false);
        //        //TestManualLockOpCountByVersion(conn, 3, true);
        //    }
        //}

        //[Fact]
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
        private void TestLockOpCountByVersion(ConnectionMultiplexer conn, int expected, bool existFirst)
        {
            const int DB = 0, LockDuration = 30;
            const string Key = "TestOpCountByVersion";

            var db = conn.GetDatabase(DB);
            db.KeyDelete(Key);
            var newVal = "us:" + CreateUniqueName();
            string expectedVal = newVal;
            if (existFirst)
            {
                expectedVal = "other:" + CreateUniqueName();
                db.StringSet(Key, expectedVal, TimeSpan.FromSeconds(LockDuration));
            }
            long countBefore = conn.GetCounters().Interactive.OperationCount;
            var taken = db.LockTake(Key, newVal, TimeSpan.FromSeconds(LockDuration));
            long countAfter = conn.GetCounters().Interactive.OperationCount;
            string valAfter = db.StringGet(Key);
            Assert.Equal(!existFirst, taken); // lock taken
            Assert.Equal(expectedVal, valAfter); // taker
            Output.WriteLine("{0} ops before, {1} ops after", countBefore, countAfter);
            Assert.Equal(expected, countAfter - countBefore); // expected ops (including ping)
            // note we get a ping from GetCounters
        }

        [Fact]
        public void TakeLockAndExtend()
        {
            using (var conn = GetUnsecuredConnection())
            {
                string right = Guid.NewGuid().ToString(),
                    wrong = Guid.NewGuid().ToString();

                const int DB = 58;
                const string Key = "lock-key";

                //conn.SuspendFlush();
                var db = conn.GetDatabase(DB);

                db.KeyDelete(Key);
                var t1 = db.LockTakeAsync(Key, right, TimeSpan.FromSeconds(20));
                var t1b = db.LockTakeAsync(Key, wrong, TimeSpan.FromSeconds(10));
                var t2 = db.StringGetAsync(Key);
                var t3 = db.LockReleaseAsync(Key, wrong);
                var t4 = db.StringGetAsync(Key);
                var t5 = db.LockExtendAsync(Key, wrong, TimeSpan.FromSeconds(60));
                var t6 = db.StringGetAsync(Key);
                var t7 = db.KeyTimeToLiveAsync(Key);
                var t8 = db.LockExtendAsync(Key, right, TimeSpan.FromSeconds(60));
                var t9 = db.StringGetAsync(Key);
                var t10 = db.KeyTimeToLiveAsync(Key);
                var t11 = db.LockReleaseAsync(Key, right);
                var t12 = db.StringGetAsync(Key);
                var t13 = db.LockTakeAsync(Key, wrong, TimeSpan.FromSeconds(10));

                Assert.NotNull(right);
                Assert.NotNull(wrong);
                Assert.NotEqual(right, wrong);
                Assert.True(conn.Wait(t1), "1");
                Assert.False(conn.Wait(t1b), "1b");
                Assert.Equal(right, conn.Wait(t2));
                Assert.False(conn.Wait(t3), "3");
                Assert.Equal(right, conn.Wait(t4));
                Assert.False(conn.Wait(t5), "5");
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

        ////public void TestManualLockOpCountByVersion(RedisConnection conn, int expected, bool existFirst)
        ////{
        ////    const int DB = 0, LockDuration = 30;
        ////    const string Key = "TestManualLockOpCountByVersion";
        ////    conn.Wait(conn.Open());
        ////    conn.Keys.Remove(DB, Key);
        ////    var newVal = "us:" + CreateUniqueName();
        ////    string expectedVal = newVal;
        ////    if (existFirst)
        ////    {
        ////        expectedVal = "other:" + CreateUniqueName();
        ////        conn.Strings.Set(DB, Key, expectedVal, LockDuration);
        ////    }
        ////    int countBefore = conn.GetCounters().MessagesSent;

        ////    var tran = conn.CreateTransaction();
        ////    tran.AddCondition(Condition.KeyNotExists(DB, Key));
        ////    tran.Strings.Set(DB, Key, newVal, LockDuration);
        ////    var taken = conn.Wait(tran.Execute());

        ////    int countAfter = conn.GetCounters().MessagesSent;
        ////    var valAfter = conn.Wait(conn.Strings.GetString(DB, Key));
        ////    Assert.Equal(!existFirst, taken, "lock taken (manual)");
        ////    Assert.Equal(expectedVal, valAfter, "taker (manual)");
        ////    Assert.Equal(expected, (countAfter - countBefore) - 1, "expected ops (including ping) (manual)");
        ////    // note we get a ping from GetCounters
        ////}

        [Fact]
        public void TestBasicLockNotTaken()
        {
            using (var conn = GetUnsecuredConnection())
            {
                int errorCount = 0;

                conn.InternalError += delegate { Interlocked.Increment(ref errorCount); };

                var db = conn.GetDatabase(0);
                Task<bool> taken = null;
                Task<RedisValue> newValue = null;
                Task<TimeSpan?> ttl = null;

                const int LOOP = 50;
                for (int i = 0; i < LOOP; i++)
                {
                    db.KeyDeleteAsync("lock-not-exists");
                    taken = db.LockTakeAsync("lock-not-exists", "new-value", TimeSpan.FromSeconds(10));
                    newValue = db.StringGetAsync("lock-not-exists");
                    ttl = db.KeyTimeToLiveAsync("lock-not-exists");
                }
                Assert.True(conn.Wait(taken), "taken");
                Assert.Equal("new-value", conn.Wait(newValue));
                var ttlValue = conn.Wait(ttl).Value.TotalSeconds;
                Assert.True(ttlValue >= 8 && ttlValue <= 10, "ttl");

                Assert.Equal(0, errorCount);
            }
        }

        [Fact]
        public void TestBasicLockTaken()
        {
            using (var conn = GetUnsecuredConnection())
            {
                var db = conn.GetDatabase(0);
                db.KeyDelete("lock-exists");
                db.StringSet("lock-exists", "old-value", TimeSpan.FromSeconds(20));
                var taken = db.LockTakeAsync("lock-exists", "new-value", TimeSpan.FromSeconds(10));
                var newValue = db.StringGetAsync("lock-exists");
                var ttl = db.KeyTimeToLiveAsync("lock-exists");

                Assert.False(conn.Wait(taken), "taken");
                Assert.Equal("old-value", conn.Wait(newValue));
                var ttlValue = conn.Wait(ttl).Value.TotalSeconds;
                Assert.True(ttlValue >= 18 && ttlValue <= 20, "ttl");
            }
        }
    }
}
