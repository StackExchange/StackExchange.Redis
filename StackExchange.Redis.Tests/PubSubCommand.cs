using System;
using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class PubSubCommand : TestBase
    {
        [Test]
        public void SubscriberCount()
        {
            using(var conn = Create())
            {
                RedisChannel channel = Me() + Guid.NewGuid();
                var server = conn.GetServer(conn.GetEndPoints()[0]);

                var channels = server.SubscriptionChannels(Me() + "*");
                Assert.IsFalse(channels.Contains(channel));

                long justWork = server.SubscriptionPatternCount();
                var count = server.SubscriptionSubscriberCount(channel);
                Assert.AreEqual(0, count);
                conn.GetSubscriber().Subscribe(channel, delegate { });
                count = server.SubscriptionSubscriberCount(channel);
                Assert.AreEqual(1, count);

                channels = server.SubscriptionChannels(Me() + "*");
                Assert.IsTrue(channels.Contains(channel));
            }
        }
    }
}
