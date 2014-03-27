//using System.Threading.Tasks;
//using NUnit.Framework;
//using System.Collections.Generic;
//using System;
//using System.Linq;
//using BookSleeve;
//using System.Text;
//using System.Threading;

//namespace Tests
//{
//    [TestFixture]
//    public class Transactions // http://redis.io/commands#transactions
//    {


//        [Test]
//        public void TestBasicMultiExec()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(1, "tran");
//                conn.Keys.Remove(2, "tran");

//                using (var tran = conn.CreateTransaction())
//                {
//                    var s1 = tran.Strings.Set(1, "tran", "abc");
//                    var s2 = tran.Strings.Set(2, "tran", "def");
//                    var g1 = tran.Strings.GetString(1, "tran");
//                    var g2 = tran.Strings.GetString(2, "tran");

//                    var outsideTran = conn.Strings.GetString(1, "tran");

//                    var exec = tran.Execute();

//                    Assert.IsNull(conn.Wait(outsideTran));
//                    Assert.AreEqual("abc", conn.Wait(g1));
//                    Assert.AreEqual("def", conn.Wait(g2));
//                    conn.Wait(s1);
//                    conn.Wait(s2);
//                    conn.Wait(exec);
//                }

//            }
//        }

//        [Test]
//        public void TestRollback()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            using (var tran = conn.CreateTransaction())
//            {
//                var task = tran.Strings.Set(4, "abc", "def");
//                tran.Discard();

//                Assert.IsTrue(task.IsCanceled, "should be cancelled");
//                try
//                {
//                    conn.Wait(task);
//                }
//                catch (TaskCanceledException)
//                { }// ok, else boom!

//            }
//        }

//        [Test]
//        public void TestDispose()
//        {
//            Task task;
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                using (var tran = conn.CreateTransaction())
//                {
//                    task = tran.Strings.Set(4, "abc", "def");
//                }
//                Assert.IsTrue(task.IsCanceled, "should be cancelled");
//                try
//                {
//                    conn.Wait(task);
//                }
//                catch (TaskCanceledException)
//                { }// ok, else boom!
//            }
//        }

//        [Test]
//        public void BlogDemo()
//        {
//            int db = 8;
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(db, "foo"); // just to reset
//                using (var tran = conn.CreateTransaction())
//                {   // deliberately ignoring INCRBY here
//                    tran.AddCondition(Condition.KeyNotExists(db, "foo"));
//                    var t1 = tran.Strings.Increment(db, "foo");
//                    var t2 = tran.Strings.Increment(db, "foo");
//                    var val = tran.Strings.GetString(db, "foo");

//                    var t3 = tran.Execute(); // this *still* returns a Task

//                    Assert.AreEqual(true, conn.Wait(t3));
//                    Assert.AreEqual(1, conn.Wait(t1));
//                    Assert.AreEqual(2, conn.Wait(t2));
//                    Assert.AreEqual("2", conn.Wait(val));
//                }
//            }
//        }

//        [Test]
//        public void AbortWorks()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                conn.CompletionMode = ResultCompletionMode.PreserveOrder;
//                conn.Keys.Remove(0, "AbortWorks");
//                using (var tran = conn.CreateTransaction())
//                {
//                    var condition = tran.AddCondition(Condition.KeyExists(0, "AbortWorks"));
//                    var rename = tran.Strings.Increment(0, "key");
//                    var success = conn.Wait(tran.Execute());

//                    Assert.IsFalse(success, "success");
//                    Assert.IsFalse(conn.Wait(condition), "condition");
//                    Assert.AreEqual(TaskStatus.Canceled, rename.Status, "rename");
//                }
//            }
//        }
//        [Test]
//        public void SignalRSend()
//        {
//            const int db = 3;
//            const string idKey = "newid";
//            const string channel = "SignalRSend";
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            using (var sub = conn.GetOpenSubscriberChannel())
//            {
//                conn.CompletionMode = ResultCompletionMode.ConcurrentIfContinuation;
//                sub.CompletionMode = ResultCompletionMode.PreserveOrder;
//                var received = new List<string>();
//                sub.Subscribe(channel, (chan, payload) =>
//                {
//                    lock (received) { received.Add(Encoding.UTF8.GetString(payload)); }
//                });
//                conn.Keys.Remove(db, idKey);

//                int totalAttempts = 0;
//                var evt = new ManualResetEvent(false);
//                const int threadCount = 2;
//                const int perThread = 5;
//                int unreadyThreads = threadCount;
//                ParameterizedThreadStart work = state =>
//                {
//                    string thread = (string)state;
//                    if (Interlocked.Decrement(ref unreadyThreads) == 0)
//                    {
//                        // all threads ready; unleash the hounds!
//                        evt.Set();
//                    }
//                    else
//                    {
//                        evt.WaitOne();
//                    }

//                    for (int i = 0; i < perThread; i++)
//                    {
//                        int attempts = Send(conn, idKey, db, channel, thread + ":" + i).Result;
//                        Interlocked.Add(ref totalAttempts, attempts);
//                    }

//                };
//                var threads = new Thread[unreadyThreads];
//                for (int i = 0; i < threads.Length; i++) threads[i] = new Thread(work);
//                for (int i = 0; i < threads.Length; i++) threads[i].Start(i.ToString());
//                for (int i = 0; i < threads.Length; i++) threads[i].Join();

//                const int expected = perThread * threadCount;
//                // it doesn't matter that this number is big; we are testing the pathological worst-case
//                // scenario here; multiple threads aggressively causing conflicts
//                Console.WriteLine("total messages: {0} (vs {1} theoretical)", totalAttempts, expected);

//                // check we got everything we expected, and nothing more; the messages should
//                // all be increasing; we should have every thread/loop combination
//                Assert.AreEqual(expected, received.Count, "total messages");
//                for (int i = 0; i < expected; i++)
//                {
//                    string want = (i + 1) + ":";
//                    Assert.IsTrue(received[i].StartsWith(want), want);
//                }
//                for (int i = 0; i < threadCount; i++)
//                {
//                    for (int j = 0; j < perThread; j++)
//                    {
//                        string want = ":" + i + ":" + j;
//                        bool found = received.Any(x => x.EndsWith(want));
//                        Assert.IsTrue(found, want);
//                    }
//                }
//            }
//        }
//        static async Task<int> Send(RedisConnection conn, string idKey, int db, string channel, string data)
//        {
//            int attempts = 0;
//            bool success;
//            do
//            {
//                var oldId = await conn.Strings.GetInt64(db, idKey).SafeAwaitable().ConfigureAwait(false); // important: let this be nullable;
//                // means "doesn't exist"
//                var newId = (oldId ?? 0) + 1;
//                var payload = Pack(newId, data);

//                using (var tran = conn.CreateTransaction())
//                {
//                    var x0 = tran.AddCondition(Condition.KeyEquals(db, idKey, oldId)).SafeAwaitable();
//                    var x1 = tran.Strings.Increment(db, idKey).SafeAwaitable();
//                    var x2 = tran.Publish(channel, payload).SafeAwaitable();
//                    success = await tran.Execute().SafeAwaitable().ConfigureAwait(false);

//                    if (success)
//                    {
//                        // still expect all of these to get answers
//                        await Task.WhenAll(x0, x1, x2);

//                        Assert.IsTrue(x0.Result, "condition passed");
//                        Assert.AreEqual(newId, x1.Result);
//                    }
//                    else
//                    {
//                        // can't say much about x0; could have got past that
//                        Assert.IsTrue(await IsCancelled(x1));
//                        Assert.IsTrue(await IsCancelled(x2));
//                    }

//                    attempts++;
//                }
//            } while (!success);
//            return attempts;
//        }

//        [Test]
//        public void Issue43()
//        {
//            using(var conn = Config.GetRemoteConnection())
//            {
//                conn.Keys.Remove(0, "anExistingKey1");
//                conn.Keys.Remove(0, "anExistingKey2");
//                conn.Keys.Remove(0, "anExistingKey3");
//                conn.Strings.Set(0, "anExistingKey1", "anExistingKey1");
//                conn.Strings.Set(0, "anExistingKey2", "anExistingKey2");
//                conn.Strings.Set(0, "anExistingKey3", "anExistingKey3");
//                for(int i = 0; i < 10000; i++)
//                {
//                       using(var tx = conn.CreateTransaction())
//                       {
//                              var cond1 = tx.AddCondition(Condition.KeyExists(0, "anExistingKey1"));
//                              var cond2 = tx.AddCondition(Condition.KeyExists(0, "anExistingKey2"));
//                              var cond3 = tx.AddCondition(Condition.KeyExists(0, "anExistingKey3"));

//                              tx.Strings.Increment(0, "foo", 1);
//                              tx.Strings.Increment(0, "foo", 1);
//                              tx.Strings.Increment(0, "foo", 1);

//                              var txRes = tx.Execute();

//                              Assert.IsTrue(tx.Wait(cond1), "cond1" + i);  //--> ok
//                              Assert.IsTrue(tx.Wait(cond2), "cond2" + i);  //--> ok
//                              Assert.IsTrue(tx.Wait(cond3), "cond3" + i);  //--> ok
//                              Assert.IsTrue(tx.Wait(txRes), "txRes" + i);   //--> not ok: false
//                       }
//                }
//            }
//        }

//        static async Task<bool> IsCancelled(Task task)
//        {
//            try
//            {
//                await task;
//                return false;
//            }
//            catch
//            {
//                return task.IsCanceled;
//            }
//        }

//        static byte[] Pack(long id, string data)
//        {
//            return Encoding.UTF8.GetBytes(id + ":" + data);
//        }


//    }
//}

