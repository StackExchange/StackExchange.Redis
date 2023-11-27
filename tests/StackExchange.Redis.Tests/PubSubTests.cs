using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable AccessToModifiedClosure

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class PubSubTests : TestBase
{
    public PubSubTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task ExplicitPublishMode()
    {
        using var conn = Create(channelPrefix: "foo:", log: Writer);

        var pub = conn.GetSubscriber();
        int a = 0, b = 0, c = 0, d = 0;
        pub.Subscribe(new RedisChannel("*bcd", RedisChannel.PatternMode.Literal), (x, y) => Interlocked.Increment(ref a));
        pub.Subscribe(new RedisChannel("a*cd", RedisChannel.PatternMode.Pattern), (x, y) => Interlocked.Increment(ref b));
        pub.Subscribe(new RedisChannel("ab*d", RedisChannel.PatternMode.Auto), (x, y) => Interlocked.Increment(ref c));
#pragma warning disable CS0618
        pub.Subscribe("abc*", (x, y) => Interlocked.Increment(ref d));

        pub.Publish("abcd", "efg");
#pragma warning restore CS0618
        await UntilConditionAsync(TimeSpan.FromSeconds(10),
            () => Thread.VolatileRead(ref b) == 1
               && Thread.VolatileRead(ref c) == 1
               && Thread.VolatileRead(ref d) == 1);
        Assert.Equal(0, Thread.VolatileRead(ref a));
        Assert.Equal(1, Thread.VolatileRead(ref b));
        Assert.Equal(1, Thread.VolatileRead(ref c));
        Assert.Equal(1, Thread.VolatileRead(ref d));

#pragma warning disable CS0618
        pub.Publish("*bcd", "efg");
#pragma warning restore CS0618
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => Thread.VolatileRead(ref a) == 1);
        Assert.Equal(1, Thread.VolatileRead(ref a));
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
        using var conn = Create(channelPrefix: channelPrefix, shared: false, log: Writer);

        var pub = GetAnyPrimary(conn);
        var sub = conn.GetSubscriber();
        await PingAsync(pub, sub).ForAwait();
        HashSet<string?> received = new();
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
#pragma warning disable CS0618
        sub.Subscribe(subChannel, handler1);
        sub.Subscribe(subChannel, handler2);
#pragma warning restore CS0618

        lock (received)
        {
            Assert.Empty(received);
        }
        Assert.Equal(0, Thread.VolatileRead(ref secondHandler));
#pragma warning disable CS0618
        var count = sub.Publish(pubChannel, "def");
#pragma warning restore CS0618

        await PingAsync(pub, sub, 3).ForAwait();

        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => received.Count == 1);
        lock (received)
        {
            Assert.Single(received);
        }
        // Give handler firing a moment
        await UntilConditionAsync(TimeSpan.FromSeconds(2), () => Thread.VolatileRead(ref secondHandler) == 1);
        Assert.Equal(1, Thread.VolatileRead(ref secondHandler));

        // unsubscribe from first; should still see second
#pragma warning disable CS0618
        sub.Unsubscribe(subChannel, handler1);
        count = sub.Publish(pubChannel, "ghi");
#pragma warning restore CS0618
        await PingAsync(pub, sub).ForAwait();
        lock (received)
        {
            Assert.Single(received);
        }

        await UntilConditionAsync(TimeSpan.FromSeconds(2), () => Thread.VolatileRead(ref secondHandler) == 2);

        var secondHandlerCount = Thread.VolatileRead(ref secondHandler);
        Log("Expecting 2 from second handler, got: " + secondHandlerCount);
        Assert.Equal(2, secondHandlerCount);
        Assert.Equal(1, count);

        // unsubscribe from second; should see nothing this time
#pragma warning disable CS0618
        sub.Unsubscribe(subChannel, handler2);
        count = sub.Publish(pubChannel, "ghi");
#pragma warning restore CS0618
        await PingAsync(pub, sub).ForAwait();
        lock (received)
        {
            Assert.Single(received);
        }
        secondHandlerCount = Thread.VolatileRead(ref secondHandler);
        Log("Expecting 2 from second handler, got: " + secondHandlerCount);
        Assert.Equal(2, secondHandlerCount);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task TestBasicPubSubFireAndForget()
    {
        using var conn = Create(shared: false, log: Writer);

        var profiler = conn.AddProfiler();
        var pub = GetAnyPrimary(conn);
        var sub = conn.GetSubscriber();

        RedisChannel key = RedisChannel.Literal(Me() + Guid.NewGuid());
        HashSet<string?> received = new();
        int secondHandler = 0;
        await PingAsync(pub, sub).ForAwait();
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
        Log(profiler);

        lock (received)
        {
            Assert.Empty(received);
        }
        Assert.Equal(0, Thread.VolatileRead(ref secondHandler));
        await PingAsync(pub, sub).ForAwait();
        var count = sub.Publish(key, "def", CommandFlags.FireAndForget);
        await PingAsync(pub, sub).ForAwait();

        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => received.Count == 1);
        Log(profiler);

        lock (received)
        {
            Assert.Single(received);
        }
        Assert.Equal(1, Thread.VolatileRead(ref secondHandler));

        sub.Unsubscribe(key);
        count = sub.Publish(key, "ghi", CommandFlags.FireAndForget);

        await PingAsync(pub, sub).ForAwait();
        Log(profiler);
        lock (received)
        {
            Assert.Single(received);
        }
        Assert.Equal(0, count);
    }

    private async Task PingAsync(IServer pub, ISubscriber sub, int times = 1)
    {
        while (times-- > 0)
        {
            // both use async because we want to drain the completion managers, and the only
            // way to prove that is to use TPL objects
            var subTask = sub.PingAsync();
            var pubTask = pub.PingAsync();
            await Task.WhenAll(subTask, pubTask).ForAwait();

            Log($"Sub PING time: {subTask.Result.TotalMilliseconds} ms");
            Log($"Pub PING time: {pubTask.Result.TotalMilliseconds} ms");
        }
    }

    [Fact]
    public async Task TestPatternPubSub()
    {
        using var conn = Create(shared: false, log: Writer);

        var pub = GetAnyPrimary(conn);
        var sub = conn.GetSubscriber();

        HashSet<string?> received = new();
        int secondHandler = 0;
#pragma warning disable CS0618
        sub.Subscribe("a*c", (channel, payload) =>
#pragma warning restore CS0618
        {
            lock (received)
            {
                if (channel == "abc")
                {
                    received.Add(payload);
                }
            }
        });

#pragma warning disable CS0618
        sub.Subscribe("a*c", (_, __) => Interlocked.Increment(ref secondHandler));
#pragma warning restore CS0618
        lock (received)
        {
            Assert.Empty(received);
        }
        Assert.Equal(0, Thread.VolatileRead(ref secondHandler));

        await PingAsync(pub, sub).ForAwait();
        var count = sub.Publish(RedisChannel.Literal("abc"), "def");
        await PingAsync(pub, sub).ForAwait();

        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => received.Count == 1);
        lock (received)
        {
            Assert.Single(received);
        }

        // Give reception a bit, the handler could be delayed under load
        await UntilConditionAsync(TimeSpan.FromSeconds(2), () => Thread.VolatileRead(ref secondHandler) == 1);
        Assert.Equal(1, Thread.VolatileRead(ref secondHandler));

#pragma warning disable CS0618
        sub.Unsubscribe("a*c");
        count = sub.Publish("abc", "ghi");
#pragma warning restore CS0618

        await PingAsync(pub, sub).ForAwait();

        lock (received)
        {
            Assert.Single(received);
        }
    }

    [Fact]
    public void TestPublishWithNoSubscribers()
    {
        using var conn = Create();

        var sub = conn.GetSubscriber();
#pragma warning disable CS0618
        Assert.Equal(0, sub.Publish(Me() + "channel", "message"));
#pragma warning restore CS0618
    }

    [FactLongRunning]
    public void TestMassivePublishWithWithoutFlush_Local()
    {
        using var conn = Create();

        var sub = conn.GetSubscriber();
        TestMassivePublish(sub, Me(), "local");
    }

    [FactLongRunning]
    public void TestMassivePublishWithWithoutFlush_Remote()
    {
        using var conn = Create(configuration: TestConfig.Current.RemoteServerAndPort);

        var sub = conn.GetSubscriber();
        TestMassivePublish(sub, Me(), "remote");
    }

    private void TestMassivePublish(ISubscriber sub, string channel, string caption)
    {
        const int loop = 10000;

        var tasks = new Task[loop];

        var withFAF = Stopwatch.StartNew();
        for (int i = 0; i < loop; i++)
        {
#pragma warning disable CS0618
            sub.Publish(channel, "bar", CommandFlags.FireAndForget);
#pragma warning restore CS0618
        }
        withFAF.Stop();

        var withAsync = Stopwatch.StartNew();
        for (int i = 0; i < loop; i++)
        {
#pragma warning disable CS0618
            tasks[i] = sub.PublishAsync(channel, "bar");
#pragma warning restore CS0618
        }
        sub.WaitAll(tasks);
        withAsync.Stop();

        Log("{2}: {0}ms (F+F) vs {1}ms (async)",
            withFAF.ElapsedMilliseconds, withAsync.ElapsedMilliseconds, caption);
        // We've made async so far, this test isn't really valid anymore
        // So let's check they're at least within a few seconds.
        Assert.True(withFAF.ElapsedMilliseconds < withAsync.ElapsedMilliseconds + 3000, caption);
    }

    [Fact]
    public async Task SubscribeAsyncEnumerable()
    {
        using var conn = Create(syncTimeout: 20000, shared: false, log: Writer);

        var sub = conn.GetSubscriber();
        RedisChannel channel = RedisChannel.Literal(Me());

        const int TO_SEND = 5;
        var gotall = new TaskCompletionSource<int>();

        var source = await sub.SubscribeAsync(channel);
        var op = Task.Run(async () => {
            int count = 0;
            await foreach (var item in source)
            {
                count++;
                if (count == TO_SEND) gotall.TrySetResult(count);
            }
            return count;
        });

        for (int i = 0; i < TO_SEND; i++)
        {
            await sub.PublishAsync(channel, i);
        }
        await gotall.Task.WithTimeout(5000);

        // check the enumerator exits cleanly
        sub.Unsubscribe(channel);
        var count = await op.WithTimeout(1000);
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task PubSubGetAllAnyOrder()
    {
        using var sonn = Create(syncTimeout: 20000, shared: false, log: Writer);

        var sub = sonn.GetSubscriber();
        RedisChannel channel = RedisChannel.Literal(Me());
        const int count = 1000;
        var syncLock = new object();

        Assert.True(sub.IsConnected(), nameof(sub.IsConnected));
        var data = new HashSet<int>();
        await sub.SubscribeAsync(channel, (_, val) =>
        {
            bool pulse;
            lock (data)
            {
                data.Add(int.Parse(Encoding.UTF8.GetString(val!)));
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

    [Fact]
    public async Task PubSubGetAllCorrectOrder()
    {
        using (var conn = Create(configuration: TestConfig.Current.RemoteServerAndPort, syncTimeout: 20000, log: Writer))
        {
            var sub = conn.GetSubscriber();
            RedisChannel channel = RedisChannel.Literal(Me());
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
                    int i = int.Parse(Encoding.UTF8.GetString(work.Message!));
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
                conn.GetDatabase().Ping();
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
        using (var conn = Create(configuration: TestConfig.Current.RemoteServerAndPort, syncTimeout: 20000, log: Writer))
        {
            var sub = conn.GetSubscriber();
            RedisChannel channel = RedisChannel.Literal(Me());
            const int count = 1000;
            var syncLock = new object();

            var data = new List<int>(count);
            var subChannel = await sub.SubscribeAsync(channel).ForAwait();
            subChannel.OnMessage(msg =>
            {
                int i = int.Parse(Encoding.UTF8.GetString(msg.Message!));
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
                conn.GetDatabase().Ping();
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
        using (var conn = Create(configuration: TestConfig.Current.RemoteServerAndPort, syncTimeout: 20000, log: Writer))
        {
            var sub = conn.GetSubscriber();
            RedisChannel channel = RedisChannel.Literal(Me());
            const int count = 1000;
            var syncLock = new object();

            var data = new List<int>(count);
            var subChannel = await sub.SubscribeAsync(channel).ForAwait();
            subChannel.OnMessage(msg =>
            {
                int i = int.Parse(Encoding.UTF8.GetString(msg.Message!));
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
                // Making sure we cope with null being returned here by a handler
                return i % 2 == 0 ? null! : Task.CompletedTask;
            });
            await sub.PingAsync().ForAwait();

            // Give a delay between subscriptions and when we try to publish to be safe
            await Task.Delay(1000).ForAwait();

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
                conn.GetDatabase().Ping();
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
        using var connA = Create(shared: false, log: Writer);
        using var connB = Create(shared: false, log: Writer);
        using var connPub = Create();

        var channel = Me();
        var listenA = connA.GetSubscriber();
        var listenB = connB.GetSubscriber();
#pragma warning disable CS0618
        var t1 = listenA.SubscribeAsync(channel, delegate { });
        var t2 = listenB.SubscribeAsync(channel, delegate { });
#pragma warning restore CS0618

        await Task.WhenAll(t1, t2).ForAwait();

        // subscribe is just a thread-race-mess
        await listenA.PingAsync();
        await listenB.PingAsync();

#pragma warning disable CS0618
        var pub = connPub.GetSubscriber().PublishAsync(channel, "message");
#pragma warning restore CS0618
        Assert.Equal(2, await pub); // delivery count
    }

    [Fact]
    public async Task TestMultipleSubscribersGetMessage()
    {
        using var connA = Create(shared: false, log: Writer);
        using var connB = Create(shared: false, log: Writer);
        using var connPub = Create();

        var channel = RedisChannel.Literal(Me());
        var listenA = connA.GetSubscriber();
        var listenB = connB.GetSubscriber();
        connPub.GetDatabase().Ping();
        var pub = connPub.GetSubscriber();
        int gotA = 0, gotB = 0;
        var tA = listenA.SubscribeAsync(channel, (_, msg) => { if (msg == "message") Interlocked.Increment(ref gotA); });
        var tB = listenB.SubscribeAsync(channel, (_, msg) => { if (msg == "message") Interlocked.Increment(ref gotB); });
        await Task.WhenAll(tA, tB).ForAwait();
        Assert.Equal(2, pub.Publish(channel, "message"));
        await AllowReasonableTimeToPublishAndProcess().ForAwait();
        Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
        Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));

        // and unsubscribe...
        tA = listenA.UnsubscribeAsync(channel);
        await tA;
        Assert.Equal(1, pub.Publish(channel, "message"));
        await AllowReasonableTimeToPublishAndProcess().ForAwait();
        Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
        Assert.Equal(2, Interlocked.CompareExchange(ref gotB, 0, 0));
    }

    [Fact]
    public async Task Issue38()
    {
        using var conn = Create(log: Writer);

        var sub = conn.GetSubscriber();
        int count = 0;
        var prefix = Me();
        void handler(RedisChannel _, RedisValue __) => Interlocked.Increment(ref count);
#pragma warning disable CS0618
        var a0 = sub.SubscribeAsync(prefix + "foo", handler);
        var a1 = sub.SubscribeAsync(prefix + "bar", handler);
        var b0 = sub.SubscribeAsync(prefix + "f*o", handler);
        var b1 = sub.SubscribeAsync(prefix + "b*r", handler);
#pragma warning restore CS0618
        await Task.WhenAll(a0, a1, b0, b1).ForAwait();

#pragma warning disable CS0618
        var c = sub.PublishAsync(prefix + "foo", "foo");
        var d = sub.PublishAsync(prefix + "f@o", "f@o");
        var e = sub.PublishAsync(prefix + "bar", "bar");
        var f = sub.PublishAsync(prefix + "b@r", "b@r");
#pragma warning restore CS0618
        await Task.WhenAll(c, d, e, f).ForAwait();

        long total = c.Result + d.Result + e.Result + f.Result;

        await AllowReasonableTimeToPublishAndProcess().ForAwait();

        Assert.Equal(6, total); // sent
        Assert.Equal(6, Interlocked.CompareExchange(ref count, 0, 0)); // received
    }

    internal static Task AllowReasonableTimeToPublishAndProcess() => Task.Delay(500);

    [Fact]
    public async Task TestPartialSubscriberGetMessage()
    {
        using var connA = Create();
        using var connB = Create();
        using var connPub = Create();

        int gotA = 0, gotB = 0;
        var listenA = connA.GetSubscriber();
        var listenB = connB.GetSubscriber();
        var pub = connPub.GetSubscriber();
        var prefix = Me();
#pragma warning disable CS0618
        var tA = listenA.SubscribeAsync(prefix + "channel", (s, msg) => { if (s == prefix + "channel" && msg == "message") Interlocked.Increment(ref gotA); });
        var tB = listenB.SubscribeAsync(prefix + "chann*", (s, msg) => { if (s == prefix + "channel" && msg == "message") Interlocked.Increment(ref gotB); });
        await Task.WhenAll(tA, tB).ForAwait();
        Assert.Equal(2, pub.Publish(prefix + "channel", "message"));
#pragma warning restore CS0618
        await AllowReasonableTimeToPublishAndProcess().ForAwait();
        Assert.Equal(1, Interlocked.CompareExchange(ref gotA, 0, 0));
        Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));

        // and unsubscibe...
#pragma warning disable CS0618
        tB = listenB.UnsubscribeAsync(prefix + "chann*", null);
        await tB;
        Assert.Equal(1, pub.Publish(prefix + "channel", "message"));
#pragma warning restore CS0618
        await AllowReasonableTimeToPublishAndProcess().ForAwait();
        Assert.Equal(2, Interlocked.CompareExchange(ref gotA, 0, 0));
        Assert.Equal(1, Interlocked.CompareExchange(ref gotB, 0, 0));
    }

    [Fact]
    public async Task TestSubscribeUnsubscribeAndSubscribeAgain()
    {
        using var connPub = Create();
        using var connSub = Create();

        var prefix = Me();
        var pub = connPub.GetSubscriber();
        var sub = connSub.GetSubscriber();
        int x = 0, y = 0;
#pragma warning disable CS0618
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
#pragma warning restore CS0618
        await AllowReasonableTimeToPublishAndProcess().ForAwait();
        Assert.Equal(2, Volatile.Read(ref x));
        Assert.Equal(2, Volatile.Read(ref y));
    }

    [Fact]
    public async Task AzureRedisEventsAutomaticSubscribe()
    {
        Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);
        Skip.IfNoConfig(nameof(TestConfig.Config.AzureCachePassword), TestConfig.Current.AzureCachePassword);

        bool didUpdate = false;
        var options = new ConfigurationOptions()
        {
            EndPoints = { TestConfig.Current.AzureCacheServer },
            Password = TestConfig.Current.AzureCachePassword,
            Ssl = true
        };

        using (var connection = await ConnectionMultiplexer.ConnectAsync(options))
        {
            connection.ServerMaintenanceEvent += (object? _, ServerMaintenanceEvent e) =>
            {
                if (e is AzureMaintenanceEvent)
                {
                    didUpdate = true;
                }
            };

            var pubSub = connection.GetSubscriber();
            await pubSub.PublishAsync(RedisChannel.Literal("AzureRedisEvents"), "HI");
            await Task.Delay(100);

            Assert.True(didUpdate);
        }
    }
}
