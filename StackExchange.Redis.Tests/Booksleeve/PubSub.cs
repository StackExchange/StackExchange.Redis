using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class PubSub : BookSleeveTestBase // https://redis.io/commands#pubsub
    {
        public PubSub(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestPublishWithNoSubscribers()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetSubscriber();
                Assert.Equal(0, conn.Publish(Me() + "channel", "message"));
            }
        }

        [FactLongRunning]
        public void TestMassivePublishWithWithoutFlush_Local()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                var conn = muxer.GetSubscriber();
                TestMassivePublish(conn, Me(), "local");
            }
        }

        [FactLongRunning]
        public void TestMassivePublishWithWithoutFlush_Remote()
        {
            using (var muxer = GetRemoteConnection(waitForOpen: true))
            {
                var conn = muxer.GetSubscriber();
                TestMassivePublish(conn, Me(), "remote");
            }
        }

        private void TestMassivePublish(ISubscriber conn, string channel, string caption)
        {
            const int loop = 10000;

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            var tasks = new Task[loop];

            var withFAF = Stopwatch.StartNew();
            for (int i = 0; i < loop; i++)
            {
                conn.Publish(channel, "bar", CommandFlags.FireAndForget);
            }
            withFAF.Stop();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            var withAsync = Stopwatch.StartNew();
            for (int i = 0; i < loop; i++)
            {
                tasks[i] = conn.PublishAsync(channel, "bar");
            }
            conn.WaitAll(tasks);
            withAsync.Stop();

            Output.WriteLine("{2}: {0}ms (F+F) vs {1}ms (async)",
                withFAF.ElapsedMilliseconds, withAsync.ElapsedMilliseconds, caption);
            // We've made async so far, this test isn't really valid anymore
            // So let's check they're at least within a few seconds.
            Assert.True(withFAF.ElapsedMilliseconds < withAsync.ElapsedMilliseconds + 3000, caption);
        }

        [FactLongRunning]
        public async Task PubSubGetAllAnyOrder()
        {
            using (var muxer = GetRemoteConnection(waitForOpen: true,
                syncTimeout: 20000))
            {
                var sub = muxer.GetSubscriber();
                RedisChannel channel = Me();
                const int count = 1000;
                var syncLock = new object();

                var data = new HashSet<int>();
                await sub.SubscribeAsync(channel, (key, val) =>
                {
                    bool pulse;
                    lock (data)
                    {
                        data.Add(int.Parse(Encoding.UTF8.GetString(val)));
                        pulse = data.Count == count;
                        if ((data.Count % 100) == 99) Output.WriteLine(data.Count.ToString());
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
            using (var muxer = GetRemoteConnection(waitForOpen: true,
                syncTimeout: 20000))
            {
                var sub = muxer.GetSubscriber();
                RedisChannel channel = Me();
                const int count = 1000;
                var syncLock = new object();

                var data = new List<int>(count);
                var subChannel = await sub.SubscribeAsync(channel);

                await sub.PingAsync();

                async Task RunLoop()
                {
                    while (!subChannel.IsCompleted)
                    {
                        var work = await subChannel.ReadAsync();
                        int i = int.Parse(Encoding.UTF8.GetString(work.Message));
                        lock (data)
                        {
                            data.Add(i);
                            if (data.Count == count) break;
                            if ((data.Count % 100) == 99) Output.WriteLine(data.Count.ToString());
                        }
                    }
                    lock (syncLock)
                    {
                        Monitor.PulseAll(syncLock);
                    }
                }

                lock (syncLock)
                {
                    Task.Run(RunLoop);
                    for (int i = 0; i < count; i++)
                    {
                        sub.Publish(channel, i.ToString(), CommandFlags.FireAndForget);
                    }
                    if (!Monitor.Wait(syncLock, 20000))
                    {
                        throw new TimeoutException("Items: " + data.Count);
                    }
                    subChannel.Unsubscribe();
                    sub.Ping();
                    muxer.GetDatabase().Ping();
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Equal(i, data[i]);
                    }
                }

                Assert.True(subChannel.IsCompleted);
                await Assert.ThrowsAsync<ChannelClosedException>(async delegate
                {
                    var final = await subChannel.ReadAsync();
                });

            }
        }

        [Fact]
        public async Task PubSubGetAllCorrectOrder_OnMessage_Sync()
        {
            using (var muxer = GetRemoteConnection(waitForOpen: true,
                syncTimeout: 20000))
            {
                var sub = muxer.GetSubscriber();
                RedisChannel channel = Me();
                const int count = 1000;
                var syncLock = new object();

                var data = new List<int>(count);
                var subChannel = await sub.SubscribeAsync(channel);
                subChannel.OnMessage(msg =>
                {
                    int i = int.Parse(Encoding.UTF8.GetString(msg.Message));
                    bool pulse = false;
                    lock (data)
                    {
                        data.Add(i);
                        if (data.Count == count) pulse = true;
                        if ((data.Count % 100) == 99) Output.WriteLine(data.Count.ToString());
                    }
                    if (pulse)
                    {
                        lock (syncLock)
                        {
                            Monitor.PulseAll(syncLock);
                        }
                    }
                });
                await sub.PingAsync();

                lock (syncLock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        sub.Publish(channel, i.ToString(), CommandFlags.FireAndForget);
                    }
                    if (!Monitor.Wait(syncLock, 20000))
                    {
                        throw new TimeoutException("Items: " + data.Count);
                    }
                    subChannel.Unsubscribe();
                    sub.Ping();
                    muxer.GetDatabase().Ping();
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Equal(i, data[i]);
                    }
                }

                Assert.True(subChannel.IsCompleted);
                await Assert.ThrowsAsync<ChannelClosedException>(async delegate
                {
                    var final = await subChannel.ReadAsync();
                });

            }
        }

        [Fact]
        public async Task PubSubGetAllCorrectOrder_OnMessage_Async()
        {
            using (var muxer = GetRemoteConnection(waitForOpen: true,
                syncTimeout: 20000))
            {
                var sub = muxer.GetSubscriber();
                RedisChannel channel = Me();
                const int count = 1000;
                var syncLock = new object();

                var data = new List<int>(count);
                var subChannel = await sub.SubscribeAsync(channel);
                subChannel.OnMessage(msg =>
                {
                    int i = int.Parse(Encoding.UTF8.GetString(msg.Message));
                    bool pulse = false;
                    lock (data)
                    {
                        data.Add(i);
                        if (data.Count == count) pulse = true;
                        if ((data.Count % 100) == 99) Output.WriteLine(data.Count.ToString());
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
                await sub.PingAsync();

                lock (syncLock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        sub.Publish(channel, i.ToString(), CommandFlags.FireAndForget);
                    }
                    if (!Monitor.Wait(syncLock, 20000))
                    {
                        throw new TimeoutException("Items: " + data.Count);
                    }
                    subChannel.Unsubscribe();
                    sub.Ping();
                    muxer.GetDatabase().Ping();
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Equal(i, data[i]);
                    }
                }

                Assert.True(subChannel.IsCompleted);
                await Assert.ThrowsAsync<ChannelClosedException>(async delegate
                {
                    var final = await subChannel.ReadAsync();
                });

            }
        }

        [Fact]
        public void TestPublishWithSubscribers()
        {
            var channel = Me();
            using (var muxerA = GetUnsecuredConnection())
            using (var muxerB = GetUnsecuredConnection())
            using (var conn = GetUnsecuredConnection())
            {
                var listenA = muxerA.GetSubscriber();
                var listenB = muxerB.GetSubscriber();
                var t1 = listenA.SubscribeAsync(channel, delegate { });
                var t2 = listenB.SubscribeAsync(channel, delegate { });

                listenA.Wait(t1);
                listenB.Wait(t2);

                var pub = conn.GetSubscriber().PublishAsync(channel, "message");
                Assert.Equal(2, conn.Wait(pub)); // delivery count
            }
        }

        [Fact]
        public void TestMultipleSubscribersGetMessage()
        {
            var channel = Me();
            using (var muxerA = GetUnsecuredConnection())
            using (var muxerB = GetUnsecuredConnection())
            using (var conn = GetUnsecuredConnection())
            {
                var listenA = muxerA.GetSubscriber();
                var listenB = muxerB.GetSubscriber();
                conn.GetDatabase().Ping();
                var pub = conn.GetSubscriber();
                int gotA = 0, gotB = 0;
                var tA = listenA.SubscribeAsync(channel, (s, msg) => { if (msg == "message") Interlocked.Increment(ref gotA); });
                var tB = listenB.SubscribeAsync(channel, (s, msg) => { if (msg == "message") Interlocked.Increment(ref gotB); });
                listenA.Wait(tA);
                listenB.Wait(tB);
                Assert.Equal(2, pub.Publish(channel, "message"));
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));

                // and unsubscibe...
                tA = listenA.UnsubscribeAsync(channel);
                listenA.Wait(tA);
                Assert.Equal(1, pub.Publish(channel, "message"));
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(2, Interlocked.CompareExchange(ref gotB, 0, 0));
            }
        }

        [Fact]
        public void Issue38()
        { // https://code.google.com/p/booksleeve/issues/detail?id=38
            using (var pub = GetUnsecuredConnection(waitForOpen: true))
            {
                var sub = pub.GetSubscriber();
                int count = 0;
                var prefix = Me();
                void handler(RedisChannel channel, RedisValue payload) => Interlocked.Increment(ref count);
                var a0 = sub.SubscribeAsync(prefix + "foo", handler);
                var a1 = sub.SubscribeAsync(prefix + "bar", handler);
                var b0 = sub.SubscribeAsync(prefix + "f*o", handler);
                var b1 = sub.SubscribeAsync(prefix + "b*r", handler);
                sub.WaitAll(a0, a1, b0, b1);

                var c = sub.PublishAsync(prefix + "foo", "foo");
                var d = sub.PublishAsync(prefix + "f@o", "f@o");
                var e = sub.PublishAsync(prefix + "bar", "bar");
                var f = sub.PublishAsync(prefix + "b@r", "b@r");

                pub.WaitAll(c, d, e, f);
                long total = c.Result + d.Result + e.Result + f.Result;

                AllowReasonableTimeToPublishAndProcess();

                Assert.Equal(6, total); // sent
                Assert.Equal(6, Interlocked.CompareExchange(ref count, 0, 0)); // received
            }
        }

        internal static void AllowReasonableTimeToPublishAndProcess()
        {
            Thread.Sleep(100);
        }

        [Fact]
        public void TestPartialSubscriberGetMessage()
        {
            using (var muxerA = GetUnsecuredConnection())
            using (var muxerB = GetUnsecuredConnection())
            using (var conn = GetUnsecuredConnection())
            {
                int gotA = 0, gotB = 0;
                var listenA = muxerA.GetSubscriber();
                var listenB = muxerB.GetSubscriber();
                var pub = conn.GetSubscriber();
                var prefix = Me();
                var tA = listenA.SubscribeAsync(prefix + "channel", (s, msg) => { if (s == prefix + "channel" && msg == "message") Interlocked.Increment(ref gotA); });
                var tB = listenB.SubscribeAsync(prefix + "chann*", (s, msg) => { if (s == prefix + "channel" && msg == "message") Interlocked.Increment(ref gotB); });
                listenA.Wait(tA);
                listenB.Wait(tB);
                Assert.Equal(2, pub.Publish(prefix + "channel", "message"));
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));

                // and unsubscibe...
                tB = listenB.UnsubscribeAsync(prefix + "chann*", null);
                listenB.Wait(tB);
                Assert.Equal(1, pub.Publish(prefix + "channel", "message"));
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(2, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));
            }
        }

        [Fact]
        public void TestSubscribeUnsubscribeAndSubscribeAgain()
        {
            using (var pubMuxer = GetUnsecuredConnection())
            using (var subMuxer = GetUnsecuredConnection())
            {
                var prefix = Me();
                var pub = pubMuxer.GetSubscriber();
                var sub = subMuxer.GetSubscriber();
                int x = 0, y = 0;
                var t1 = sub.SubscribeAsync(prefix + "abc", delegate { Interlocked.Increment(ref x); });
                var t2 = sub.SubscribeAsync(prefix + "ab*", delegate { Interlocked.Increment(ref y); });
                sub.WaitAll(t1, t2);
                pub.Publish(prefix + "abc", "");
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(1, Volatile.Read(ref x));
                Assert.Equal(1, Volatile.Read(ref y));
                t1 = sub.UnsubscribeAsync(prefix + "abc", null);
                t2 = sub.UnsubscribeAsync(prefix + "ab*", null);
                sub.WaitAll(t1, t2);
                pub.Publish(prefix + "abc", "");
                Assert.Equal(1, Volatile.Read(ref x));
                Assert.Equal(1, Volatile.Read(ref y));
                t1 = sub.SubscribeAsync(prefix + "abc", delegate { Interlocked.Increment(ref x); });
                t2 = sub.SubscribeAsync(prefix + "ab*", delegate { Interlocked.Increment(ref y); });
                sub.WaitAll(t1, t2);
                pub.Publish(prefix + "abc", "");
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(2, Volatile.Read(ref x));
                Assert.Equal(2, Volatile.Read(ref y));
            }
        }
    }
}
