//using BookSleeve;
//using NUnit.Framework;
//using System.Linq;
//using System;
//using System.Diagnostics;
//using System.IO;
//using System.Threading;
//using System.Collections.Generic;
//using System.Threading.Tasks;

//namespace Tests
//{
//    [TestFixture]
//    public class Connections // http://redis.io/commands#connection
//    {
//        [Test]
//        public void TestConnectWithDownedNodeMustBeFast_multipletimes()
//        {
//            for (int i = 0; i < 5; i++) TestConnectWithDownedNodeMustBeFast();
//        }
//        [Test]
//        public void TestConnectWithDownedNodeMustBeFast()
//        {
//            using (var good = ConnectionUtils.Connect(Config.LocalHost + ":6379"))
//            using (var bad = ConnectionUtils.Connect(Config.LocalHost + ":6666"))
//            {
//                Assert.IsNotNull(good, "6379 should exist for this test");
//                Assert.IsNull(bad, "6666 should not exist for this test");
//            }

//            StringWriter log = new StringWriter();
//            var watch = Stopwatch.StartNew();
//            using (var selected = ConnectionUtils.Connect(Config.LocalHost +":6379," + Config.LocalHost + ":6666,name=Core(Q&A)", log))
//            {}
//            watch.Stop();
//            Console.WriteLine(log);
//            Assert.Less(1, 2, "I always get this wrong!");
//            Assert.Less(watch.ElapsedMilliseconds, 1200, "I always get this wrong!");
            
//        }

//        [Test]
//        public void TestConnectViaSentinel()
//        {
//            string[] endpoints;
//            StringWriter sw = new StringWriter();
//            var selected = ConnectionUtils.SelectConfiguration(Config.RemoteHost+":26379,serviceName=mymaster", out endpoints, sw);
//            string log = sw.ToString();
//            Console.WriteLine(log);
//            Assert.IsNotNull(selected, NO_SERVER);
//            Assert.AreEqual(Config.RemoteHost + ":6379", selected);
//        }
//        [Test]
//        public void TestConnectViaSentinelInvalidServiceName()
//        {
//            string[] endpoints;
//            StringWriter sw = new StringWriter();
//            var selected = ConnectionUtils.SelectConfiguration(Config.RemoteHost + ":26379,serviceName=garbage", out endpoints, sw);
//            string log = sw.ToString();
//            Console.WriteLine(log);
//            Assert.IsNull(selected);
//        }

//        const string NO_SERVER = "No server available";
//        [Test]
//        public void TestDirectConnect()
//        {
//            string[] endpoints;
//            StringWriter sw = new StringWriter();
//            var selected = ConnectionUtils.SelectConfiguration(Config.RemoteHost + ":6379", out endpoints, sw);
//            string log = sw.ToString();
//            Console.WriteLine(log);
//            Assert.IsNotNull(selected, NO_SERVER);
//            Assert.AreEqual(Config.RemoteHost + ":6379", selected);

//        }


//        [Test]
//        public void TestName()
//        {
//            using (var conn = Config.GetUnsecuredConnection(open: false, allowAdmin: true))
//            {
//                string name = Config.CreateUniqueName();
//                conn.Name = name;
//                conn.Wait(conn.Open());
//                if (!conn.Features.ClientName) Assert.Inconclusive();
//                var client = conn.Wait(conn.Server.ListClients()).SingleOrDefault(c => c.Name == name);
//                Assert.IsNotNull(client, "found client");
//            }
//        }

//        [Test]
//        public void CheckInProgressCountersGoToZero()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.CompletionMode = ResultCompletionMode.Concurrent;
//                Task<Counters> counters = null;
//                Task[] allTasks = new Task[5000];
//                for (int i = 0; i < 5000; i++)
//                {
//                    var tmp = conn.Strings.Get(5, "foo" + i);
//                    if (i == 2500)
//                    {
//                        counters = tmp.ContinueWith(x =>
//                        {
//                            return conn.GetCounters(false);
//                        },TaskContinuationOptions.ExecuteSynchronously);
//                    }
//                    allTasks[i] = tmp;
//                }

//                var c = conn.Wait(counters);
//                Console.WriteLine("in progress during: {0}", c.AsyncCallbacksInProgress);
//                Assert.AreNotEqual(0, c.AsyncCallbacksInProgress, "async during");
                
//                conn.WaitAll(allTasks);
//                PubSub.AllowReasonableTimeToPublishAndProcess();
//                Assert.AreEqual(0, conn.GetCounters(false).AsyncCallbacksInProgress, "async @ end");
//                Assert.AreEqual(0, c.SyncCallbacksInProgress, "sync @ end");
//            }
//        }

//        [Test]
//        public void TestSubscriberName()
//        {
//            using (var conn = Config.GetUnsecuredConnection(open: false, allowAdmin: true))
//            {
//                string name = Config.CreateUniqueName();
//                conn.Name = name;
//                conn.Wait(conn.Open());
//                if (!conn.Features.ClientName) Assert.Inconclusive();
//                using (var subscriber = conn.GetOpenSubscriberChannel())
//                {
//                    var evt = new ManualResetEvent(false);
//                    var tmp =  subscriber.Subscribe("test-subscriber-name", delegate
//                        {
//                            evt.Set();
//                        });
//                    subscriber.Wait(tmp);
//                    conn.Publish("test-subscriber-name", "foo");
//                    Assert.IsTrue(evt.WaitOne(1000), "event was set");
//                    var clients = conn.Wait(conn.Server.ListClients()).Where(c => c.Name == name).ToList();
//                    Assert.AreEqual(2, clients.Count, "number of clients");
//                }

//            }
//        }

//        [Test]
//        public void TestSubscriberNameOnRemote_WithName()
//        {
//            TestSubscriberNameOnRemote(true);
//        }
//        [Test]
//        public void TestSubscriberNameOnRemote_WithoutName()
//        {
//            TestSubscriberNameOnRemote(false);
//        }
//        private void TestSubscriberNameOnRemote(bool setName)
//        {
//            string id = Config.CreateUniqueName();
            
//            using (var pub = new RedisConnection(Config.RemoteHost, allowAdmin: true))
//            using (var sub = new RedisSubscriberConnection(Config.RemoteHost))
//            {
//                List<string> errors = new List<string>();
//                EventHandler<BookSleeve.ErrorEventArgs> errorHandler = (sender, args) =>
//                {
//                    lock (errors) errors.Add(args.Exception.Message);
//                };
//                pub.Error += errorHandler;
//                sub.Error += errorHandler;

//                if (setName)
//                {
//                    pub.Name = "pub_" + id;
//                    sub.Name = "sub_" + id;
//                }
//                int count = 0;
//                var subscribe = sub.Subscribe("foo"+id, (key,payload) => Interlocked.Increment(ref count));

//                Task pOpen = pub.Open(), sOpen = sub.Open();
//                pub.WaitAll(pOpen, sOpen, subscribe);

//                Assert.AreEqual(0, Interlocked.CompareExchange(ref count, 0, 0), "init message count");
//                pub.Wait(pub.Publish("foo" + id, "hello"));
                
//                PubSub.AllowReasonableTimeToPublishAndProcess();
//                var clients = setName ? pub.Wait(pub.Server.ListClients()) : null;
//                Assert.AreEqual(1, Interlocked.CompareExchange(ref count, 0, 0), "got message");
//                lock (errors)
//                {
//                    foreach (var error in errors)
//                    {
//                        Console.WriteLine(error);
//                    }
//                    Assert.AreEqual(0, errors.Count, "zero errors");
//                }
//                if (setName)
//                {
//                    Assert.AreEqual(1, clients.Count(x => x.Name == pub.Name), "pub has name");
//                    Assert.AreEqual(1, clients.Count(x => x.Name == sub.Name), "sub has name");
//                }
//            }
            
//        }

//        [Test]
//        public void TestForcedSubscriberName()
//        {
//            using (var conn = Config.GetUnsecuredConnection(allowAdmin: true, open: true, waitForOpen: true))
//            using (var sub = new RedisSubscriberConnection(conn.Host, conn.Port))
//            {
//                var task = sub.Subscribe("foo", delegate { });
//                string name = Config.CreateUniqueName();
//                sub.Name = name;
//                sub.SetServerVersion(new Version("2.6.9"), ServerType.Master);
//                sub.Wait(sub.Open());
//                sub.Wait(task);
//                Assert.AreEqual(1, sub.SubscriptionCount);

//                if (!conn.Features.ClientName) Assert.Inconclusive();
//                var clients = conn.Wait(conn.Server.ListClients()).Where(c => c.Name == name).ToList();
//                Assert.AreEqual(1, clients.Count, "number of clients");
//            }
//        }

//        [Test]
//        public void TestNameViaConnect()
//        {
//            string name = Config.CreateUniqueName();
//            using (var conn = ConnectionUtils.Connect(Config.RemoteHost+",allowAdmin=true,name=" + name))
//            {
//                Assert.IsNotNull(conn, NO_SERVER, "connection");
//                Assert.AreEqual(name, conn.Name, "connection name");
//                if (!conn.Features.ClientName) Assert.Inconclusive();
//                var client = conn.Wait(conn.Server.ListClients()).SingleOrDefault(c => c.Name == name);
//                Assert.IsNotNull(client, "find client");
//            }
//        }

//        // AUTH is already tested by secured connection

//        // QUIT is implicit in dispose

//        // ECHO has little utility in an application

//        [Test]
//        public void TestGetSetOnDifferentDbHasDifferentValues()
//        {
//            // note: we don't expose SELECT directly, but we can verify that we have different DBs in play:

//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(1, "select", "abc");
//                conn.Strings.Set(2, "select", "def");
//                var x = conn.Strings.GetString(1, "select");
//                var y = conn.Strings.GetString(2, "select");
//                conn.WaitAll(x, y);
//                Assert.AreEqual("abc", x.Result);
//                Assert.AreEqual("def", y.Result);
//            }
//        }
//        [Test, ExpectedException(typeof(ArgumentOutOfRangeException))]
//        public void TestGetOnInvalidDbThrows()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.GetString(-1, "select");                
//            }
//        }


//        [Test]
//        public void Ping()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                var ms = conn.Wait(conn.Server.Ping());
//                Assert.GreaterOrEqual(ms, 0);
//            }
//        }

//        [Test]
//        public void CheckCounters()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen:true))
//            {
//                conn.Wait(conn.Strings.GetString(0, "CheckCounters"));
//                var first = conn.GetCounters();

//                conn.Wait(conn.Strings.GetString(0, "CheckCounters"));
//                var second = conn.GetCounters();
//                // +2 = ping + one select
//                Assert.AreEqual(first.MessagesSent + 2, second.MessagesSent, "MessagesSent");
//                Assert.AreEqual(first.MessagesReceived + 2, second.MessagesReceived, "MessagesReceived");
//                Assert.AreEqual(0, second.ErrorMessages, "ErrorMessages");
//                Assert.AreEqual(0, second.MessagesCancelled, "MessagesCancelled");
//                Assert.AreEqual(0, second.SentQueue, "SentQueue");
//                Assert.AreEqual(0, second.UnsentQueue, "UnsentQueue");
//                Assert.AreEqual(0, second.QueueJumpers, "QueueJumpers");
//                Assert.AreEqual(0, second.Timeouts, "Timeouts");
//                Assert.IsTrue(second.Ping >= 0, "Ping");
//                Assert.IsTrue(second.ToString().Length > 0, "ToString");
//            }
//        }

        
//    }
//}
