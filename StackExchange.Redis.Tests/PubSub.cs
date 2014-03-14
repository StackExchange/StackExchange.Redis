using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class PubSub : TestBase
    {

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
                Assert.AreEqual(0, Thread.VolatileRead(ref secondHandler));
                var count = sub.Publish(pubChannel, "def");

                Ping(muxer, pub, sub, 3);

                lock (received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(1, Thread.VolatileRead(ref secondHandler));

                // unsubscribe from first; should still see second
                sub.Unsubscribe(subChannel, handler1);
                count = sub.Publish(pubChannel, "ghi");
                Ping(muxer, pub, sub);
                lock(received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(2, Thread.VolatileRead(ref secondHandler));
                Assert.AreEqual(1, count);

                // unsubscribe from second; should see nothing this time
                sub.Unsubscribe(subChannel, handler2);
                count = sub.Publish(pubChannel, "ghi");
                Ping(muxer, pub, sub);
                lock (received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(2, Thread.VolatileRead(ref secondHandler));
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
                Assert.AreEqual(0, Thread.VolatileRead(ref secondHandler));
                Ping(muxer, pub, sub);
                var count = sub.Publish(key, "def", CommandFlags.FireAndForget);
                Ping(muxer, pub, sub);

                lock (received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(1, Thread.VolatileRead(ref secondHandler));

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
                Assert.AreEqual(0, Thread.VolatileRead(ref secondHandler));
                var count = sub.Publish("abc", "def");

                Ping(muxer, pub, sub);

                lock(received)
                {
                    Assert.AreEqual(1, received.Count);
                }
                Assert.AreEqual(1, Thread.VolatileRead(ref secondHandler));

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
        public void SubscriptionsSurviceMasterSwitch()
        {
            using (var a = Create(allowAdmin: true))
            using (var b = Create(allowAdmin: true))
            {
                RedisChannel channel = Me();
                var subA = a.GetSubscriber();
                var subB = b.GetSubscriber();
                
                long masterChanged = 0, aCount = 0, bCount = 0;
                a.MasterChanged += delegate { Interlocked.Increment(ref masterChanged); };
                subA.Subscribe(channel, delegate { Interlocked.Increment(ref aCount); });
                subB.Subscribe(channel, delegate { Interlocked.Increment(ref bCount); });

                //var epA = subA.IdentifyEndpoint(channel);
                //var epB = subB.IdentifyEndpoint(channel);
                //Console.WriteLine(epA);
                //Console.WriteLine(epB);
                subA.Publish(channel, "a");
                subB.Publish(channel, "b");
                subA.Ping();
                subB.Ping();

                Assert.AreEqual(0, Interlocked.Read(ref masterChanged), "master");
                Assert.AreEqual(2, Interlocked.Read(ref aCount), "a");
                Assert.AreEqual(2, Interlocked.Read(ref bCount), "b");

                try
                {
                    b.GetServer(PrimaryServer, SlavePort).MakeMaster(ReplicationChangeOptions.All);
                    Thread.Sleep(100);
                    //epA = subA.IdentifyEndpoint(channel);
                    //epB = subB.IdentifyEndpoint(channel);
                    //Console.WriteLine(epA);
                    //Console.WriteLine(epB);
                    subA.Publish(channel, "a");
                    subB.Publish(channel, "b");
                    subA.Ping();
                    subA.Ping();
                    subB.Ping();
                    Assert.AreEqual(2, Interlocked.Read(ref masterChanged), "master");
                    Assert.AreEqual(4, Interlocked.Read(ref aCount), "a");
                    Assert.AreEqual(4, Interlocked.Read(ref bCount), "b");
                }
                finally
                {
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
                Assert.AreEqual(1, Thread.VolatileRead(ref counter), "counter");
                var server = GetServer(muxer);
                Assert.AreEqual(1, server.GetCounters().Subscription.SocketCount, "sockets");

#if DEBUG
                ((IRedisServerDebug)server).SimulateConnectionFailure();
                SetExpectedAmbientFailureCount(2);
#endif

                Thread.Sleep(100);
                sub.Ping();
#if DEBUG
                Assert.AreEqual(2, server.GetCounters().Subscription.SocketCount, "sockets");
#endif
                sub.Publish(channel, "abc");
                sub.Ping();
                Assert.AreEqual(2, Thread.VolatileRead(ref counter), "counter");
            }
        }
    }
}
