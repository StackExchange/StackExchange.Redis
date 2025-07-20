using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(NonParallelCollection.Name)]
public class ClusterShardedTests(ITestOutputHelper output) : TestBase(output)
{
    protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts + ",connectTimeout=10000";

    [Fact]
    public async Task TestShardedPubsubSubscriberAgainstReconnects()
    {
        Skip.UnlessLongRunning();
        var channel = RedisChannel.Sharded(Me());
        await using var conn = Create(allowAdmin: true, keepAlive: 1, connectTimeout: 3000, shared: false, require: RedisFeatures.v7_0_0_rc1);
        Assert.True(conn.IsConnected);
        var db = conn.GetDatabase();
        Assert.Equal(0, await db.PublishAsync(channel, "noClientReceivesThis"));
        await Task.Delay(50); // let the sub settle (this isn't needed on RESP3, note)

        var pubsub = conn.GetSubscriber();
        List<(RedisChannel, RedisValue)> received = [];
        var queue = await pubsub.SubscribeAsync(channel);
        _ = Task.Run(async () =>
        {
            // use queue API to have control over order
            await foreach (var item in queue)
            {
                lock (received)
                {
                    if (item.Channel.IsSharded && item.Channel == channel) received.Add((item.Channel, item.Message));
                }
            }
        });
        Assert.Equal(1, conn.GetSubscriptionsCount());

        await Task.Delay(50); // let the sub settle (this isn't needed on RESP3, note)
        await db.PingAsync();

        for (int i = 0; i < 5; i++)
        {
            // check we get a hit
            Assert.Equal(1, await db.PublishAsync(channel, i.ToString()));
        }
        await Task.Delay(50); // let the sub settle (this isn't needed on RESP3, note)

        // this is endpoint at index 1 which has the hashslot for "testShardChannel"
        var server = conn.GetServer(conn.GetEndPoints()[1]);
        server.SimulateConnectionFailure(SimulatedFailureType.All);
        SetExpectedAmbientFailureCount(2);

        await Task.Delay(4000);
        for (int i = 0; i < 5; i++)
        {
            // check we get a hit
            Assert.Equal(1, await db.PublishAsync(channel, i.ToString()));
        }
        await Task.Delay(50); // let the sub settle (this isn't needed on RESP3, note)

        Assert.Equal(1, conn.GetSubscriptionsCount());
        Assert.Equal(10, received.Count);
        ClearAmbientFailures();
    }

    [Fact]
    public async Task TestShardedPubsubSubscriberAgainsHashSlotMigration()
    {
        Skip.UnlessLongRunning();
        var channel = RedisChannel.Sharded(Me());
        await using var conn = Create(allowAdmin: true, keepAlive: 1, connectTimeout: 3000, shared: false, require: RedisFeatures.v7_0_0_rc1);
        Assert.True(conn.IsConnected);
        var db = conn.GetDatabase();
        Assert.Equal(0, await db.PublishAsync(channel, "noClientReceivesThis"));
        await Task.Delay(50); // let the sub settle (this isn't needed on RESP3, note)

        var pubsub = conn.GetSubscriber();
        List<(RedisChannel, RedisValue)> received = [];
        var queue = await pubsub.SubscribeAsync(channel);
        _ = Task.Run(async () =>
        {
            // use queue API to have control over order
            await foreach (var item in queue)
            {
                lock (received)
                {
                    if (item.Channel.IsSharded && item.Channel == channel) received.Add((item.Channel, item.Message));
                }
            }
        });
        Assert.Equal(1, conn.GetSubscriptionsCount());

        await Task.Delay(50); // let the sub settle (this isn't needed on RESP3, note)
        await db.PingAsync();

        for (int i = 0; i < 5; i++)
        {
            // check we get a hit
            Assert.Equal(1, await db.PublishAsync(channel, i.ToString()));
        }
        await Task.Delay(50); // let the sub settle (this isn't needed on RESP3, note)

        // lets migrate the slot for "testShardChannel" to another node
        await DoHashSlotMigrationAsync();

        await Task.Delay(4000);
        for (int i = 0; i < 5; i++)
        {
            // check we get a hit
            Assert.Equal(1, await db.PublishAsync(channel, i.ToString()));
        }
        await Task.Delay(50); // let the sub settle (this isn't needed on RESP3, note)

        Assert.Equal(1, conn.GetSubscriptionsCount());
        Assert.Equal(10, received.Count);
        await RollbackHashSlotMigrationAsync();
        ClearAmbientFailures();
    }

    private Task DoHashSlotMigrationAsync() => MigrateSlotForTestShardChannelAsync(false);
    private Task RollbackHashSlotMigrationAsync() => MigrateSlotForTestShardChannelAsync(true);

    private async Task MigrateSlotForTestShardChannelAsync(bool rollback)
    {
        int hashSlotForTestShardChannel = 7177;
        await using var conn = Create(allowAdmin: true, keepAlive: 1, connectTimeout: 5000, shared: false);
        var servers = conn.GetServers();
        IServer? serverWithPort7000 = null;
        IServer? serverWithPort7001 = null;

        string nodeIdForPort7000 = "780813af558af81518e58e495d63b6e248e80adf";
        string nodeIdForPort7001 = "ea828c6074663c8bd4e705d3e3024d9d1721ef3b";
        foreach (var server in servers)
        {
            string id = server.Execute("CLUSTER", "MYID").ToString();
            if (id == nodeIdForPort7000)
            {
                serverWithPort7000 = server;
            }
            if (id == nodeIdForPort7001)
            {
                serverWithPort7001 = server;
            }
        }

        IServer fromServer, toServer;
        string fromNode, toNode;
        if (rollback)
        {
            fromServer = serverWithPort7000!;
            fromNode = nodeIdForPort7000;
            toServer = serverWithPort7001!;
            toNode = nodeIdForPort7001;
        }
        else
        {
            fromServer = serverWithPort7001!;
            fromNode = nodeIdForPort7001;
            toServer = serverWithPort7000!;
            toNode = nodeIdForPort7000;
        }

        try
        {
            Assert.Equal("OK", toServer.Execute("CLUSTER", "SETSLOT", hashSlotForTestShardChannel, "IMPORTING", fromNode).ToString());
            Assert.Equal("OK", fromServer.Execute("CLUSTER", "SETSLOT", hashSlotForTestShardChannel, "MIGRATING", toNode).ToString());
            Assert.Equal("OK", toServer.Execute("CLUSTER", "SETSLOT", hashSlotForTestShardChannel, "NODE", toNode).ToString());
            Assert.Equal("OK", fromServer!.Execute("CLUSTER", "SETSLOT", hashSlotForTestShardChannel, "NODE", toNode).ToString());
        }
        catch (RedisServerException ex) when (ex.Message == "ERR I'm already the owner of hash slot 7177")
        {
            Log("Slot already migrated.");
        }
    }
}
