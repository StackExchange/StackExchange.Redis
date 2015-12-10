using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class PubSub : TestBase
    {

        [Test]
        public void ExplicitPublishMode()
        {
            using(var mx = Create(channelPrefix: "foo:"))
            {
                var pub = mx.GetSubscriber();
                int a = 0, b = 0, c = 0, d = 0;
                pub.Subscribe(new RedisChannel("*bcd", RedisChannel.PatternMode.Literal), (x, y) =>
                {
                    Interlocked.Increment(ref a);
                });
                pub.Subscribe(new RedisChannel("a*cd", RedisChannel.PatternMode.Pattern), (x, y) =>
                {
                    Interlocked.Increment(ref b);
                });
                pub.Subscribe(new RedisChannel("ab*d", RedisChannel.PatternMode.Auto), (x, y) =>
                {
                    Interlocked.Increment(ref c);
                });
                pub.Subscribe("abc*", (x, y) =>
                {
                    Interlocked.Increment(ref d);
                });

                Thread.Sleep(1000);
                pub.Publish("abcd", "efg");
                Thread.Sleep(500);
                Assert.AreEqual(0, VolatileWrapper.Read(ref a), "a1");
                Assert.AreEqual(1, VolatileWrapper.Read(ref b), "b1");
                Assert.AreEqual(1, VolatileWrapper.Read(ref c), "c1");
                Assert.AreEqual(1, VolatileWrapper.Read(ref d), "d1");

                pub.Publish("*bcd", "efg");
                Thread.Sleep(500);
                Assert.AreEqual(1, VolatileWrapper.Read(ref a), "a2");
                //Assert.AreEqual(1, VolatileWrapper.Read(ref b), "b2");
                //Assert.AreEqual(1, VolatileWrapper.Read(ref c), "c2");
                //Assert.AreEqual(1, VolatileWrapper.Read(ref d), "d2");

            }
        }

        [Test]
        [TestCase(true, null, false)]
        [TestCase(false, null, false)]
        [TestCase(true, "", false)]
        [TestCase(false, "", false)]
        [TestCase(true, "Foo:", false)]
        [TestCase(false, "Foo:", false)]
        [TestCase(true, null, true)]
        [TestCase(false, null, true)]
        [TestCase(true, "", true)]
        [TestCase(false, "", true)]
        [TestCase(true, "Foo:", true)]
        [TestCase(false, "Foo:", true)]
        public void TestBasicPubSub(bool preserveOrder, string channelPrefix, bool wildCard)
        {
            using (var muxer = Create(channelPrefix: channelPrefix))
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var pub = GetServer(muxer);
                var sub = muxer.GetSubscriber();
                Ping(muxer, pub, sub);
                HashSet<string> received = new HashSet<string>();
                int secondHandler = 0;
                string subChannel = wildCard ? "a*c" : "abc";
                const string pubChannel = "abc";
                Action<RedisChannel, RedisValue> handler1 = (channel, payload) =>
                {
                    lock (received)
                    {
                        if (channel == pubChannel)
                        {
                            received.Add(payload);
                        } else
                        {
                            Console.WriteLine((string)channel);
                        }
                    }
                }, handler2 = (channel, payload) =>
                {
                    Interlocked.Increment(ref secondHandler);
                };
                sub.Subscribe(subChannel, handler1);
                sub.Subscribe(subChannel, handler2);

                lock (received)
                {
                    Assert.AreEqual(0, received.Count);
                }
                Assert.AreEqual(0, VolatileWrapper.Read(ref secondHandler));
                var count = sub.Publish(pubChannel, "def");

                Ping(muxer, pub, sub, 3);

                lock (received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(1, VolatileWrapper.Read(ref secondHandler));

                // unsubscribe from first; should still see second
                sub.Unsubscribe(subChannel, handler1);
                count = sub.Publish(pubChannel, "ghi");
                Ping(muxer, pub, sub);
                lock(received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(2, VolatileWrapper.Read(ref secondHandler));
                Assert.AreEqual(1, count);

                // unsubscribe from second; should see nothing this time
                sub.Unsubscribe(subChannel, handler2);
                count = sub.Publish(pubChannel, "ghi");
                Ping(muxer, pub, sub);
                lock (received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(2, VolatileWrapper.Read(ref secondHandler));
                Assert.AreEqual(0, count);
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void TestBasicPubSubFireAndForget(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var pub = GetServer(muxer);
                var sub = muxer.GetSubscriber();

                RedisChannel key = Guid.NewGuid().ToString();
                HashSet<string> received = new HashSet<string>();
                int secondHandler = 0;
                Ping(muxer, pub, sub);
                sub.Subscribe(key, (channel, payload) =>
                {
                    lock (received)
                    {
                        if (channel == key)
                        {
                            received.Add(payload);
                        }
                    }
                }, CommandFlags.FireAndForget);
                

                sub.Subscribe(key, (channel, payload) =>
                {
                    Interlocked.Increment(ref secondHandler);
                }, CommandFlags.FireAndForget);

                lock (received)
                {
                    Assert.AreEqual(0, received.Count);
                }
                Assert.AreEqual(0, VolatileWrapper.Read(ref secondHandler));
                Ping(muxer, pub, sub);
                var count = sub.Publish(key, "def", CommandFlags.FireAndForget);
                Ping(muxer, pub, sub);

                lock (received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(1, VolatileWrapper.Read(ref secondHandler));

                sub.Unsubscribe(key);
                count = sub.Publish(key, "ghi", CommandFlags.FireAndForget);

                Ping(muxer, pub, sub);

                lock (received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(0, count);
            }
        }

        static void Ping(ConnectionMultiplexer muxer, IServer pub, ISubscriber sub, int times = 1)
        {
            while (times-- > 0)
            {
                // both use async because we want to drain the completion managers, and the only
                // way to prove that is to use TPL objects
                var t1 = sub.PingAsync();
                var t2 = pub.PingAsync();
                Thread.Sleep(100); // especially useful when testing any-order mode

                if (!Task.WaitAll(new[] { t1, t2 }, muxer.TimeoutMilliseconds * 2)) throw new TimeoutException();
            }
        }

        //protected override string GetConfiguration()
        //{
        //    return PrimaryServer + ":" + PrimaryPortString;
        //}

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void TestPatternPubSub(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var pub = GetServer(muxer);
                var sub = muxer.GetSubscriber();

                HashSet<string> received = new HashSet<string>();
                int secondHandler = 0;
                sub.Subscribe("a*c", (channel, payload) =>
                {
                    lock(received)
                    {
                        if (channel == "abc")
                        {
                            received.Add(payload);
                        }
                    }
                });

                sub.Subscribe("a*c", (channel, payload) =>
                {
                    Interlocked.Increment(ref secondHandler);
                });
                lock (received)
                {
                    Assert.AreEqual(0, received.Count);
                }
                Assert.AreEqual(0, VolatileWrapper.Read(ref secondHandler));
                var count = sub.Publish("abc", "def");

                Ping(muxer, pub, sub);

                lock(received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(1, VolatileWrapper.Read(ref secondHandler));

                sub.Unsubscribe("a*c");
                count = sub.Publish("abc", "ghi");

                Ping(muxer, pub, sub);

                lock(received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(0, count);
            }
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void SubscriptionsSurviveMasterSwitch(bool useSharedSocketManager)
        {
            using (var a = Create(allowAdmin: true, useSharedSocketManager: useSharedSocketManager))
            using (var b = Create(allowAdmin: true, useSharedSocketManager: useSharedSocketManager))
            {
                RedisChannel channel = Me();
                var subA = a.GetSubscriber();
                var subB = b.GetSubscriber();
                
                long masterChanged = 0, aCount = 0, bCount = 0;
                a.ConfigurationChangedBroadcast += delegate {
                    Console.WriteLine("a noticed config broadcast: " + Interlocked.Increment(ref masterChanged)); 
                };
                b.ConfigurationChangedBroadcast += delegate {
                    Console.WriteLine("b noticed config broadcast: " + Interlocked.Increment(ref masterChanged));
                };
                subA.Subscribe(channel, (ch, message) => {
                    Console.WriteLine("a got message: " + message);
                    Interlocked.Increment(ref aCount);
                });
                subB.Subscribe(channel, (ch, message) => {
                    Console.WriteLine("b got message: " + message);
                    Interlocked.Increment(ref bCount);
                });


                Assert.IsFalse(a.GetServer(PrimaryServer, PrimaryPort).IsSlave, PrimaryPortString + " is master via a");
                Assert.IsTrue(a.GetServer(PrimaryServer, SlavePort).IsSlave, SlavePortString + " is slave via a");
                Assert.IsFalse(b.GetServer(PrimaryServer, PrimaryPort).IsSlave, PrimaryPortString + " is master via b");
                Assert.IsTrue(b.GetServer(PrimaryServer, SlavePort).IsSlave, SlavePortString + " is slave via b");


                var epA = subA.SubscribedEndpoint(channel);
                var epB = subB.SubscribedEndpoint(channel);
                Console.WriteLine("a: " + EndPointCollection.ToString(epA));
                Console.WriteLine("b: " + EndPointCollection.ToString(epB));
                subA.Publish(channel, "a1");
                subB.Publish(channel, "b1");
                subA.Ping();
                subB.Ping();
                
                Assert.AreEqual(2, Interlocked.Read(ref aCount), "a");
                Assert.AreEqual(2, Interlocked.Read(ref bCount), "b");
                Assert.AreEqual(0, Interlocked.Read(ref masterChanged), "master");

                try
                {
                    Interlocked.Exchange(ref masterChanged, 0);
                    Interlocked.Exchange(ref aCount, 0);
                    Interlocked.Exchange(ref bCount, 0);
                    Console.WriteLine("Changing master...");
                    using (var sw = new StringWriter())
                    {
                        a.GetServer(PrimaryServer, SlavePort).MakeMaster(ReplicationChangeOptions.All, sw);
                        Console.WriteLine(sw);
                    }
                    subA.Ping();
                    subB.Ping();
                    Console.WriteLine("Pausing...");
                    Thread.Sleep(2000);

                    Assert.IsTrue(a.GetServer(PrimaryServer, PrimaryPort).IsSlave, PrimaryPortString + " is slave via a");
                    Assert.IsFalse(a.GetServer(PrimaryServer, SlavePort).IsSlave, SlavePortString + " is master via a");
                    Assert.IsTrue(b.GetServer(PrimaryServer, PrimaryPort).IsSlave, PrimaryPortString + " is slave via b");
                    Assert.IsFalse(b.GetServer(PrimaryServer, SlavePort).IsSlave, SlavePortString + " is master via b");

                    Console.WriteLine("Pause complete");
                    var counters = a.GetCounters();
                    Console.WriteLine("a outstanding: " + counters.TotalOutstanding);
                    counters = b.GetCounters();
                    Console.WriteLine("b outstanding: " + counters.TotalOutstanding);
                    subA.Ping();
                    subB.Ping();
                    epA = subA.SubscribedEndpoint(channel);
                    epB = subB.SubscribedEndpoint(channel);
                    Console.WriteLine("a: " + EndPointCollection.ToString(epA));
                    Console.WriteLine("b: " + EndPointCollection.ToString(epB));
                    Console.WriteLine("a2 sent to: " + subA.Publish(channel, "a2"));
                    Console.WriteLine("b2 sent to: " + subB.Publish(channel, "b2"));
                    subA.Ping();
                    subB.Ping();
                    Console.WriteLine("Checking...");
                    
                    Assert.AreEqual(2, Interlocked.Read(ref aCount), "a");
                    Assert.AreEqual(2, Interlocked.Read(ref bCount), "b");
                    Assert.AreEqual(4, Interlocked.CompareExchange(ref masterChanged, 0, 0), "master");
                }
                finally
                {
                    Console.WriteLine("Restoring configuration...");
                    try
                    {
                        a.GetServer(PrimaryServer, PrimaryPort).MakeMaster(ReplicationChangeOptions.All);
                    }
                    catch
                    { }
                }
            }
        }

        [Test]
        public void SubscriptionsSurviveConnectionFailure()
        {

#if !DEBUG
            Assert.Inconclusive("Needs #DEBUG");
#endif
            using(var muxer = Create( allowAdmin: true))
            {
                RedisChannel channel = Me();
                var sub = muxer.GetSubscriber();
                int counter = 0;
                sub.Subscribe(channel, delegate
                {
                    Interlocked.Increment(ref counter);
                });
                sub.Publish(channel, "abc");
                sub.Ping();
                Assert.AreEqual(1, VolatileWrapper.Read(ref counter), "counter");
                var server = GetServer(muxer);
                Assert.AreEqual(1, server.GetCounters().Subscription.SocketCount, "sockets");

#if DEBUG
                server.SimulateConnectionFailure();
                SetExpectedAmbientFailureCount(2);
#endif

                Thread.Sleep(100);
                sub.Ping();
#if DEBUG
                Assert.AreEqual(2, server.GetCounters().Subscription.SocketCount, "sockets");
#endif
                sub.Publish(channel, "abc");
                sub.Ping();
                Assert.AreEqual(2, VolatileWrapper.Read(ref counter), "counter");
            }
        }
    }

    internal static class VolatileWrapper
    {
        public static int Read(ref int location)
        {
#if !CORE_CLR
            return Thread.VolatileRead(ref location);
#else
            return Volatile.Read(ref location);
#endif
        }
    }
}
