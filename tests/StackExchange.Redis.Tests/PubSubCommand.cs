using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class PubSubCommand : TestBase
    {
        public PubSubCommand(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

        [Fact]
        public void SubscriberCount()
        {
            using (var conn = Create())
            {
                RedisChannel channel = Me() + Guid.NewGuid();
                var server = conn.GetServer(conn.GetEndPoints()[0]);

                var channels = server.SubscriptionChannels(Me() + "*");
                Assert.DoesNotContain(channel, channels);

                long justWork = server.SubscriptionPatternCount();
                var count = server.SubscriptionSubscriberCount(channel);
                Assert.Equal(0, count);
                conn.GetSubscriber().Subscribe(channel, delegate { });
                count = server.SubscriptionSubscriberCount(channel);
                Assert.Equal(1, count);

                channels = server.SubscriptionChannels(Me() + "*");
                Assert.Contains(channel, channels);
            }
        }
    }
}
