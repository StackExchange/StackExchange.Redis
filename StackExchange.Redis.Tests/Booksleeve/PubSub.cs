using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
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
                Assert.Equal(0, conn.Publish("channel", "message"));
            }
        }

        [Fact]
        public void TestMassivePublishWithWithoutFlush_Local()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                var conn = muxer.GetSubscriber();
                TestMassivePublish(conn, "local");
            }
        }

        [FactLongRunning]
        public void TestMassivePublishWithWithoutFlush_Remote()
        {
            using (var muxer = GetRemoteConnection(waitForOpen: true))
            {
                var conn = muxer.GetSubscriber();
                TestMassivePublish(conn, "remote");
            }
        }

        private void TestMassivePublish(ISubscriber conn, string caption)
        {
            const int loop = 100000;

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            var tasks = new Task[loop];

            var withFAF = Stopwatch.StartNew();
            for (int i = 0; i < loop; i++)
            {
                conn.Publish("foo", "bar", CommandFlags.FireAndForget);
            }
            withFAF.Stop();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            var withAsync = Stopwatch.StartNew();
            for (int i = 0; i < loop; i++)
            {
                tasks[i] = conn.PublishAsync("foo", "bar");
            }
            conn.WaitAll(tasks);
            withAsync.Stop();

            Assert.True(withFAF.ElapsedMilliseconds < withAsync.ElapsedMilliseconds, caption);
            Output.WriteLine("{2}: {0}ms (F+F) vs {1}ms (async)",
                withFAF.ElapsedMilliseconds, withAsync.ElapsedMilliseconds, caption);
        }

        [FactLongRunning]
        public async Task PubSubOrder()
        {
            using (var muxer = GetRemoteConnection(waitForOpen: true))
            {
                var sub = muxer.GetSubscriber();
                const string channel = "PubSubOrder";
                const int count = 500000;
                var syncLock = new object();

                var data = new List<int>(count);
                muxer.PreserveAsyncOrder = true;
                await sub.SubscribeAsync(channel, (key, val) =>
                {
                    bool pulse;
                    lock (data)
                    {
                        data.Add(int.Parse(Encoding.UTF8.GetString(val)));
                        pulse = data.Count == count;
                        if ((data.Count % 10) == 99) Output.WriteLine(data.Count.ToString());
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
                        Assert.Equal(i, data[i]);
                    }
                }
            }
        }

        [Fact]
        public void TestPublishWithSubscribers()
        {
            using (var muxerA = GetUnsecuredConnection())
            using (var muxerB = GetUnsecuredConnection())
            using (var conn = GetUnsecuredConnection())
            {
                var listenA = muxerA.GetSubscriber();
                var listenB = muxerB.GetSubscriber();
                var t1 = listenA.SubscribeAsync("channel", delegate { });
                var t2 = listenB.SubscribeAsync("channel", delegate { });

                listenA.Wait(t1);
                listenB.Wait(t2);

                var pub = conn.GetSubscriber().PublishAsync("channel", "message");
                Assert.Equal(2, conn.Wait(pub)); // delivery count
            }
        }

        [Fact]
        public void TestMultipleSubscribersGetMessage()
        {
            using (var muxerA = GetUnsecuredConnection())
            using (var muxerB = GetUnsecuredConnection())
            using (var conn = GetUnsecuredConnection())
            {
                var listenA = muxerA.GetSubscriber();
                var listenB = muxerB.GetSubscriber();
                conn.GetDatabase().Ping();
                var pub = conn.GetSubscriber();
                int gotA = 0, gotB = 0;
                var tA = listenA.SubscribeAsync("channel", (s, msg) => { if (msg == "message") Interlocked.Increment(ref gotA); });
                var tB = listenB.SubscribeAsync("channel", (s, msg) => { if (msg == "message") Interlocked.Increment(ref gotB); });
                listenA.Wait(tA);
                listenB.Wait(tB);
                Assert.Equal(2, pub.Publish("channel", "message"));
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));

                // and unsubscibe...
                tA = listenA.UnsubscribeAsync("channel");
                listenA.Wait(tA);
                Assert.Equal(1, pub.Publish("channel", "message"));
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
                void handler(RedisChannel channel, RedisValue payload) => Interlocked.Increment(ref count);
                var a0 = sub.SubscribeAsync("foo", handler);
                var a1 = sub.SubscribeAsync("bar", handler);
                var b0 = sub.SubscribeAsync("f*o", handler);
                var b1 = sub.SubscribeAsync("b*r", handler);
                sub.WaitAll(a0, a1, b0, b1);

                var c = sub.PublishAsync("foo", "foo");
                var d = sub.PublishAsync("f@o", "f@o");
                var e = sub.PublishAsync("bar", "bar");
                var f = sub.PublishAsync("b@r", "b@r");

                pub.WaitAll(c, d, e, f);
                long total = c.Result + d.Result + e.Result + f.Result;

                AllowReasonableTimeToPublishAndProcess();

                Assert.Equal(6, total); // sent
                Assert.Equal(6, Interlocked.CompareExchange(ref count, 0, 0)); // received
            }
        }

        internal static void AllowReasonableTimeToPublishAndProcess()
        {
            Thread.Sleep(50);
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
                var tA = listenA.SubscribeAsync("channel", (s, msg) => { if (s == "channel" && msg == "message") Interlocked.Increment(ref gotA); });
                var tB = listenB.SubscribeAsync("chann*", (s, msg) => { if (s == "channel" && msg == "message") Interlocked.Increment(ref gotB); });
                listenA.Wait(tA);
                listenB.Wait(tB);
                Assert.Equal(2, pub.Publish("channel", "message"));
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
                Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));

                // and unsubscibe...
                tB = listenB.UnsubscribeAsync("chann*", null);
                listenB.Wait(tB);
                Assert.Equal(1, pub.Publish("channel", "message"));
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
                var pub = pubMuxer.GetSubscriber();
                var sub = subMuxer.GetSubscriber();
                int x = 0, y = 0;
                var t1 = sub.SubscribeAsync("abc", delegate { Interlocked.Increment(ref x); });
                var t2 = sub.SubscribeAsync("ab*", delegate { Interlocked.Increment(ref y); });
                sub.WaitAll(t1, t2);
                pub.Publish("abc", "");
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(1, Volatile.Read(ref x));
                Assert.Equal(1, Volatile.Read(ref y));
                t1 = sub.UnsubscribeAsync("abc", null);
                t2 = sub.UnsubscribeAsync("ab*", null);
                sub.WaitAll(t1, t2);
                pub.Publish("abc", "");
                Assert.Equal(1, Volatile.Read(ref x));
                Assert.Equal(1, Volatile.Read(ref y));
                t1 = sub.SubscribeAsync("abc", delegate { Interlocked.Increment(ref x); });
                t2 = sub.SubscribeAsync("ab*", delegate { Interlocked.Increment(ref y); });
                sub.WaitAll(t1, t2);
                pub.Publish("abc", "");
                AllowReasonableTimeToPublishAndProcess();
                Assert.Equal(2, Volatile.Read(ref x));
                Assert.Equal(2, Volatile.Read(ref y));
            }
        }
    }
}
