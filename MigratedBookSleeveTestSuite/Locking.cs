//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using BookSleeve;
//using NUnit.Framework;
//using System.Threading;
//using System.Threading.Tasks;

//namespace Tests
//{
//    [TestFixture]
//    public class Locking
//    {
//        [Test]
//        public void AggressiveParallel()
//        {
//            int count = 2;
//            int errorCount = 0;
//            ManualResetEvent evt = new ManualResetEvent(false);
//            using (var c1 = Config.GetUnsecuredConnection(waitForOpen: true))
//            using (var c2 = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                WaitCallback cb = obj =>
//                {
//                    var conn = (RedisConnection)obj;
//                    conn.Error += delegate { Interlocked.Increment(ref errorCount); };
//                    for(int i = 0 ; i < 1000 ; i++)
//                    {
//                        conn.Strings.TakeLock(2, "abc", "def", 5);
//                    }
//                    conn.Wait(conn.Server.Ping());
//                    conn.Close(false);
//                    if (Interlocked.Decrement(ref count) == 0) evt.Set();
//                };
//                ThreadPool.QueueUserWorkItem(cb, c1);
//                ThreadPool.QueueUserWorkItem(cb, c2);
//                evt.WaitOne(8000);
//            }
//            Assert.AreEqual(0, Interlocked.CompareExchange(ref errorCount, 0, 0));
//        }

//        [Test]
//        public void TestOpCountByVersionLocal_UpLevel()
//        {
//            using (var conn = Config.GetUnsecuredConnection(open: false))
//            {
//                TestLockOpCountByVersion(conn, 1, false);
//                TestLockOpCountByVersion(conn, 1, true);
//                //TestManualLockOpCountByVersion(conn, 5, false);
//                //TestManualLockOpCountByVersion(conn, 3, true);
//            }
//        }
//        [Test]
//        public void TestOpCountByVersionLocal_DownLevel()
//        {
//            using (var conn = Config.GetUnsecuredConnection(open: false))
//            {
//                conn.SetServerVersion(new Version(2, 6, 0), ServerType.Master);
//                TestLockOpCountByVersion(conn, 5, false);
//                TestLockOpCountByVersion(conn, 3, true);
//                //TestManualLockOpCountByVersion(conn, 5, false);
//                //TestManualLockOpCountByVersion(conn, 3, true);
//            }
//        }

//        [Test]
//        public void TestOpCountByVersionRemote()
//        {
//            using (var conn = Config.GetRemoteConnection(open:false))
//            {
//                TestLockOpCountByVersion(conn, 1, false);
//                TestLockOpCountByVersion(conn, 1, true);
//                //TestManualLockOpCountByVersion(conn, 1, false);
//                //TestManualLockOpCountByVersion(conn, 1, true);
//            }
//        }
//        public void TestLockOpCountByVersion(RedisConnection conn, int expected, bool existFirst)
//        {
//            const int DB = 0, LockDuration = 30;
//            const string Key = "TestOpCountByVersion";
//            conn.Wait(conn.Open());
//            conn.Keys.Remove(DB, Key);
//            var newVal = "us:" + Config.CreateUniqueName();
//            string expectedVal = newVal;
//            if (existFirst)
//            {
//                expectedVal = "other:" + Config.CreateUniqueName();
//                conn.Strings.Set(DB, Key, expectedVal, LockDuration);
//            }
//            int countBefore = conn.GetCounters().MessagesSent;
//            var taken = conn.Wait(conn.Strings.TakeLock(DB, Key, newVal, LockDuration));
//            int countAfter = conn.GetCounters().MessagesSent;
//            var valAfter = conn.Wait(conn.Strings.GetString(DB, Key));
//            Assert.AreEqual(!existFirst, taken, "lock taken");
//            Assert.AreEqual(expectedVal, valAfter, "taker");
//            Assert.AreEqual(expected, (countAfter - countBefore) - 1, "expected ops (including ping)");
//            // note we get a ping from GetCounters
//        }

//        [Test]
//        public void TakeLockAndExtend()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                string right = Guid.NewGuid().ToString(),
//                    wrong = Guid.NewGuid().ToString();

//                const int DB = 7;
//                const string Key = "lock-key";

//                conn.SuspendFlush();
                
//                conn.Keys.Remove(DB, Key);
//                var t1 = conn.Strings.TakeLock(DB, Key, right, 20);
//                var t1b = conn.Strings.TakeLock(DB, Key, wrong, 10);
//                var t2 = conn.Strings.GetString(DB, Key);
//                var t3 = conn.Strings.ReleaseLock(DB, Key, wrong);
//                var t4 = conn.Strings.GetString(DB, Key);
//                var t5 = conn.Strings.ExtendLock(DB, Key, wrong, 60);
//                var t6 = conn.Strings.GetString(DB, Key);
//                var t7 = conn.Keys.TimeToLive(DB, Key);
//                var t8 = conn.Strings.ExtendLock(DB, Key, right, 60);
//                var t9 = conn.Strings.GetString(DB, Key);
//                var t10 = conn.Keys.TimeToLive(DB, Key);
//                var t11 = conn.Strings.ReleaseLock(DB, Key, right);
//                var t12 = conn.Strings.GetString(DB, Key);
//                var t13 = conn.Strings.TakeLock(DB, Key, wrong, 10);
//                conn.ResumeFlush();
//                Assert.IsNotNull(right);
//                Assert.IsNotNull(wrong);
//                Assert.AreNotEqual(right, wrong);
//                Assert.IsTrue(conn.Wait(t1), "1");
//                Assert.IsFalse(conn.Wait(t1b), "1b");
//                Assert.AreEqual(right, conn.Wait(t2), "2");
//                Assert.IsFalse(conn.Wait(t3), "3");
//                Assert.AreEqual(right, conn.Wait(t4), "4");
//                Assert.IsFalse(conn.Wait(t5), "5");
//                Assert.AreEqual(right, conn.Wait(t6), "6");
//                var ttl = conn.Wait(t7);
//                Assert.IsTrue(ttl > 0 && ttl <= 20, "7");
//                Assert.IsTrue(conn.Wait(t8), "8");
//                Assert.AreEqual(right, conn.Wait(t9), "9");
//                ttl = conn.Wait(t10);
//                Assert.IsTrue(ttl > 50 && ttl <= 60, "10");
//                Assert.IsTrue(conn.Wait(t11), "11");
//                Assert.IsNull(conn.Wait(t12), "12");
//                Assert.IsTrue(conn.Wait(t13), "13");
//            }
//        }


//        //public void TestManualLockOpCountByVersion(RedisConnection conn, int expected, bool existFirst)
//        //{
//        //    const int DB = 0, LockDuration = 30;
//        //    const string Key = "TestManualLockOpCountByVersion";
//        //    conn.Wait(conn.Open());
//        //    conn.Keys.Remove(DB, Key);
//        //    var newVal = "us:" + Config.CreateUniqueName();
//        //    string expectedVal = newVal;
//        //    if (existFirst)
//        //    {
//        //        expectedVal = "other:" + Config.CreateUniqueName();
//        //        conn.Strings.Set(DB, Key, expectedVal, LockDuration);
//        //    }
//        //    int countBefore = conn.GetCounters().MessagesSent;

//        //    var tran = conn.CreateTransaction();
//        //    tran.AddCondition(Condition.KeyNotExists(DB, Key));
//        //    tran.Strings.Set(DB, Key, newVal, LockDuration);
//        //    var taken = conn.Wait(tran.Execute());

//        //    int countAfter = conn.GetCounters().MessagesSent;
//        //    var valAfter = conn.Wait(conn.Strings.GetString(DB, Key));
//        //    Assert.AreEqual(!existFirst, taken, "lock taken (manual)");
//        //    Assert.AreEqual(expectedVal, valAfter, "taker (manual)");
//        //    Assert.AreEqual(expected, (countAfter - countBefore) - 1, "expected ops (including ping) (manual)");
//        //    // note we get a ping from GetCounters
//        //}



//        [Test]
//        public void TestBasicLockNotTaken()
//        {
//            using(var conn = Config.GetUnsecuredConnection())
//            {
//                int errorCount = 0;
//                conn.Error += delegate { Interlocked.Increment(ref errorCount); };
//                Task<bool> taken = null;
//                Task<string> newValue = null;
//                Task<long> ttl = null;

//                const int LOOP = 50;
//                for (int i = 0; i < LOOP; i++)
//                {
//                    conn.Keys.Remove(0, "lock-not-exists");
//                    taken = conn.Strings.TakeLock(0, "lock-not-exists", "new-value", 10);
//                    newValue = conn.Strings.GetString(0, "lock-not-exists");
//                    ttl = conn.Keys.TimeToLive(0, "lock-not-exists");
//                }
//                Assert.IsTrue(conn.Wait(taken), "taken");
//                Assert.AreEqual("new-value", conn.Wait(newValue));
//                var ttlValue = conn.Wait(ttl);
//                Assert.IsTrue(ttlValue >= 8 && ttlValue <= 10, "ttl");

//                Assert.AreEqual(0, errorCount);
//            }
//        }

//        [Test]
//        public void TestBasicLockTaken()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(0, "lock-exists");
//                conn.Strings.Set(0, "lock-exists", "old-value", expirySeconds: 20);
//                var taken = conn.Strings.TakeLock(0, "lock-exists", "new-value", 10);
//                var newValue = conn.Strings.GetString(0, "lock-exists");
//                var ttl = conn.Keys.TimeToLive(0, "lock-exists");

//                Assert.IsFalse(conn.Wait(taken), "taken");
//                Assert.AreEqual("old-value", conn.Wait(newValue));
//                var ttlValue = conn.Wait(ttl);
//                Assert.IsTrue(ttlValue >= 18 && ttlValue <= 20, "ttl");
//            }
//        }
//    }
//}
