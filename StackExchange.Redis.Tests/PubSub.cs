using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class PubSub : TestBase
    {
        public PubSub(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void ExplicitPublishMode()
        {
            using (var mx = Create(channelPrefix: "foo:"))
            {
                var pub = mx.GetSubscriber();
                int a = 0, b = 0, c = 0, d = 0;
                pub.Subscribe(new RedisChannel("*bcd", RedisChannel.PatternMode.Literal), (x, y) => Interlocked.Increment(ref a));
                pub.Subscribe(new RedisChannel("a*cd", RedisChannel.PatternMode.Pattern), (x, y) => Interlocked.Increment(ref b));
                pub.Subscribe(new RedisChannel("ab*d", RedisChannel.PatternMode.Auto), (x, y) => Interlocked.Increment(ref c));
                pub.Subscribe("abc*", (x, y) => Interlocked.Increment(ref d));

                Thread.Sleep(1000);
                pub.Publish("abcd", "efg");
                Thread.Sleep(500);
                Assert.Equal(0, VolatileWrapper.Read(ref a));
                Assert.Equal(1, VolatileWrapper.Read(ref b));
                Assert.Equal(1, VolatileWrapper.Read(ref c));
                Assert.Equal(1, VolatileWrapper.Read(ref d));

                pub.Publish("*bcd", "efg");
                Thread.Sleep(500);
                Assert.Equal(1, VolatileWrapper.Read(ref a));
                //Assert.Equal(1, VolatileWrapper.Read(ref b));
                //Assert.Equal(1, VolatileWrapper.Read(ref c));
                //Assert.Equal(1, VolatileWrapper.Read(ref d));

            }
        }

        [Theory]
        [InlineData(true, null, false)]
        [InlineData(false, null, false)]
        [InlineData(true, "", false)]
        [InlineData(false, "", false)]
        [InlineData(true, "Foo:", false)]
        [InlineData(false, "Foo:", false)]
        [InlineData(true, null, true)]
        [InlineData(false, null, true)]
        [InlineData(true, "", true)]
        [InlineData(false, "", true)]
        [InlineData(true, "Foo:", true)]
        [InlineData(false, "Foo:", true)]
        public void TestBasicPubSub(bool preserveOrder, string channelPrefix, bool wildCard)
        {
            using (var muxer = Create(channelPrefix: channelPrefix))
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var pub = GetAnyMaster(muxer);
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
                        }
                        else
                        {
                            Output.WriteLine((string)channel);
                        }
                    }
                }
                , handler2 = (channel, payload) => Interlocked.Increment(ref secondHandler);
                sub.Subscribe(subChannel, handler1);
                sub.Subscribe(subChannel, handler2);

                lock (received)
                {
                    Assert.Empty(received);
                }
                Assert.Equal(0, VolatileWrapper.Read(ref secondHandler));
                var count = sub.Publish(pubChannel, "def");

                Ping(muxer, pub, sub, 3);

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(1, VolatileWrapper.Read(ref secondHandler));

                // unsubscribe from first; should still see second
                sub.Unsubscribe(subChannel, handler1);
                count = sub.Publish(pubChannel, "ghi");
                Ping(muxer, pub, sub);
                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(2, VolatileWrapper.Read(ref secondHandler));
                Assert.Equal(1, count);

                // unsubscribe from second; should see nothing this time
                sub.Unsubscribe(subChannel, handler2);
                count = sub.Publish(pubChannel, "ghi");
                Ping(muxer, pub, sub);
                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(2, VolatileWrapper.Read(ref secondHandler));
                Assert.Equal(0, count);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestBasicPubSubFireAndForget(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var pub = GetAnyMaster(muxer);
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

                sub.Subscribe(key, (channel, payload) => Interlocked.Increment(ref secondHandler), CommandFlags.FireAndForget);

                lock (received)
                {
                    Assert.Empty(received);
                }
                Assert.Equal(0, VolatileWrapper.Read(ref secondHandler));
                Ping(muxer, pub, sub);
                var count = sub.Publish(key, "def", CommandFlags.FireAndForget);
                Ping(muxer, pub, sub);

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(1, VolatileWrapper.Read(ref secondHandler));

                sub.Unsubscribe(key);
                count = sub.Publish(key, "ghi", CommandFlags.FireAndForget);

                Ping(muxer, pub, sub);

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(0, count);
            }
        }

        private static void Ping(ConnectionMultiplexer muxer, IServer pub, ISubscriber sub, int times = 1)
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
        //    return TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort;
        //}

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestPatternPubSub(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var pub = GetAnyMaster(muxer);
                var sub = muxer.GetSubscriber();

                HashSet<string> received = new HashSet<string>();
                int secondHandler = 0;
                sub.Subscribe("a*c", (channel, payload) =>
                {
                    lock (received)
                    {
                        if (channel == "abc")
                        {
                            received.Add(payload);
                        }
                    }
                });

                sub.Subscribe("a*c", (channel, payload) => Interlocked.Increment(ref secondHandler));
                lock (received)
                {
                    Assert.Empty(received);
                }
                Assert.Equal(0, VolatileWrapper.Read(ref secondHandler));
                var count = sub.Publish("abc", "def");

                Ping(muxer, pub, sub);

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(1, VolatileWrapper.Read(ref secondHandler));

                sub.Unsubscribe("a*c");
                count = sub.Publish("abc", "ghi");

                Ping(muxer, pub, sub);

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(0, count);
            }
        }

#if DEBUG
        [Fact]
        public async Task SubscriptionsSurviveConnectionFailureAsync()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                RedisChannel channel = Me();
                var sub = muxer.GetSubscriber();
                int counter = 0;
                await sub.SubscribeAsync(channel, delegate
                {
                    Interlocked.Increment(ref counter);
                }).ConfigureAwait(false);
                await sub.PublishAsync(channel, "abc").ConfigureAwait(false);
                sub.Ping();
                await Task.Delay(200).ConfigureAwait(false);
                Assert.Equal(1, VolatileWrapper.Read(ref counter));
                var server = GetServer(muxer);
                Assert.Equal(1, server.GetCounters().Subscription.SocketCount);

                server.SimulateConnectionFailure();
                SetExpectedAmbientFailureCount(2);
                await Task.Delay(200).ConfigureAwait(false);
                sub.Ping();
                Assert.Equal(2, server.GetCounters().Subscription.SocketCount);
                await sub.PublishAsync(channel, "abc").ConfigureAwait(false);
                await Task.Delay(200).ConfigureAwait(false);
                sub.Ping();
                Assert.Equal(2, VolatileWrapper.Read(ref counter));
            }
        }
#endif
    }

    internal static class VolatileWrapper
    {
        public static int Read(ref int location)
        {
#if !NETCOREAPP1_0
            return Thread.VolatileRead(ref location);
#else
            return Volatile.Read(ref location);
#endif
        }
    }
}
