using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue1101 : TestBase
    {
        public Issue1101(ITestOutputHelper output) : base(output) { }

        static void AssertCounts(ISubscriber pubsub, in RedisChannel channel,
            bool has, int handlers, int queues)
        {
            var aHas = ((RedisSubscriber)pubsub).GetSubscriberCounts(channel, out var ah, out var aq);
            Assert.Equal(has, aHas);
            Assert.Equal(handlers, ah);
            Assert.Equal(queues, aq);
        }
        [Fact]
        public async Task ExecuteWithUnsubscribeViaChannel()
        {
            using (var muxer = Create())
            {
                RedisChannel name = Me();
                var pubsub = muxer.GetSubscriber();
                AssertCounts(pubsub, name, false, 0, 0);

                // subscribe and check we get data
                var first = await pubsub.SubscribeAsync(name);
                var second = await pubsub.SubscribeAsync(name);
                AssertCounts(pubsub, name, true, 0, 2);
                List<string> values = new List<string>();
                int i = 0;
                first.OnMessage(x =>
                {
                    lock (values) { values.Add(x.Message); }
                    return Task.CompletedTask;
                });
                second.OnMessage(_ => Interlocked.Increment(ref i));
                await Task.Delay(200);
                await pubsub.PublishAsync(name, "abc");
                await UntilCondition(TimeSpan.FromSeconds(10), () => values.Count == 1);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                var subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(1, subs);
                Assert.False(first.Completion.IsCompleted, "completed");
                Assert.False(second.Completion.IsCompleted, "completed");

                await first.UnsubscribeAsync();
                await Task.Delay(200);
                await pubsub.PublishAsync(name, "def");
                await UntilCondition(TimeSpan.FromSeconds(10), () => values.Count == 1 && Volatile.Read(ref i) == 2);
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
                await UntilCondition(TimeSpan.FromSeconds(10), () => values.Count == 1);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                Assert.Equal(2, Volatile.Read(ref i));
                Assert.True(first.Completion.IsCompleted, "completed");
                Assert.True(second.Completion.IsCompleted, "completed");
                AssertCounts(pubsub, name, false, 0, 0);


                subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(0, subs);
                Assert.True(first.Completion.IsCompleted, "completed");
                Assert.True(second.Completion.IsCompleted, "completed");
            }
        }

        [Fact]
        public async Task ExecuteWithUnsubscribeViaSubscriber()
        {
            using (var muxer = Create())
            {
                RedisChannel name = Me();
                var pubsub = muxer.GetSubscriber();
                AssertCounts(pubsub, name, false, 0, 0);

                // subscribe and check we get data
                var first = await pubsub.SubscribeAsync(name);
                var second = await pubsub.SubscribeAsync(name);
                AssertCounts(pubsub, name, true, 0, 2);
                List<string> values = new List<string>();
                int i = 0;
                first.OnMessage(x =>
                {
                    lock (values) { values.Add(x.Message); }
                    return Task.CompletedTask;
                });
                second.OnMessage(_ => Interlocked.Increment(ref i));

                await Task.Delay(100);
                await pubsub.PublishAsync(name, "abc");
                await UntilCondition(TimeSpan.FromSeconds(10), () => values.Count == 1);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                var subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(1, subs);
                Assert.False(first.Completion.IsCompleted, "completed");
                Assert.False(second.Completion.IsCompleted, "completed");

                await pubsub.UnsubscribeAsync(name);
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "def");
                await UntilCondition(TimeSpan.FromSeconds(10), () => values.Count == 1);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                Assert.Equal(1, Volatile.Read(ref i));

                subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(0, subs);
                Assert.True(first.Completion.IsCompleted, "completed");
                Assert.True(second.Completion.IsCompleted, "completed");
                AssertCounts(pubsub, name, false, 0, 0);
            }
        }

        [Fact]
        public async Task ExecuteWithUnsubscribeViaClearAll()
        {
            using (var muxer = Create())
            {
                RedisChannel name = Me();
                var pubsub = muxer.GetSubscriber();
                AssertCounts(pubsub, name, false, 0, 0);

                // subscribe and check we get data
                var first = await pubsub.SubscribeAsync(name);
                var second = await pubsub.SubscribeAsync(name);
                AssertCounts(pubsub, name, true, 0, 2);
                List<string> values = new List<string>();
                int i = 0;
                first.OnMessage(x =>
                {
                    lock (values) { values.Add(x.Message); }
                    return Task.CompletedTask;
                });
                second.OnMessage(_ => Interlocked.Increment(ref i));
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "abc");
                await UntilCondition(TimeSpan.FromSeconds(10), () => values.Count == 1);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                var subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(1, subs);
                Assert.False(first.Completion.IsCompleted, "completed");
                Assert.False(second.Completion.IsCompleted, "completed");

                await pubsub.UnsubscribeAllAsync();
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "def");
                await UntilCondition(TimeSpan.FromSeconds(10), () => values.Count == 1);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                Assert.Equal(1, Volatile.Read(ref i));

                subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(0, subs);
                Assert.True(first.Completion.IsCompleted, "completed");
                Assert.True(second.Completion.IsCompleted, "completed");
                AssertCounts(pubsub, name, false, 0, 0);
            }
        }
    }
}
