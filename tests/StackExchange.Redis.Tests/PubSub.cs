using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable AccessToModifiedClosure

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class PubSub : TestBase
    {
        public PubSub(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public async Task ExplicitPublishMode()
        {
            using (var mx = Create(channelPrefix: "foo:"))
            {
                var pub = mx.GetSubscriber();
                int a = 0, b = 0, c = 0, d = 0;
                pub.Subscribe(new RedisChannel("*bcd", RedisChannel.PatternMode.Literal), (x, y) => Interlocked.Increment(ref a));
                pub.Subscribe(new RedisChannel("a*cd", RedisChannel.PatternMode.Pattern), (x, y) => Interlocked.Increment(ref b));
                pub.Subscribe(new RedisChannel("ab*d", RedisChannel.PatternMode.Auto), (x, y) => Interlocked.Increment(ref c));
                pub.Subscribe("abc*", (x, y) => Interlocked.Increment(ref d));

                await Task.Delay(1000).ForAwait();
                pub.Publish("abcd", "efg");
                await UntilCondition(TimeSpan.FromSeconds(10),
                    () => Thread.VolatileRead(ref b) == 1
                       && Thread.VolatileRead(ref c) == 1
                       && Thread.VolatileRead(ref d) == 1);
                Assert.Equal(0, Thread.VolatileRead(ref a));
                Assert.Equal(1, Thread.VolatileRead(ref b));
                Assert.Equal(1, Thread.VolatileRead(ref c));
                Assert.Equal(1, Thread.VolatileRead(ref d));

                pub.Publish("*bcd", "efg");
                await UntilCondition(TimeSpan.FromSeconds(10), () => Thread.VolatileRead(ref a) == 1);
                Assert.Equal(1, Thread.VolatileRead(ref a));
            }
        }

        [Theory]
        [InlineData(null, false, "a")]
        [InlineData("", false, "b")]
        [InlineData("Foo:", false, "c")]
        [InlineData(null, true, "d")]
        [InlineData("", true, "e")]
        [InlineData("Foo:", true, "f")]
        public async Task TestBasicPubSub(string channelPrefix, bool wildCard, string breaker)
        {
            using (var muxer = Create(channelPrefix: channelPrefix))
            {
                var pub = GetAnyMaster(muxer);
                var sub = muxer.GetSubscriber();
                await PingAsync(muxer, pub, sub).ForAwait();
                HashSet<string> received = new HashSet<string>();
                int secondHandler = 0;
                string subChannel = (wildCard ? "a*c" : "abc") + breaker;
                string pubChannel = "abc" + breaker;
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
                            Log(channel);
                        }
                    }
                }
                , handler2 = (_, __) => Interlocked.Increment(ref secondHandler);
                sub.Subscribe(subChannel, handler1);
                sub.Subscribe(subChannel, handler2);

                lock (received)
                {
                    Assert.Empty(received);
                }
                Assert.Equal(0, Thread.VolatileRead(ref secondHandler));
                var count = sub.Publish(pubChannel, "def");

                await PingAsync(muxer, pub, sub, 3).ForAwait();

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(1, Thread.VolatileRead(ref secondHandler));

                // unsubscribe from first; should still see second
                sub.Unsubscribe(subChannel, handler1);
                count = sub.Publish(pubChannel, "ghi");
                await PingAsync(muxer, pub, sub).ForAwait();
                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(2, Thread.VolatileRead(ref secondHandler));
                Assert.Equal(1, count);

                // unsubscribe from second; should see nothing this time
                sub.Unsubscribe(subChannel, handler2);
                count = sub.Publish(pubChannel, "ghi");
                await PingAsync(muxer, pub, sub).ForAwait();
                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(2, Thread.VolatileRead(ref secondHandler));
                Assert.Equal(0, count);
            }
        }

        [Fact]
        public async Task TestBasicPubSubFireAndForget()
        {
            using (var muxer = Create())
            {
                var pub = GetAnyMaster(muxer);
                var sub = muxer.GetSubscriber();

                RedisChannel key = Me() + Guid.NewGuid();
                HashSet<string> received = new HashSet<string>();
                int secondHandler = 0;
                await PingAsync(muxer, pub, sub).ForAwait();
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

                sub.Subscribe(key, (_, __) => Interlocked.Increment(ref secondHandler), CommandFlags.FireAndForget);

                lock (received)
                {
                    Assert.Empty(received);
                }
                Assert.Equal(0, Thread.VolatileRead(ref secondHandler));
                await PingAsync(muxer, pub, sub).ForAwait();
                var count = sub.Publish(key, "def", CommandFlags.FireAndForget);
                await PingAsync(muxer, pub, sub).ForAwait();

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(1, Thread.VolatileRead(ref secondHandler));

                sub.Unsubscribe(key);
                count = sub.Publish(key, "ghi", CommandFlags.FireAndForget);

                await PingAsync(muxer, pub, sub).ForAwait();

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(0, count);
            }
        }

        private static async Task PingAsync(IConnectionMultiplexer muxer, IServer pub, ISubscriber sub, int times = 1)
        {
            while (times-- > 0)
            {
                // both use async because we want to drain the completion managers, and the only
                // way to prove that is to use TPL objects
                var t1 = sub.PingAsync();
                var t2 = pub.PingAsync();
                await Task.Delay(100).ForAwait(); // especially useful when testing any-order mode

                if (!Task.WaitAll(new[] { t1, t2 }, muxer.TimeoutMilliseconds * 2)) throw new TimeoutException();
            }
        }

        [Fact]
        public async Task TestPatternPubSub()
        {
            using (var muxer = Create())
            {
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

                sub.Subscribe("a*c", (_, __) => Interlocked.Increment(ref secondHandler));
                lock (received)
                {
                    Assert.Empty(received);
                }
                Assert.Equal(0, Thread.VolatileRead(ref secondHandler));

                await PingAsync(muxer, pub, sub).ForAwait();
                var count = sub.Publish("abc", "def");
                await PingAsync(muxer, pub, sub).ForAwait();

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(1, Thread.VolatileRead(ref secondHandler));

                sub.Unsubscribe("a*c");
                count = sub.Publish("abc", "ghi");

                await PingAsync(muxer, pub, sub).ForAwait();

                lock (received)
                {
                    Assert.Single(received);
                }
                Assert.Equal(0, count);
            }
        }

        [Fact]
        public void TestPublishWithNoSubscribers()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetSubscriber();
                Assert.Equal(0, conn.Publish(Me() + "channel", "message"));
            }
        }

        [FactLongRunning]
        public void TestMassivePublishWithWithoutFlush_Local()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetSubscriber();
                TestMassivePublish(conn, Me(), "local");
            }
        }

        [FactLongRunning]
        public void TestMassivePublishWithWithoutFlush_Remote()
        {
            using (var muxer = Create(configuration: TestConfig.Current.RemoteServerAndPort))
            {
                var conn = muxer.GetSubscriber();
                TestMassivePublish(conn, Me(), "remote");
            }
        }

        private void TestMassivePublish(ISubscriber conn, string channel, string caption)
        {
            const int loop = 10000;

            var tasks = new Task[loop];

            var withFAF = Stopwatch.StartNew();
            for (int i = 0; i < loop; i++)
            {
                conn.Publish(channel, "bar", CommandFlags.FireAndForget);
            }
            withFAF.Stop();

            var withAsync = Stopwatch.StartNew();
            for (int i = 0; i < loop; i++)
            {
                tasks[i] = conn.PublishAsync(channel, "bar");
            }
            conn.WaitAll(tasks);
            withAsync.Stop();

            Log("{2}: {0}ms (F+F) vs {1}ms (async)",
                withFAF.ElapsedMilliseconds, withAsync.ElapsedMilliseconds, caption);
            // We've made async so far, this test isn't really valid anymore
            // So let's check they're at least within a few seconds.
            Assert.True(withFAF.ElapsedMilliseconds < withAsync.ElapsedMilliseconds + 3000, caption);
        }

        [FactLongRunning]
        public async Task PubSubGetAllAnyOrder()
        {
            using (var muxer = Create(syncTimeout: 20000))
            {
                var sub = muxer.GetSubscriber();
                RedisChannel channel = Me();
                const int count = 1000;
                var syncLock = new object();

                var data = new HashSet<int>();
                await sub.SubscribeAsync(channel, (_, val) =>
                {
                    bool pulse;
                    lock (data)
                    {
                        data.Add(int.Parse(Encoding.UTF8.GetString(val)));
                        pulse = data.Count == count;
                        if ((data.Count % 100) == 99) Log(data.Count.ToString());
                    }
                    if (pulse)
                    {
                        lock (syncLock)
                        {
                            Monitor.PulseAll(syncLock);
                        }
                    }
                }).ForAwait();

                lock (syncLock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        sub.Publish(channel, i.ToString(), CommandFlags.FireAndForget);
                    }
                    sub.Ping();
                    if (!Monitor.Wait(syncLock, 20000))
                    {
                        throw new TimeoutException("Items: " + data.Count);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Contains(i, data);
                    }
                }
            }
        }

        [Fact]
        public async Task PubSubGetAllCorrectOrder()
        {
            using (var muxer = Create(configuration: TestConfig.Current.RemoteServerAndPort, syncTimeout: 20000))
            {
                var sub = muxer.GetSubscriber();
                RedisChannel channel = Me();
                const int count = 250;
                var syncLock = new object();

                var data = new List<int>(count);
                var subChannel = await sub.SubscribeAsync(channel).ForAwait();

                await sub.PingAsync().ForAwait();

                async Task RunLoop()
                {
                    while (!subChannel.Completion.IsCompleted)
                    {
                        var work = await subChannel.ReadAsync().ForAwait();
                        int i = int.Parse(Encoding.UTF8.GetString(work.Message));
                        lock (data)
                        {
                            data.Add(i);
                            if (data.Count == count) break;
                            if ((data.Count % 100) == 99) Log("Received: " + data.Count.ToString());
                        }
                    }
                    lock (syncLock)
                    {
                        Log("PulseAll.");
                        Monitor.PulseAll(syncLock);
                    }
                }

                lock (syncLock)
                {
                    // Intentionally not awaited - running in parallel
                    _ = Task.Run(RunLoop);
                    for (int i = 0; i < count; i++)
                    {
                        sub.Publish(channel, i.ToString());
                        if ((i % 100) == 99) Log("Published: " + i.ToString());
                    }
                    Log("Send loop complete.");
                    if (!Monitor.Wait(syncLock, 20000))
                    {
                        throw new TimeoutException("Items: " + data.Count);
                    }
                    Log("Unsubscribe.");
                    subChannel.Unsubscribe();
                    Log("Sub Ping.");
                    sub.Ping();
                    Log("Database Ping.");
                    muxer.GetDatabase().Ping();
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Equal(i, data[i]);
                    }
                }

                Log("Awaiting completion.");
                await subChannel.Completion;
                Log("Completion awaited.");
                await Assert.ThrowsAsync<ChannelClosedException>(async delegate
                {
                    await subChannel.ReadAsync().ForAwait();
                }).ForAwait();
                Log("End of muxer.");
            }
            Log("End of test.");
        }

        [Fact]
        public async Task PubSubGetAllCorrectOrder_OnMessage_Sync()
        {
            using (var muxer = Create(configuration: TestConfig.Current.RemoteServerAndPort, syncTimeout: 20000))
            {
                var sub = muxer.GetSubscriber();
                RedisChannel channel = Me();
                const int count = 1000;
                var syncLock = new object();

                var data = new List<int>(count);
                var subChannel = await sub.SubscribeAsync(channel).ForAwait();
                subChannel.OnMessage(msg =>
                {
                    int i = int.Parse(Encoding.UTF8.GetString(msg.Message));
                    bool pulse = false;
                    lock (data)
                    {
                        data.Add(i);
                        if (data.Count == count) pulse = true;
                        if ((data.Count % 100) == 99) Log("Received: " + data.Count.ToString());
                    }
                    if (pulse)
                    {
                        lock (syncLock)
                        {
                            Monitor.PulseAll(syncLock);
                        }
                    }
                });
                await sub.PingAsync().ForAwait();

                lock (syncLock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        sub.Publish(channel, i.ToString(), CommandFlags.FireAndForget);
                        if ((i % 100) == 99) Log("Published: " + i.ToString());
                    }
                    Log("Send loop complete.");
                    if (!Monitor.Wait(syncLock, 20000))
                    {
                        throw new TimeoutException("Items: " + data.Count);
                    }
                    Log("Unsubscribe.");
                    subChannel.Unsubscribe();
                    Log("Sub Ping.");
                    sub.Ping();
                    Log("Database Ping.");
                    muxer.GetDatabase().Ping();
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Equal(i, data[i]);
                    }
                }

                Log("Awaiting completion.");
                await subChannel.Completion;
                Log("Completion awaited.");
                Assert.True(subChannel.Completion.IsCompleted);
                await Assert.ThrowsAsync<ChannelClosedException>(async delegate
                {
                    await subChannel.ReadAsync().ForAwait();
                }).ForAwait();
                Log("End of muxer.");
            }
            Log("End of test.");
        }

        [Fact]
        public async Task PubSubGetAllCorrectOrder_OnMessage_Async()
        {
            using (var muxer = Create(configuration: TestConfig.Current.RemoteServerAndPort, syncTimeout: 20000))
            {
                var sub = muxer.GetSubscriber();
                RedisChannel channel = Me();
                const int count = 1000;
                var syncLock = new object();

                var data = new List<int>(count);
                var subChannel = await sub.SubscribeAsync(channel).ForAwait();
                subChannel.OnMessage(msg =>
                {
                    int i = int.Parse(Encoding.UTF8.GetString(msg.Message));
                    bool pulse = false;
                    lock (data)
                    {
                        data.Add(i);
                        if (data.Count == count) pulse = true;
                        if ((data.Count % 100) == 99) Log("Received: " + data.Count.ToString());
                    }
                    if (pulse)
                    {
                        lock (syncLock)
                        {
                            Monitor.PulseAll(syncLock);
                        }
                    }
                    return i % 2 == 0 ? null : Task.CompletedTask;
                });
                await sub.PingAsync().ForAwait();

                lock (syncLock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        sub.Publish(channel, i.ToString(), CommandFlags.FireAndForget);
                        if ((i % 100) == 99) Log("Published: " + i.ToString());
                    }
                    Log("Send loop complete.");
                    if (!Monitor.Wait(syncLock, 20000))
                    {
                        throw new TimeoutException("Items: " + data.Count);
                    }
                    Log("Unsubscribe.");
                    subChannel.Unsubscribe();
                    Log("Sub Ping.");
                    sub.Ping();
                    Log("Database Ping.");
                    muxer.GetDatabase().Ping();
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Equal(i, data[i]);
                    }
                }

                Log("Awaiting completion.");
                await subChannel.Completion;
                Log("Completion awaited.");
                Assert.True(subChannel.Completion.IsCompleted);
                await Assert.ThrowsAsync<ChannelClosedException>(async delegate
                {
                    await subChannel.ReadAsync().ForAwait();
                }).ForAwait();
                Log("End of muxer.");
            }
            Log("End of test.");
        }

        [Fact]
        public async Task TestPublishWithSubscribers()
        {
            var channel = Me();
            using (var muxerA = Create(shared: false))
            using (var muxerB = Create(shared: false))
            using (var conn = Create())
            {
                var listenA = muxerA.GetSubscriber();
                var listenB = muxerB.GetSubscriber();
                var t1 = listenA.SubscribeAsync(channel, delegate { });
                var t2 = listenB.SubscribeAsync(channel, delegate { });

                await Task.WhenAll(t1, t2).ForAwait();

                // subscribe is just a thread-race-mess
                await listenA.PingAsync();
                await listenB.PingAsync();

                var pub = conn.GetSubscriber().PublishAsync(channel, "message");
                Assert.Equal(2, await pub); // delivery count
            }
        }

        [Fact]
        public async Task TestMultipleSubscribersGetMessage()
        {
            var channel = Me();
            using (var muxerA = Create(shared: false))
            using (var muxerB = Create(shared: false))
            using (var conn = Create())
            {
                var listenA = muxerA.GetSubscriber();
                var listenB = muxerB.GetSubscriber();
                conn.GetDatabase().Ping();
                var pub = conn.GetSubscriber();
                int gotA = 0, gotB = 0;
                var tA = listenA.SubscribeAsync(channel, (_, msg) => { if (msg == "message") Interlocked.Increment(ref gotA); });
                var tB = listenB.SubscribeAsync(channel, (_, msg) => { if (msg == "message") Interlocked.Increment(ref gotB); });
                await Task.WhenAll(tA, tB).ForAwait();
                Assert.Equal(2, pub.Publish(channel, "message"));
                await AllowReasonableTimeToPublishAndProcess().ForAwait();
                Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));

                // and unsubscibe...
                tA = listenA.UnsubscribeAsync(channel);
                await tA;
                Assert.Equal(1, pub.Publish(channel, "message"));
                await AllowReasonableTimeToPublishAndProcess().ForAwait();
                Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(2, Interlocked.CompareExchange(ref gotB, 0, 0));
            }
        }

        [Fact]
        public async Task Issue38()
        {
            // https://code.google.com/p/booksleeve/issues/detail?id=38
            using (var pub = Create())
            {
                var sub = pub.GetSubscriber();
                int count = 0;
                var prefix = Me();
                void handler(RedisChannel _, RedisValue __) => Interlocked.Increment(ref count);
                var a0 = sub.SubscribeAsync(prefix + "foo", handler);
                var a1 = sub.SubscribeAsync(prefix + "bar", handler);
                var b0 = sub.SubscribeAsync(prefix + "f*o", handler);
                var b1 = sub.SubscribeAsync(prefix + "b*r", handler);
                await Task.WhenAll(a0, a1, b0, b1).ForAwait();

                var c = sub.PublishAsync(prefix + "foo", "foo");
                var d = sub.PublishAsync(prefix + "f@o", "f@o");
                var e = sub.PublishAsync(prefix + "bar", "bar");
                var f = sub.PublishAsync(prefix + "b@r", "b@r");
                await Task.WhenAll(c, d, e, f).ForAwait();

                long total = c.Result + d.Result + e.Result + f.Result;

                await AllowReasonableTimeToPublishAndProcess().ForAwait();

                Assert.Equal(6, total); // sent
                Assert.Equal(6, Interlocked.CompareExchange(ref count, 0, 0)); // received
            }
        }

        internal static Task AllowReasonableTimeToPublishAndProcess() => Task.Delay(100);

        [Fact]
        public async Task TestPartialSubscriberGetMessage()
        {
            using (var muxerA = Create())
            using (var muxerB = Create())
            using (var conn = Create())
            {
                int gotA = 0, gotB = 0;
                var listenA = muxerA.GetSubscriber();
                var listenB = muxerB.GetSubscriber();
                var pub = conn.GetSubscriber();
                var prefix = Me();
                var tA = listenA.SubscribeAsync(prefix + "channel", (s, msg) => { if (s == prefix + "channel" && msg == "message") Interlocked.Increment(ref gotA); });
                var tB = listenB.SubscribeAsync(prefix + "chann*", (s, msg) => { if (s == prefix + "channel" && msg == "message") Interlocked.Increment(ref gotB); });
                await Task.WhenAll(tA, tB).ForAwait();
                Assert.Equal(2, pub.Publish(prefix + "channel", "message"));
                await AllowReasonableTimeToPublishAndProcess().ForAwait();
                Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));

                // and unsubscibe...
                tB = listenB.UnsubscribeAsync(prefix + "chann*", null);
                await tB;
                Assert.Equal(1, pub.Publish(prefix + "channel", "message"));
                await AllowReasonableTimeToPublishAndProcess().ForAwait();
                Assert.Equal(2, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));
            }
        }

        [Fact]
        public async Task TestSubscribeUnsubscribeAndSubscribeAgain()
        {
            using (var pubMuxer = Create())
            using (var subMuxer = Create())
            {
                var prefix = Me();
                var pub = pubMuxer.GetSubscriber();
                var sub = subMuxer.GetSubscriber();
                int x = 0, y = 0;
                var t1 = sub.SubscribeAsync(prefix + "abc", delegate { Interlocked.Increment(ref x); });
                var t2 = sub.SubscribeAsync(prefix + "ab*", delegate { Interlocked.Increment(ref y); });
                await Task.WhenAll(t1, t2).ForAwait();
                pub.Publish(prefix + "abc", "");
                await AllowReasonableTimeToPublishAndProcess().ForAwait();
                Assert.Equal(1, Volatile.Read(ref x));
                Assert.Equal(1, Volatile.Read(ref y));
                t1 = sub.UnsubscribeAsync(prefix + "abc", null);
                t2 = sub.UnsubscribeAsync(prefix + "ab*", null);
                await Task.WhenAll(t1, t2).ForAwait();
                pub.Publish(prefix + "abc", "");
                Assert.Equal(1, Volatile.Read(ref x));
                Assert.Equal(1, Volatile.Read(ref y));
                t1 = sub.SubscribeAsync(prefix + "abc", delegate { Interlocked.Increment(ref x); });
                t2 = sub.SubscribeAsync(prefix + "ab*", delegate { Interlocked.Increment(ref y); });
                await Task.WhenAll(t1, t2).ForAwait();
                pub.Publish(prefix + "abc", "");
                await AllowReasonableTimeToPublishAndProcess().ForAwait();
                Assert.Equal(2, Volatile.Read(ref x));
                Assert.Equal(2, Volatile.Read(ref y));
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
                Assert.Equal(1, Thread.VolatileRead(ref counter));
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
                Assert.Equal(2, Thread.VolatileRead(ref counter));
            }
        }
#endif
    }
}
