using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class Issue1101Tests : TestBase
{
    public Issue1101Tests(ITestOutputHelper output) : base(output) { }

    private static void AssertCounts(ISubscriber pubsub, in RedisChannel channel, bool has, int handlers, int queues)
    {
        if (pubsub.Multiplexer is ConnectionMultiplexer muxer)
        {
            var aHas = muxer.GetSubscriberCounts(channel, out var ah, out var aq);
            Assert.Equal(has, aHas);
            Assert.Equal(handlers, ah);
            Assert.Equal(queues, aq);
        }
    }

    [Fact]
    public async Task ExecuteWithUnsubscribeViaChannel()
    {
        using var conn = Create(log: Writer);

        RedisChannel name = RedisChannel.Literal(Me());
        var pubsub = conn.GetSubscriber();
        AssertCounts(pubsub, name, false, 0, 0);

        // subscribe and check we get data
        var first = await pubsub.SubscribeAsync(name);
        var second = await pubsub.SubscribeAsync(name);
        AssertCounts(pubsub, name, true, 0, 2);
        var values = new List<string?>();
        int i = 0;
        first.OnMessage(x =>
        {
            lock (values) { values.Add(x.Message); }
            return Task.CompletedTask;
        });
        second.OnMessage(_ => Interlocked.Increment(ref i));
        await Task.Delay(200);
        await pubsub.PublishAsync(name, "abc");
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => values.Count == 1);
        lock (values)
        {
            Assert.Equal("abc", Assert.Single(values));
        }
        var subs = conn.GetServer(conn.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
        Assert.Equal(1, subs);
        Assert.False(first.Completion.IsCompleted, "completed");
        Assert.False(second.Completion.IsCompleted, "completed");

        await first.UnsubscribeAsync();
        await Task.Delay(200);
        await pubsub.PublishAsync(name, "def");
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => values.Count == 1 && Volatile.Read(ref i) == 2);
        lock (values)
        {
            Assert.Equal("abc", Assert.Single(values));
        }
        Assert.Equal(2, Volatile.Read(ref i));
        Assert.True(first.Completion.IsCompleted, "completed");
        Assert.False(second.Completion.IsCompleted, "completed");
        AssertCounts(pubsub, name, true, 0, 1);

        await second.UnsubscribeAsync();
        await Task.Delay(200);
        await pubsub.PublishAsync(name, "ghi");
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => values.Count == 1);
        lock (values)
        {
            Assert.Equal("abc", Assert.Single(values));
        }
        Assert.Equal(2, Volatile.Read(ref i));
        Assert.True(first.Completion.IsCompleted, "completed");
        Assert.True(second.Completion.IsCompleted, "completed");
        AssertCounts(pubsub, name, false, 0, 0);

        subs = conn.GetServer(conn.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
        Assert.Equal(0, subs);
        Assert.True(first.Completion.IsCompleted, "completed");
        Assert.True(second.Completion.IsCompleted, "completed");
    }

    [Fact]
    public async Task ExecuteWithUnsubscribeViaSubscriber()
    {
        using var conn = Create(shared: false, log: Writer);

        RedisChannel name = RedisChannel.Literal(Me());
        var pubsub = conn.GetSubscriber();
        AssertCounts(pubsub, name, false, 0, 0);

        // subscribe and check we get data
        var first = await pubsub.SubscribeAsync(name);
        var second = await pubsub.SubscribeAsync(name);
        AssertCounts(pubsub, name, true, 0, 2);
        var values = new List<string?>();
        int i = 0;
        first.OnMessage(x =>
        {
            lock (values) { values.Add(x.Message); }
            return Task.CompletedTask;
        });
        second.OnMessage(_ => Interlocked.Increment(ref i));

        await Task.Delay(100);
        await pubsub.PublishAsync(name, "abc");
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => values.Count == 1);
        lock (values)
        {
            Assert.Equal("abc", Assert.Single(values));
        }
        var subs = conn.GetServer(conn.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
        Assert.Equal(1, subs);
        Assert.False(first.Completion.IsCompleted, "completed");
        Assert.False(second.Completion.IsCompleted, "completed");

        await pubsub.UnsubscribeAsync(name);
        await Task.Delay(100);
        await pubsub.PublishAsync(name, "def");
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => values.Count == 1);
        lock (values)
        {
            Assert.Equal("abc", Assert.Single(values));
        }
        Assert.Equal(1, Volatile.Read(ref i));

        subs = conn.GetServer(conn.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
        Assert.Equal(0, subs);
        Assert.True(first.Completion.IsCompleted, "completed");
        Assert.True(second.Completion.IsCompleted, "completed");
        AssertCounts(pubsub, name, false, 0, 0);
    }

    [Fact]
    public async Task ExecuteWithUnsubscribeViaClearAll()
    {
        using var conn = Create(log: Writer);

        RedisChannel name = RedisChannel.Literal(Me());
        var pubsub = conn.GetSubscriber();
        AssertCounts(pubsub, name, false, 0, 0);

        // subscribe and check we get data
        var first = await pubsub.SubscribeAsync(name);
        var second = await pubsub.SubscribeAsync(name);
        AssertCounts(pubsub, name, true, 0, 2);
        var values = new List<string?>();
        int i = 0;
        first.OnMessage(x =>
        {
            lock (values) { values.Add(x.Message); }
            return Task.CompletedTask;
        });
        second.OnMessage(_ => Interlocked.Increment(ref i));
        await Task.Delay(100);
        await pubsub.PublishAsync(name, "abc");
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => values.Count == 1);
        lock (values)
        {
            Assert.Equal("abc", Assert.Single(values));
        }
        var subs = conn.GetServer(conn.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
        Assert.Equal(1, subs);
        Assert.False(first.Completion.IsCompleted, "completed");
        Assert.False(second.Completion.IsCompleted, "completed");

        await pubsub.UnsubscribeAllAsync();
        await Task.Delay(100);
        await pubsub.PublishAsync(name, "def");
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => values.Count == 1);
        lock (values)
        {
            Assert.Equal("abc", Assert.Single(values));
        }
        Assert.Equal(1, Volatile.Read(ref i));

        subs = conn.GetServer(conn.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
        Assert.Equal(0, subs);
        Assert.True(first.Completion.IsCompleted, "completed");
        Assert.True(second.Completion.IsCompleted, "completed");
        AssertCounts(pubsub, name, false, 0, 0);
    }
}
