#if NET6_0_OR_GREATER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class FailoverTests : TestBase, IAsyncLifetime
{
    protected override string GetConfiguration() => GetPrimaryReplicaConfig().ToString();

    public FailoverTests(ITestOutputHelper output) : base(output) { }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        using var conn = Create();

        var shouldBePrimary = conn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort);
        if (shouldBePrimary.IsReplica)
        {
            Log(shouldBePrimary.EndPoint + " should be primary, fixing...");
            await shouldBePrimary.MakePrimaryAsync(ReplicationChangeOptions.SetTiebreaker);
        }

        var shouldBeReplica = conn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort);
        if (!shouldBeReplica.IsReplica)
        {
            Log(shouldBeReplica.EndPoint + " should be a replica, fixing...");
            await shouldBeReplica.ReplicaOfAsync(shouldBePrimary.EndPoint);
            await Task.Delay(2000).ForAwait();
        }
    }

    private static ConfigurationOptions GetPrimaryReplicaConfig()
    {
        return new ConfigurationOptions
        {
            AllowAdmin = true,
            SyncTimeout = 100000,
            EndPoints =
            {
                { TestConfig.Current.FailoverPrimaryServer, TestConfig.Current.FailoverPrimaryPort },
                { TestConfig.Current.FailoverReplicaServer, TestConfig.Current.FailoverReplicaPort },
            }
        };
    }

    [Fact]
    public async Task ConfigureAsync()
    {
        using var conn = Create();

        await Task.Delay(1000).ForAwait();
        Log("About to reconfigure.....");
        await conn.ConfigureAsync().ForAwait();
        Log("Reconfigured");
    }

    [Fact]
    public async Task ConfigureSync()
    {
        using var conn = Create();

        await Task.Delay(1000).ForAwait();
        Log("About to reconfigure.....");
        conn.Configure();
        Log("Reconfigured");
    }

    [Fact]
    public async Task ConfigVerifyReceiveConfigChangeBroadcast()
    {
        _ = GetConfiguration();
        using var senderConn = Create(allowAdmin: true);
        using var receiverConn = Create(syncTimeout: 2000);

        int total = 0;
        receiverConn.ConfigurationChangedBroadcast += (s, a) =>
        {
            Log("Config changed: " + (a.EndPoint == null ? "(none)" : a.EndPoint.ToString()));
            Interlocked.Increment(ref total);
        };
        // send a reconfigure/reconnect message
        long count = senderConn.PublishReconfigure();
        GetServer(receiverConn).Ping();
        GetServer(receiverConn).Ping();
        await Task.Delay(1000).ConfigureAwait(false);
        Assert.True(count == -1 || count >= 2, "subscribers");
        Assert.True(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (1st)");

        Interlocked.Exchange(ref total, 0);

        // and send a second time via a re-primary operation
        var server = GetServer(senderConn);
        if (server.IsReplica) Skip.Inconclusive("didn't expect a replica");
        await server.MakePrimaryAsync(ReplicationChangeOptions.Broadcast);
        await Task.Delay(1000).ConfigureAwait(false);
        GetServer(receiverConn).Ping();
        GetServer(receiverConn).Ping();
        Assert.True(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (2nd)");
    }

    [Fact]
    public async Task DereplicateGoesToPrimary()
    {
        ConfigurationOptions config = GetPrimaryReplicaConfig();
        config.ConfigCheckSeconds = 5;

        using var conn = ConnectionMultiplexer.Connect(config);

        var primary = conn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort);
        var secondary = conn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort);

        primary.Ping();
        secondary.Ping();

        await primary.MakePrimaryAsync(ReplicationChangeOptions.SetTiebreaker);
        await secondary.MakePrimaryAsync(ReplicationChangeOptions.None);

        await Task.Delay(100).ConfigureAwait(false);

        primary.Ping();
        secondary.Ping();

        using (var writer = new StringWriter())
        {
            conn.Configure(writer);
            string log = writer.ToString();
            Log(log);
            bool isUnanimous = log.Contains("tie-break is unanimous at " + TestConfig.Current.FailoverPrimaryServerAndPort);
            if (!isUnanimous) Skip.Inconclusive("this is timing sensitive; unable to verify this time");
        }
        // k, so we know everyone loves 6379; is that what we get?

        var db = conn.GetDatabase();
        RedisKey key = Me();

        Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferMaster));
        Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandMaster));
        Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferReplica));

        var ex = Assert.Throws<RedisConnectionException>(() => db.IdentifyEndpoint(key, CommandFlags.DemandReplica));
        Assert.StartsWith("No connection is active/available to service this operation: EXISTS " + Me(), ex.Message);
        Log("Invoking MakePrimaryAsync()...");
        await primary.MakePrimaryAsync(ReplicationChangeOptions.Broadcast | ReplicationChangeOptions.ReplicateToOtherEndpoints | ReplicationChangeOptions.SetTiebreaker, Writer);
        Log("Finished MakePrimaryAsync() call.");

        await Task.Delay(100).ConfigureAwait(false);

        Log("Invoking Ping() (post-primary)");
        primary.Ping();
        secondary.Ping();
        Log("Finished Ping() (post-primary)");

        Assert.True(primary.IsConnected, $"{primary.EndPoint} is not connected.");
        Assert.True(secondary.IsConnected, $"{secondary.EndPoint} is not connected.");

        Log($"{primary.EndPoint}: {primary.ServerType}, Mode: {(primary.IsReplica ? "Replica" : "Primary")}");
        Log($"{secondary.EndPoint}: {secondary.ServerType}, Mode: {(secondary.IsReplica ? "Replica" : "Primary")}");

        // Create a separate multiplexer with a valid view of the world to distinguish between failures of
        // server topology changes from failures to recognize those changes
        Log("Connecting to secondary validation connection.");
        using (var conn2 = ConnectionMultiplexer.Connect(config))
        {
            var primary2 = conn2.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort);
            var secondary2 = conn2.GetServer(TestConfig.Current.FailoverReplicaServerAndPort);

            Log($"Check: {primary2.EndPoint}: {primary2.ServerType}, Mode: {(primary2.IsReplica ? "Replica" : "Primary")}");
            Log($"Check: {secondary2.EndPoint}: {secondary2.ServerType}, Mode: {(secondary2.IsReplica ? "Replica" : "Primary")}");

            Assert.False(primary2.IsReplica, $"{primary2.EndPoint} should be a primary (verification connection).");
            Assert.True(secondary2.IsReplica, $"{secondary2.EndPoint} should be a replica (verification connection).");

            var db2 = conn2.GetDatabase();

            Assert.Equal(primary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.PreferMaster));
            Assert.Equal(primary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.DemandMaster));
            Assert.Equal(secondary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.PreferReplica));
            Assert.Equal(secondary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.DemandReplica));
        }

        await UntilConditionAsync(TimeSpan.FromSeconds(20), () => !primary.IsReplica && secondary.IsReplica);

        Assert.False(primary.IsReplica, $"{primary.EndPoint} should be a primary.");
        Assert.True(secondary.IsReplica, $"{secondary.EndPoint} should be a replica.");

        Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferMaster));
        Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandMaster));
        Assert.Equal(secondary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferReplica));
        Assert.Equal(secondary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandReplica));
    }

#if DEBUG
    [Fact]
    public async Task SubscriptionsSurviveConnectionFailureAsync()
    {
        using var conn = Create(allowAdmin: true, shared: false, log: Writer, syncTimeout: 1000);

        var profiler = conn.AddProfiler();
        RedisChannel channel = RedisChannel.Literal(Me());
        var sub = conn.GetSubscriber();
        int counter = 0;
        Assert.True(sub.IsConnected());
        await sub.SubscribeAsync(channel, delegate
        {
            Interlocked.Increment(ref counter);
        }).ConfigureAwait(false);

        var profile1 = Log(profiler);

        Assert.Equal(1, conn.GetSubscriptionsCount());

        await Task.Delay(200).ConfigureAwait(false);

        await sub.PublishAsync(channel, "abc").ConfigureAwait(false);
        sub.Ping();
        await Task.Delay(200).ConfigureAwait(false);

        var counter1 = Thread.VolatileRead(ref counter);
        Log($"Expecting 1 message, got {counter1}");
        Assert.Equal(1, counter1);

        var server = GetServer(conn);
        var socketCount = server.GetCounters().Subscription.SocketCount;
        Log($"Expecting 1 socket, got {socketCount}");
        Assert.Equal(1, socketCount);

        // We might fail both connections or just the primary in the time period
        SetExpectedAmbientFailureCount(-1);

        // Make sure we fail all the way
        conn.AllowConnect = false;
        Log("Failing connection");
        // Fail all connections
        server.SimulateConnectionFailure(SimulatedFailureType.All);
        // Trigger failure (RedisTimeoutException or RedisConnectionException because
        // of backlog behavior)
        var ex = Assert.ThrowsAny<Exception>(() => sub.Ping());
        Assert.True(ex is RedisTimeoutException or RedisConnectionException);
        Assert.False(sub.IsConnected(channel));

        // Now reconnect...
        conn.AllowConnect = true;
        Log("Waiting on reconnect");
        // Wait until we're reconnected
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => sub.IsConnected(channel));
        Log("Reconnected");
        // Ensure we're reconnected
        Assert.True(sub.IsConnected(channel));

        // Ensure we've sent the subscribe command after reconnecting
        var profile2 = Log(profiler);
        //Assert.Equal(1, profile2.Count(p => p.Command == nameof(RedisCommand.SUBSCRIBE)));

        Log("Issuing ping after reconnected");
        sub.Ping();

        var muxerSubCount = conn.GetSubscriptionsCount();
        Log($"Muxer thinks we have {muxerSubCount} subscriber(s).");
        Assert.Equal(1, muxerSubCount);

        var muxerSubs = conn.GetSubscriptions();
        foreach (var pair in muxerSubs)
        {
            var muxerSub = pair.Value;
            Log($"  Muxer Sub: {pair.Key}: (EndPoint: {muxerSub.GetCurrentServer()}, Connected: {muxerSub.IsConnected})");
        }

        Log("Publishing");
        var published = await sub.PublishAsync(channel, "abc").ConfigureAwait(false);

        Log($"Published to {published} subscriber(s).");
        Assert.Equal(1, published);

        // Give it a few seconds to get our messages
        Log("Waiting for 2 messages");
        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => Thread.VolatileRead(ref counter) == 2);

        var counter2 = Thread.VolatileRead(ref counter);
        Log($"Expecting 2 messages, got {counter2}");
        Assert.Equal(2, counter2);

        // Log all commands at the end
        Log("All commands since connecting:");
        var profile3 = profiler.FinishProfiling();
        foreach (var command in profile3)
        {
            Log($"{command.EndPoint}: {command}");
        }
    }

    [Fact]
    public async Task SubscriptionsSurvivePrimarySwitchAsync()
    {
        static void TopologyFail() => Skip.Inconclusive("Replication topology change failed...and that's both inconsistent and not what we're testing.");

        if (RunningInCI)
        {
            Skip.Inconclusive("TODO: Fix race in broadcast reconfig a zero latency.");
        }

        using var aConn = Create(allowAdmin: true, shared: false);
        using var bConn = Create(allowAdmin: true, shared: false);

        RedisChannel channel = RedisChannel.Literal(Me());
        Log("Using Channel: " + channel);
        var subA = aConn.GetSubscriber();
        var subB = bConn.GetSubscriber();

        long primaryChanged = 0, aCount = 0, bCount = 0;
        aConn.ConfigurationChangedBroadcast += (s, args) => Log("A noticed config broadcast: " + Interlocked.Increment(ref primaryChanged) + " (Endpoint:" + args.EndPoint + ")");
        bConn.ConfigurationChangedBroadcast += (s, args) => Log("B noticed config broadcast: " + Interlocked.Increment(ref primaryChanged) + " (Endpoint:" + args.EndPoint + ")");
        subA.Subscribe(channel, (_, message) =>
        {
            Log("A got message: " + message);
            Interlocked.Increment(ref aCount);
        });
        subB.Subscribe(channel, (_, message) =>
        {
            Log("B got message: " + message);
            Interlocked.Increment(ref bCount);
        });

        Assert.False(aConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica, $"A Connection: {TestConfig.Current.FailoverPrimaryServerAndPort} should be a primary");
        if (!aConn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica)
        {
            TopologyFail();
        }
        Assert.True(aConn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica, $"A Connection: {TestConfig.Current.FailoverReplicaServerAndPort} should be a replica");
        Assert.False(bConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica, $"B Connection: {TestConfig.Current.FailoverPrimaryServerAndPort} should be a primary");
        Assert.True(bConn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica, $"B Connection: {TestConfig.Current.FailoverReplicaServerAndPort} should be a replica");

        Log("Failover 1 Complete");
        var epA = subA.SubscribedEndpoint(channel);
        var epB = subB.SubscribedEndpoint(channel);
        Log("  A: " + EndPointCollection.ToString(epA));
        Log("  B: " + EndPointCollection.ToString(epB));
        subA.Publish(channel, "A1");
        subB.Publish(channel, "B1");
        Log("  SubA ping: " + subA.Ping());
        Log("  SubB ping: " + subB.Ping());
        // If redis is under load due to this suite, it may take a moment to send across.
        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => Interlocked.Read(ref aCount) == 2 && Interlocked.Read(ref bCount) == 2).ForAwait();

        Assert.Equal(2, Interlocked.Read(ref aCount));
        Assert.Equal(2, Interlocked.Read(ref bCount));
        Assert.Equal(0, Interlocked.Read(ref primaryChanged));

        try
        {
            Interlocked.Exchange(ref primaryChanged, 0);
            Interlocked.Exchange(ref aCount, 0);
            Interlocked.Exchange(ref bCount, 0);
            Log("Changing primary...");
            using (var sw = new StringWriter())
            {
                await aConn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).MakePrimaryAsync(ReplicationChangeOptions.All, sw);
                Log(sw.ToString());
            }
            Log("Waiting for connection B to detect...");
            await UntilConditionAsync(TimeSpan.FromSeconds(10), () => bConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica).ForAwait();
            subA.Ping();
            subB.Ping();
            Log("Failover 2 Attempted. Pausing...");
            Log("  A " + TestConfig.Current.FailoverPrimaryServerAndPort + " status: " + (aConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica ? "Replica" : "Primary"));
            Log("  A " + TestConfig.Current.FailoverReplicaServerAndPort + " status: " + (aConn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica ? "Replica" : "Primary"));
            Log("  B " + TestConfig.Current.FailoverPrimaryServerAndPort + " status: " + (bConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica ? "Replica" : "Primary"));
            Log("  B " + TestConfig.Current.FailoverReplicaServerAndPort + " status: " + (bConn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica ? "Replica" : "Primary"));

            if (!aConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica)
            {
                TopologyFail();
            }
            Log("Failover 2 Complete.");

            Assert.True(aConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica, $"A Connection: {TestConfig.Current.FailoverPrimaryServerAndPort} should be a replica");
            Assert.False(aConn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica, $"A Connection: {TestConfig.Current.FailoverReplicaServerAndPort} should be a primary");
            await UntilConditionAsync(TimeSpan.FromSeconds(10), () => bConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica).ForAwait();
            var sanityCheck = bConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).IsReplica;
            if (!sanityCheck)
            {
                Log("FAILURE: B has not detected the topology change.");
                foreach (var server in bConn.GetServerSnapshot().ToArray())
                {
                    Log("  Server: " + server.EndPoint);
                    Log("    State (Interactive): " + server.InteractiveConnectionState);
                    Log("    State (Subscription): " + server.SubscriptionConnectionState);
                    Log("    IsReplica: " + !server.IsReplica);
                    Log("    Type: " + server.ServerType);
                }
                //Skip.Inconclusive("Not enough latency.");
            }
            Assert.True(sanityCheck, $"B Connection: {TestConfig.Current.FailoverPrimaryServerAndPort} should be a replica");
            Assert.False(bConn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica, $"B Connection: {TestConfig.Current.FailoverReplicaServerAndPort} should be a primary");

            Log("Pause complete");
            Log("  A outstanding: " + aConn.GetCounters().TotalOutstanding);
            Log("  B outstanding: " + bConn.GetCounters().TotalOutstanding);
            subA.Ping();
            subB.Ping();
            await Task.Delay(5000).ForAwait();
            epA = subA.SubscribedEndpoint(channel);
            epB = subB.SubscribedEndpoint(channel);
            Log("Subscription complete");
            Log("  A: " + EndPointCollection.ToString(epA));
            Log("  B: " + EndPointCollection.ToString(epB));
            var aSentTo = subA.Publish(channel, "A2");
            var bSentTo = subB.Publish(channel, "B2");
            Log("  A2 sent to: " + aSentTo);
            Log("  B2 sent to: " + bSentTo);
            subA.Ping();
            subB.Ping();
            Log("Ping Complete. Checking...");
            await UntilConditionAsync(TimeSpan.FromSeconds(10), () => Interlocked.Read(ref aCount) == 2 && Interlocked.Read(ref bCount) == 2).ForAwait();

            Log("Counts so far:");
            Log("  aCount: " + Interlocked.Read(ref aCount));
            Log("  bCount: " + Interlocked.Read(ref bCount));
            Log("  primaryChanged: " + Interlocked.Read(ref primaryChanged));

            Assert.Equal(2, Interlocked.Read(ref aCount));
            Assert.Equal(2, Interlocked.Read(ref bCount));
            // Expect 12, because a sees a, but b sees a and b due to replication, but contenders may add their own
            Assert.True(Interlocked.CompareExchange(ref primaryChanged, 0, 0) >= 12);
        }
        catch
        {
            Log("");
            Log("ERROR: Something went bad - see above! Roooooolling back. Back it up. Baaaaaack it on up.");
            Log("");
            throw;
        }
        finally
        {
            Log("Restoring configuration...");
            try
            {
                await aConn.GetServer(TestConfig.Current.FailoverPrimaryServerAndPort).MakePrimaryAsync(ReplicationChangeOptions.All);
                await Task.Delay(1000).ForAwait();
            }
            catch { /* Don't bomb here */ }
        }
    }
#endif
}
#endif
