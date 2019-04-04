using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue1101 : TestBase
    {
        public Issue1101(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task ExecuteWithUnsubscribeViaChannel()
        {
            using (var muxer = Create())
            {
                RedisChannel name = Me();
                var pubsub = muxer.GetSubscriber();

                // subscribe and check we get data
                var channel = await pubsub.SubscribeAsync(name);
                List<string> values = new List<string>();
                channel.OnMessage(x =>
                {
                    lock (values) { values.Add(x.Message); }
                    return Task.CompletedTask;
                });
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "abc");
                await Task.Delay(100);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                var subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(1, subs);
                Assert.False(channel.Completion.IsCompleted, "completed");

                await channel.UnsubscribeAsync();
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "def");
                await Task.Delay(100);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }

                subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(0, subs);
                Assert.True(channel.Completion.IsCompleted, "completed");
            }
        }

        [Fact]
        public async Task ExecuteWithUnsubscribeViaSubscriber()
        {
            using (var muxer = Create())
            {
                RedisChannel name = Me();
                var pubsub = muxer.GetSubscriber();

                // subscribe and check we get data
                var channel = await pubsub.SubscribeAsync(name);
                List<string> values = new List<string>();
                channel.OnMessage(x =>
                {
                    lock (values) { values.Add(x.Message); }
                    return Task.CompletedTask;
                });
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "abc");
                await Task.Delay(100);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                var subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(1, subs);
                Assert.False(channel.Completion.IsCompleted, "completed");

                await pubsub.UnsubscribeAsync(name);
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "def");
                await Task.Delay(100);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }

                subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(0, subs);
                Assert.True(channel.Completion.IsCompleted, "completed");
            }
        }

        [Fact]
        public async Task ExecuteWithUnsubscribeViaClearAll()
        {
            using (var muxer = Create())
            {
                RedisChannel name = Me();
                var pubsub = muxer.GetSubscriber();

                // subscribe and check we get data
                var channel = await pubsub.SubscribeAsync(name);
                List<string> values = new List<string>();
                channel.OnMessage(x =>
                {
                    lock (values) { values.Add(x.Message); }
                    return Task.CompletedTask;
                });
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "abc");
                await Task.Delay(100);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }
                var subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(1, subs);
                Assert.False(channel.Completion.IsCompleted, "completed");

                await pubsub.UnsubscribeAllAsync();
                await Task.Delay(100);
                await pubsub.PublishAsync(name, "def");
                await Task.Delay(100);
                lock (values)
                {
                    Assert.Equal("abc", Assert.Single(values));
                }

                subs = muxer.GetServer(muxer.GetEndPoints().Single()).SubscriptionSubscriberCount(name);
                Assert.Equal(0, subs);
                Assert.True(channel.Completion.IsCompleted, "completed");
            }
        }
    }
}
