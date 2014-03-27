//using NUnit.Framework;
//using System.Threading;
//using System.Text;
//using System;
//using System.Diagnostics;
//using System.Threading.Tasks;
//using BookSleeve;
//using System.Collections.Generic;

//namespace Tests
//{
//    [TestFixture]
//    public class PubSub // http://redis.io/commands#pubsub
//    {
//        [Test]
//        public void TestPublishWithNoSubscribers()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                Assert.AreEqual(0, conn.Wait(conn.Publish("channel", "message")));
//            }
//        }

//        [Test]
//        public void TestMassivePublishWithWithoutFlush_Local()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                TestMassivePublishWithWithoutFlush(conn, "local");
//            }
//        }
//        [Test]
//        public void TestMassivePublishWithWithoutFlush_Remote()
//        {
//            using (var conn = Config.GetRemoteConnection(waitForOpen: true))
//            {
//                TestMassivePublishWithWithoutFlush(conn, "remote");
//            }
//        }

//        private void TestMassivePublishWithWithoutFlush(RedisConnection conn, string caption)
//        {
//            const int loop = 100000;

//            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
//            GC.WaitForPendingFinalizers();

//            var tasks = new Task[loop];
//            var withFlush = Stopwatch.StartNew();
//            for (int i = 0; i < loop; i++)
//                tasks[i] = conn.Publish("foo", "bar");
//            conn.WaitAll(tasks);
//            withFlush.Stop();

//            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
//            GC.WaitForPendingFinalizers();

//            conn.SuspendFlush();
//            var withoutFlush = Stopwatch.StartNew();
//            for (int i = 0; i < loop; i++)
//                tasks[i] = conn.Publish("foo", "bar");
//            conn.ResumeFlush();
//            conn.WaitAll(tasks);
//            withoutFlush.Stop();

//            Assert.Less(1, 2, "sanity check");
//            Assert.Less(withoutFlush.ElapsedMilliseconds, withFlush.ElapsedMilliseconds, caption);
//            Console.WriteLine("{2}: {0}ms (eager-flush) vs {1}ms (suspend-flush)",
//                withFlush.ElapsedMilliseconds, withoutFlush.ElapsedMilliseconds, caption);
//        }


//        [Test]
//        public void PubSubOrder()
//        {
//            using (var pub = Config.GetRemoteConnection(waitForOpen: true))
//            using (var sub = pub.GetOpenSubscriberChannel())
//            {
//                string channel = "PubSubOrder";
//                const int count = 500000;
//                object syncLock = new object();

//                List<int> data = new List<int>(count);
//                sub.CompletionMode = ResultCompletionMode.PreserveOrder;
//                sub.Subscribe(channel, (key,val) => {
//                    bool pulse;
//                    lock (data)
//                    {
//                        data.Add(int.Parse(Encoding.UTF8.GetString(val)));
//                        pulse = data.Count == count;
//                        if((data.Count % 10) == 99) Console.WriteLine(data.Count);
//                    }
//                    if(pulse)
//                        lock(syncLock)
//                            Monitor.PulseAll(syncLock);
//                }).Wait();
                
//                lock (syncLock)
//                {
//                    for (int i = 0; i < count; i++)
//                    {
//                        pub.Publish(channel, i.ToString());
//                    }
//                    if (!Monitor.Wait(syncLock, 10000))
//                    {
//                        throw new TimeoutException("Items: " + data.Count);
//                    }
//                    for (int i = 0; i < count; i++)
//                        Assert.AreEqual(i, data[i]);
//                }
//            }

//        }

//        [Test]
//        public void TestPublishWithSubscribers()
//        {
//            using (var listenA = Config.GetSubscriberConnection())
//            using (var listenB = Config.GetSubscriberConnection())
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                var t1 = listenA.Subscribe("channel", delegate { });
//                var t2 = listenB.Subscribe("channel", delegate { });

//                listenA.Wait(t1);
//                Assert.AreEqual(1, listenA.SubscriptionCount, "A subscriptions");

//                listenB.Wait(t2);
//                Assert.AreEqual(1, listenB.SubscriptionCount, "B subscriptions");

//                var pub = conn.Publish("channel", "message");
//                Assert.AreEqual(2, conn.Wait(pub), "delivery count");
//            }
//        }

//        [Test]
//        public void TestMultipleSubscribersGetMessage()
//        {
//            using (var listenA = Config.GetSubscriberConnection())
//            using (var listenB = Config.GetSubscriberConnection())
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Wait(conn.Server.Ping());
//                int gotA = 0, gotB = 0;
//                var tA = listenA.Subscribe("channel", (s, msg) => { if (Encoding.UTF8.GetString(msg) == "message") Interlocked.Increment(ref gotA); });
//                var tB = listenB.Subscribe("channel", (s, msg) => { if (Encoding.UTF8.GetString(msg) == "message") Interlocked.Increment(ref gotB); });
//                listenA.Wait(tA);
//                listenB.Wait(tB);
//                Assert.AreEqual(2, conn.Wait(conn.Publish("channel", "message")));
//                AllowReasonableTimeToPublishAndProcess();
//                Assert.AreEqual(1, Interlocked.CompareExchange(ref gotA, 0, 0));
//                Assert.AreEqual(1, Interlocked.CompareExchange(ref gotB, 0, 0));

//                // and unsubscibe...
//                tA = listenA.Unsubscribe("channel");
//                listenA.Wait(tA);
//                Assert.AreEqual(1, conn.Wait(conn.Publish("channel", "message")));
//                AllowReasonableTimeToPublishAndProcess();
//                Assert.AreEqual(1, Interlocked.CompareExchange(ref gotA, 0, 0));
//                Assert.AreEqual(2, Interlocked.CompareExchange(ref gotB, 0, 0));
//            }
//        }

//        [Test]
//        public void Issue38()
//        { // https://code.google.com/p/booksleeve/issues/detail?id=38

//            using (var pub = Config.GetUnsecuredConnection(waitForOpen: true))
//            using (var sub = pub.GetOpenSubscriberChannel())
//            {
//                int count = 0;
//                Action<string, byte[]> handler = (channel, payload) => Interlocked.Increment(ref count);
//                var a = sub.Subscribe(new string[] { "foo", "bar" }, handler);
//                var b = sub.PatternSubscribe(new string[] { "f*o", "b*r" }, handler);
//                sub.WaitAll(a, b);

//                var c = pub.Publish("foo", "foo");
//                var d = pub.Publish("f@o", "f@o");
//                var e = pub.Publish("bar", "bar");
//                var f = pub.Publish("b@r", "b@r");

//                pub.WaitAll(c, d, e, f);
//                long total = c.Result + d.Result + e.Result + f.Result;

//                AllowReasonableTimeToPublishAndProcess();

//                Assert.AreEqual(6, total, "sent");
//                Assert.AreEqual(6, Interlocked.CompareExchange(ref count, 0, 0), "received");


//            }
//        }

//        internal static void AllowReasonableTimeToPublishAndProcess()
//        {
//            Thread.Sleep(50);
//        }

//        [Test]
//        public void TestPartialSubscriberGetMessage()
//        {
//            using (var listenA = Config.GetSubscriberConnection())
//            using (var listenB = Config.GetSubscriberConnection())
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                int gotA = 0, gotB = 0;
//                var tA = listenA.Subscribe("channel", (s, msg) => { if (s == "channel" && Encoding.UTF8.GetString(msg) == "message") Interlocked.Increment(ref gotA); });
//                var tB = listenB.PatternSubscribe("chann*", (s, msg) => { if (s == "channel" && Encoding.UTF8.GetString(msg) == "message") Interlocked.Increment(ref gotB); });
//                listenA.Wait(tA);
//                listenB.Wait(tB);
//                Assert.AreEqual(2, conn.Wait(conn.Publish("channel", "message")));
//                AllowReasonableTimeToPublishAndProcess();
//                Assert.AreEqual(1, Interlocked.CompareExchange(ref gotA, 0, 0));
//                Assert.AreEqual(1, Interlocked.CompareExchange(ref gotB, 0, 0));

//                // and unsubscibe...
//                tB = listenB.PatternUnsubscribe("chann*");
//                listenB.Wait(tB);
//                Assert.AreEqual(1, conn.Wait(conn.Publish("channel", "message")));
//                AllowReasonableTimeToPublishAndProcess();
//                Assert.AreEqual(2, Interlocked.CompareExchange(ref gotA, 0, 0));
//                Assert.AreEqual(1, Interlocked.CompareExchange(ref gotB, 0, 0));
//            }
//        }

//        [Test]
//        public void TestSubscribeUnsubscribeAndSubscribeAgain()
//        {
//            using (var pub = Config.GetUnsecuredConnection())
//            using (var sub = Config.GetSubscriberConnection())
//            {
//                int x = 0, y = 0;
//                var t1 = sub.Subscribe("abc", delegate { Interlocked.Increment(ref x); });
//                var t2 = sub.PatternSubscribe("ab*", delegate { Interlocked.Increment(ref y); });
//                sub.WaitAll(t1, t2);
//                pub.Publish("abc", "");
//                AllowReasonableTimeToPublishAndProcess();
//                Assert.AreEqual(1, Thread.VolatileRead(ref x));
//                Assert.AreEqual(1, Thread.VolatileRead(ref y));
//                t1 = sub.Unsubscribe("abc");
//                t2 = sub.PatternUnsubscribe("ab*");
//                sub.WaitAll(t1, t2);
//                pub.Publish("abc", "");
//                Assert.AreEqual(1, Thread.VolatileRead(ref x));
//                Assert.AreEqual(1, Thread.VolatileRead(ref y));
//                t1 = sub.Subscribe("abc", delegate { Interlocked.Increment(ref x); });
//                t2 = sub.PatternSubscribe("ab*", delegate { Interlocked.Increment(ref y); });
//                sub.WaitAll(t1, t2);
//                pub.Publish("abc", "");
//                AllowReasonableTimeToPublishAndProcess();
//                Assert.AreEqual(2, Thread.VolatileRead(ref x));
//                Assert.AreEqual(2, Thread.VolatileRead(ref y));

//            }
//        }
//    }
//}
