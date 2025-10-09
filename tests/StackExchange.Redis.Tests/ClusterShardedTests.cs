using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubscribeToWrongServerAsync(bool sharded)
    {
        // the purpose of this test is to simulate subscribing while a node move is happening, i.e. we send
        // the SSUBSCRIBE to the wrong server, get a -MOVED, and redirect; in particular: do we end up *knowing*
        // where we actually subscribed to?
        //
        // note: to check our thinking, we also do this for regular non-sharded channels too; the point here
        // being that this should behave *differently*, since there will be no -MOVED
        var name = $"{Me()}:{Guid.NewGuid()}";
        var channel = sharded ? RedisChannel.Sharded(name) : RedisChannel.Literal(name).WithKeyRouting();
        await using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var asKey = (RedisKey)(byte[])channel!;
        Assert.False(asKey.IsEmpty);
        var shouldBeServer = conn.GetServer(asKey); // this is where it *should* go

        // now intentionally choose *a different* server
        var server = conn.GetServers().First(s => !Equals(s.EndPoint, shouldBeServer.EndPoint));
        Log($"Should be {Format.ToString(shouldBeServer.EndPoint)}; routing via {Format.ToString(server.EndPoint)}");

        var subscriber = Assert.IsType<RedisSubscriber>(conn.GetSubscriber());
        var serverEndpoint = conn.GetServerEndPoint(server.EndPoint);
        Assert.Equal(server.EndPoint, serverEndpoint.EndPoint);
        var queue = await subscriber.SubscribeAsync(channel, server: serverEndpoint);
        await Task.Delay(50);
        var actual = subscriber.SubscribedEndpoint(channel);

        if (sharded)
        {
            // we should end up at the correct node, following the -MOVED
            Assert.Equal(shouldBeServer.EndPoint, actual);
        }
        else
        {
            // we should end up where we *actually sent the message* - there is no -MOVED
            Assert.Equal(serverEndpoint.EndPoint, actual);
        }
        await queue.UnsubscribeAsync();
    }

    [Fact]
    public async Task KeepSubscribedThroughSlotMigrationAsync()
    {
        await using var conn = Create(require: RedisFeatures.v7_0_0_rc1, allowAdmin: true);
        var name = $"{Me()}:{Guid.NewGuid()}";
        var channel = RedisChannel.Sharded(name);
        var subscriber = conn.GetSubscriber();
        var queue = await subscriber.SubscribeAsync(channel);
        await Task.Delay(50);
        var actual = subscriber.SubscribedEndpoint(channel);
        Assert.NotNull(actual);

        var asKey = (RedisKey)(byte[])channel!;
        Assert.False(asKey.IsEmpty);
        var slot = conn.GetHashSlot(asKey);
        var viaMap = conn.ServerSelectionStrategy.Select(slot, RedisCommand.SSUBSCRIBE, CommandFlags.None, allowDisconnected: false);

        Log($"Slot {slot}, subscribed to {Format.ToString(actual)} (mapped to {Format.ToString(viaMap?.EndPoint)})");
        Assert.NotNull(viaMap);
        Assert.Equal(actual, viaMap.EndPoint);

        var oldServer = conn.GetServer(asKey); // this is where it *should* go

        // now intentionally choose *a different* server
        var newServer = conn.GetServers().First(s => !Equals(s.EndPoint, oldServer.EndPoint));

        var nodes = await newServer.ClusterNodesAsync();
        Assert.NotNull(nodes);
        var fromNode = nodes[oldServer.EndPoint]?.NodeId;
        var toNode = nodes[newServer.EndPoint]?.NodeId;
        Assert.NotNull(fromNode);
        Assert.NotNull(toNode);
        Assert.Equal(oldServer.EndPoint, nodes.GetBySlot(slot)?.EndPoint);

        var ep = subscriber.SubscribedEndpoint(channel);
        Log($"Endpoint before migration: {Format.ToString(ep)}");
        Log($"Migrating slot {slot} to {Format.ToString(newServer.EndPoint)}; node {fromNode} -> {toNode}...");

        // see https://redis.io/docs/latest/commands/cluster-setslot/#redis-cluster-live-resharding-explained
        WriteLog("IMPORTING", await newServer.ExecuteAsync("CLUSTER", "SETSLOT", slot, "IMPORTING", fromNode));
        WriteLog("MIGRATING", await oldServer.ExecuteAsync("CLUSTER", "SETSLOT", slot, "MIGRATING", toNode));

        while (true)
        {
            var keys = (await oldServer.ExecuteAsync("CLUSTER", "GETKEYSINSLOT", slot, 100)).AsRedisKeyArray()!;
            Log($"Migrating {keys.Length} keys...");
            if (keys.Length == 0) break;
            foreach (var key in keys)
            {
                await conn.GetDatabase().KeyMigrateAsync(key, newServer.EndPoint, migrateOptions: MigrateOptions.None);
            }
        }

        WriteLog("NODE (old)", await newServer.ExecuteAsync("CLUSTER", "SETSLOT", slot, "NODE", toNode));
        WriteLog("NODE (new)", await oldServer.ExecuteAsync("CLUSTER", "SETSLOT", slot, "NODE", toNode));

        void WriteLog(string caption, RedisResult result)
        {
            if (result.IsNull)
            {
                Log($"{caption}: null");
            }
            else if (result.Length >= 0)
            {
                var arr = result.AsRedisValueArray()!;
                Log($"{caption}: {arr.Length} items");
                foreach (var item in arr)
                {
                    Log($"  {item}");
                }
            }
            else
            {
                Log($"{caption}: {result}");
            }
        }

        Log("Migration initiated; checking node state...");
        await Task.Delay(100);
        ep = subscriber.SubscribedEndpoint(channel);
        Log($"Endpoint after migration: {Format.ToString(ep)}");
        Assert.True(
            ep is null || ep == newServer.EndPoint,
            "Target server after migration should be null or the new server");

        nodes = await newServer.ClusterNodesAsync();
        Assert.NotNull(nodes);
        Assert.Equal(newServer.EndPoint, nodes.GetBySlot(slot)?.EndPoint);
        await conn.ConfigureAsync();
        Assert.Equal(newServer, conn.GetServer(asKey));

        // now publish... we *expect* things to have sorted themselves out
        var msg = Guid.NewGuid().ToString();
        var count = await subscriber.PublishAsync(channel, msg);
        Assert.Equal(1, count);

        Log("Waiting for message...");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var received = await queue.ReadAsync(timeout.Token);
        Assert.Equal(msg, (string)received.Message!);

        await queue.UnsubscribeAsync();
    }
}
