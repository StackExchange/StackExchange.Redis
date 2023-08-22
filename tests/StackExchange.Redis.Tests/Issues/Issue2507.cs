using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    [Collection(NonParallelCollection.Name)]
    public class Issue2507 : TestBase
    {
        public Issue2507(ITestOutputHelper output, SharedConnectionFixture? fixture = null)
            : base(output, fixture) { }

        [Fact]
        public async Task Execute()
        {
            using var conn = Create(shared: false);
            var db = conn.GetDatabase();
            var pubsub = conn.GetSubscriber();
            var queue = await pubsub.SubscribeAsync(RedisChannel.Literal("__redis__:invalidate"));
            await Task.Delay(100);
            var connectionId = conn.GetConnectionId(conn.GetEndPoints().Single(), ConnectionType.Subscription);
            if (connectionId is null) Skip.Inconclusive("Connection id not available");

            string baseKey = Me();
            RedisKey key1 = baseKey + "abc",
                     key2 = baseKey + "ghi",
                     key3 = baseKey + "mno";

            await db.StringSetAsync(new KeyValuePair<RedisKey, RedisValue>[] { new(key1, "def"), new(key2, "jkl"), new(key3, "pqr") });
            // this is not supported, but: we want it to at least not fail
            await db.ExecuteAsync("CLIENT", "TRACKING", "on", "REDIRECT", connectionId!.Value, "BCAST");
            await db.KeyDeleteAsync(new RedisKey[] { key1, key2, key3 });
            await Task.Delay(100);
            queue.Unsubscribe();
            Assert.True(queue.TryRead(out var message));
            Assert.Equal(key1, message.Message);
            Assert.True(queue.TryRead(out message));
            Assert.Equal(key2, message.Message);
            Assert.True(queue.TryRead(out message));
            Assert.Equal(key3, message.Message);
            Assert.False(queue.TryRead(out message));
        }
    }
}
