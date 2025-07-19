using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

[Collection(NonParallelCollection.Name)]
public class Issue2507(ITestOutputHelper output, SharedConnectionFixture? fixture = null) : TestBase(output, fixture)
{
    [Fact]
    public async Task Execute()
    {
        await using var conn = Create(shared: false);
        var db = conn.GetDatabase();
        var pubsub = conn.GetSubscriber();
        var queue = await pubsub.SubscribeAsync(RedisChannel.Literal("__redis__:invalidate"));
        await Task.Delay(100);
        var connectionId = conn.GetConnectionId(conn.GetEndPoints().Single(), ConnectionType.Subscription);
        if (connectionId is null) Assert.Skip("Connection id not available");

        string baseKey = Me();
        RedisKey key1 = baseKey + "abc",
                 key2 = baseKey + "ghi",
                 key3 = baseKey + "mno";

        await db.StringSetAsync([new(key1, "def"), new(key2, "jkl"), new(key3, "pqr")]);
        // this is not supported, but: we want it to at least not fail
        await db.ExecuteAsync("CLIENT", "TRACKING", "on", "REDIRECT", connectionId!.Value, "BCAST");
        await db.KeyDeleteAsync([key1, key2, key3]);
        await Task.Delay(100);
        queue.Unsubscribe();
        Assert.True(queue.TryRead(out var message), "Queue 1 Read failed");
        Assert.Equal(key1, message.Message);
        Assert.True(queue.TryRead(out message), "Queue 2 Read failed");
        Assert.Equal(key2, message.Message);
        Assert.True(queue.TryRead(out message), "Queue 3 Read failed");
        Assert.Equal(key3, message.Message);
        // Paralle test suites can be invalidating at the same time, so this is not guaranteed to be empty
        // Assert.False(queue.TryRead(out message), "Queue 4 Read succeeded");
    }
}
