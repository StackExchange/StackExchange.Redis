using System;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A proxied deployment can answer <c>HELLO</c> with <c>mode: cluster</c> while <c>INFO</c> says
/// <c>redis_mode:standalone</c> and every <c>CLUSTER</c> subcommand is refused (Azure Managed Redis with the
/// Enterprise clustering policy; see #3284). Such a server gives the client no slot map, so it must not be
/// treated as a cluster - in particular, cross-slot commands must keep working, including after a reconnect
/// of the subscription connection alone, whose handshake is <c>HELLO</c> without the <c>INFO</c> that follows
/// on the interactive connection.
/// </summary>
public class HelloClusterModeUnitTests(ITestOutputHelper log)
{
    private sealed class HelloSaysClusterServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        protected override string HelloModeValue => "cluster";

        protected override TypedRedisValue ClusterSlots(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Error("ERR command is not allowed");

        protected override TypedRedisValue ClusterNodes(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Error("ERR command is not allowed");
    }

    [Theory]
    [InlineData(RedisProtocol.Resp2)]
    [InlineData(RedisProtocol.Resp3)]
    public async Task CrossSlotSurvivesSubscriptionReconnect(RedisProtocol protocol)
    {
        using var server = new HelloSaysClusterServer(log); // ServerType stays Standalone: INFO says so

        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = protocol;
        config.AllowSimulateConnectionFailure = true;
        config.AbortOnConnectFail = false;
        var logger = new CapturingLogger();
        config.LoggerFactory = logger;
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var db = conn.GetDatabase();

        RedisKey[] crossSlot = ["a", "b"]; // different hash slots
        await db.StringGetAsync(crossSlot); // sanity: fine on a fresh connection
        Assert.Equal(ServerType.Standalone, conn.ServerSelectionStrategy.ServerType);

        // the new core opens the subscription connection on first use rather than at connect, so give it a
        // reason to exist: without one there is nothing to drop below, and the test would time out waiting
        // for a reconnect that never has to happen
        await conn.GetSubscriber().SubscribeAsync(RedisChannel.Literal("hello-mode"), static (_, _) => { });
        var endpoint = conn.GetServerSnapshot()[0];
        // the dedicated subscription connection is lazy (and this config has no configuration channel), so give it
        // a reason to exist before we try to drop it
        await conn.GetSubscriber().SubscribeAsync(RedisChannel.Literal(nameof(CrossSlotSurvivesSubscriptionReconnect)), (_, _) => { });
        await WaitUntilAsync(() => endpoint.IsSubscriberConnected, "subscriber connected");

        // drop *only* the subscription connection, as the server did in #3284; the reconnect is too quick
        // to observe by polling, so watch for the restored event instead
        var restored = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.ConnectionRestored += (_, e) =>
        {
            if (e.ConnectionType == ConnectionType.Subscription) restored.TrySetResult(true);
        };
        log.WriteLine("dropping the subscription connection");
        endpoint.SimulateConnectionFailure(SimulatedFailureType.AllSubscription);
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => endpoint.IsSubscriberConnected, "subscriber reconnected");

        // the restored connection triggers a fire-and-forget reconfigure (and so can a server-type change);
        // ReconfigureAsync declines while one is in flight, so insist on one that actually ran
        await Poll.ReconfigureAsync(conn, "after subscription reconnect");

        try
        {
            // the user-visible symptom from #3284: cross-slot commands refused client-side
            for (int i = 0; i < 5; i++)
            {
                await db.StringGetAsync(crossSlot);
                await Task.Delay(100);
            }
            Assert.Equal(ServerType.Standalone, endpoint.ServerType);
            Assert.Equal(ServerType.Standalone, conn.ServerSelectionStrategy.ServerType);
        }
        finally
        {
            log.WriteLine(logger.All);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for: " + what);
            await Task.Delay(50);
        }
    }
}
