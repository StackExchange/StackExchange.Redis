using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue2507 : TestBase
    {
        public Issue2507(ITestOutputHelper output, SharedConnectionFixture? fixture = null)
            : base(output, fixture) { }

        [Fact]
        public async Task Execute()
        {
            using var conn = Create();
            var db = conn.GetDatabase();
            var pubsub = conn.GetSubscriber();
            var queue = await pubsub.SubscribeAsync(RedisChannel.Literal("__redis__:invalidate"));
            await Task.Delay(100);
            var connectionId = conn.GetConnectionId(conn.GetEndPoints().Single(), ConnectionType.Subscription);
            if (connectionId is null) Skip.Inconclusive("Connection id not available");
            await db.StringSetAsync(new KeyValuePair<RedisKey, RedisValue>[] { new("abc", "def"), new("ghi", "jkl"), new("mno", "pqr") });
            // this is not supported, but: we want it to at least not fail
            await db.ExecuteAsync("CLIENT", "TRACKING", "on", "REDIRECT", connectionId!.Value, "BCAST");
            await db.KeyDeleteAsync(new RedisKey[] { "abc", "ghi", "mno" });
            await Task.Delay(100);
            queue.Unsubscribe();
            Assert.True(queue.TryRead(out var message));
            Assert.Equal("abc", message.Message);
            Assert.True(queue.TryRead(out message));
            Assert.Equal("ghi", message.Message);
            Assert.True(queue.TryRead(out message));
            Assert.Equal("mno", message.Message);
            Assert.False(queue.TryRead(out message));
        }
    }
}
