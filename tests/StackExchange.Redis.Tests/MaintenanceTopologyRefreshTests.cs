using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Server;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Non-parallel deliberately. Several of these wait out a jittered refresh, and the retirement one depends on
/// the doomed server being *idle* - and <c>IsIdle</c> counts outstanding work, which a heartbeat ping in flight
/// supplies. On a loaded machine those complete slowly enough that pruning is starved, which is a real property
/// of the policy rather than a flaw in the test: see the design notes on why a usage-based grace rule was
/// dropped for exactly this reason.
/// <para>
/// Reacting to a completed slot migration by re-reading the topology, rather than waiting to be told by a
/// <c>-MOVED</c>. Asserted from the server's side - did another <c>CLUSTER</c> command actually arrive - because
/// that is the thing the fleet pays for, and the thing a scoping bug would multiply.
/// </para>
/// </summary>
[Collection(NonParallelCollection.Name)]
public class MaintenanceTopologyRefreshTests(ITestOutputHelper log)
{
    /// <summary>
    /// Counts inbound <c>CLUSTER</c> commands, so a test can tell a refresh happened from the outside.
    /// </summary>
    private sealed class CountingServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        private int _clusterCommands, _subscribeCommands;

        public int ClusterCommands => Volatile.Read(ref _clusterCommands);

        public int SubscribeCommands => Volatile.Read(ref _subscribeCommands);

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            if (request.Count > 0)
            {
                var command = request.GetString(0);
                if (string.Equals(command, "cluster", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _clusterCommands);
                }
                else if (string.Equals(command, "ssubscribe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(command, "subscribe", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _subscribeCommands);
                }
            }

            return base.Execute(client, in request);
        }
    }

    private static async Task<(CountingServer Server, ConnectionMultiplexer Connection)> ConnectAsync(
        ITestOutputHelper log,
        Action<CountingServer>? configure = null,
        MaintenanceNotificationMode mode = MaintenanceNotificationMode.Enabled)
    {
        var server = new CountingServer(log) { ServerType = ServerType.Cluster };
        configure?.Invoke(server);
        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.MaintenanceNotifications = mode;

        var conn = await ConnectionMultiplexer.ConnectAsync(config);
        return (server, conn);
    }

    /// <summary>
    /// The jitter is up to a second, so a refresh is not immediate by design.
    /// </summary>
    private static Task<bool> UntilRefreshedAsync(CountingServer server, int before)
        => Poll.UntilAsync(() => server.ClusterCommands > before, timeoutMilliseconds: 5000);

    [Fact]
    public async Task SlotsMovingAwayFromUsTriggersARefresh()
    {
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            GetHost(server.DefaultEndPoint, out var port);
            var before = server.ClusterCommands;

            server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
            [
                ($"127.0.0.1:{port}", $"127.0.0.1:{port + 1}", "0-99"),
            ]);

            Assert.True(await UntilRefreshedAsync(server, before), "a refresh should follow slots leaving us");
            log.WriteLine($"cluster commands: {before} -> {server.ClusterCommands}");
        }
    }

    [Fact]
    public async Task SlotsMovingBetweenOtherNodesDoesNotTriggerARefresh()
    {
        // the whole herd argument: every node reports the same movements, so most notifications are about
        // somebody else. Refreshing on those means every client in the fleet re-reads topology whenever any
        // shard moves anywhere
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var before = server.ClusterCommands;

            server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
            [
                ("127.0.0.1:7100", "127.0.0.1:7101", "0-99"),
                ("127.0.0.1:7102", "127.0.0.1:7103", "100-199"),
            ]);

            await Task.Delay(2000); // longer than the jitter, so "not yet" cannot be mistaken for "never"
            log.WriteLine($"cluster commands: {before} -> {server.ClusterCommands}");
            Assert.Equal(before, server.ClusterCommands);
        }
    }

    [Fact]
    public async Task SourceIsMatchedByAnnouncedIdentityNotJustAddress()
    {
        // A node answers to its address *and* its announced hostname, and the delta may name either, so the
        // scoping test resolves through the identity map rather than comparing endpoints as text. Announced
        // before connecting, because the handshake's own topology read is what registers the identity - a
        // hostname set afterwards is not known to us until something re-reads it.
        const string Hostname = "node-1.redis.example.com";
        var (server, conn) = await ConnectAsync(log, s => s.SetHostname(s.DefaultEndPoint, Hostname));
        using (server)
        await using (conn)
        {
            GetHost(server.DefaultEndPoint, out var port);
            var before = server.ClusterCommands;

            server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
            [
                ($"{Hostname}:{port}", $"127.0.0.1:{port + 1}", "0-99"),
            ]);

            Assert.True(await UntilRefreshedAsync(server, before), "the hostname form should resolve to us");
        }
    }

    [Fact]
    public async Task ProxyStyleCompletionDoesNotTriggerARefresh()
    {
        // MIGRATED/FAILED_OVER arrive in proxied deployments addressed as a single endpoint, where there is no
        // topology for a refresh to learn - so they get relaxation and nothing else
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var before = server.ClusterCommands;

            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 5);
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrated, timeSeconds: 0);

            await Task.Delay(2000);
            log.WriteLine($"cluster commands: {before} -> {server.ClusterCommands}");
            Assert.Equal(before, server.ClusterCommands);
        }
    }

    [Fact]
    public async Task ShardedSubscriptionOnAMovedSlotIsReEstablished()
    {
        // mostly belt-and-braces: the server also sends an unsolicited SUNSUBSCRIBE for this, which we already
        // act on. What this adds is being pre-emptive when SMIGRATED lands first, and covering the case where
        // the unsubscribe never arrives - where the only other symptom is messages silently stopping
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            GetHost(server.DefaultEndPoint, out var port);
            var sub = conn.GetSubscriber();
            var channel = RedisChannel.Sharded("resub-channel");
            await sub.SubscribeAsync(channel, (_, _) => { });

            var slot = ((ConnectionMultiplexer)conn).ServerSelectionStrategy.HashSlot(channel);
            log.WriteLine($"channel hashes to slot {slot}");

            var before = server.SubscribeCommands;
            server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
            [
                ($"127.0.0.1:{port}", $"127.0.0.1:{port + 1}", $"{slot}"),
            ]);

            Assert.True(
                await Poll.UntilAsync(() => server.SubscribeCommands > before),
                "the sharded subscription should have been re-established");
        }
    }

    [Fact]
    public async Task ShardedSubscriptionOnAnUnaffectedSlotIsLeftAlone()
    {
        // knowing *which* slots moved is the advantage over the unsolicited-unsubscribe path: only the
        // affected channels are touched, rather than everything subscribed on this server
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            GetHost(server.DefaultEndPoint, out var port);
            var sub = conn.GetSubscriber();
            var channel = RedisChannel.Sharded("untouched-channel");
            await sub.SubscribeAsync(channel, (_, _) => { });

            var slot = ((ConnectionMultiplexer)conn).ServerSelectionStrategy.HashSlot(channel);
            var otherSlot = slot == 0 ? 1 : 0;
            var before = server.SubscribeCommands;

            server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
            [
                ($"127.0.0.1:{port}", $"127.0.0.1:{port + 1}", $"{otherSlot}"),
            ]);

            await Task.Delay(2000);
            log.WriteLine($"subscribe commands: {before} -> {server.SubscribeCommands} (slot {slot} vs moved {otherSlot})");
            Assert.Equal(before, server.SubscribeCommands);
        }
    }

    [Fact]
    public async Task OrdinaryPubSubIsNotSlotBoundAndIsLeftAlone()
    {
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            GetHost(server.DefaultEndPoint, out var port);
            var sub = conn.GetSubscriber();
            await sub.SubscribeAsync(RedisChannel.Literal("plain-channel"), (_, _) => { });

            var before = server.SubscribeCommands;
            server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
            [
                ($"127.0.0.1:{port}", $"127.0.0.1:{port + 1}", "0-16383"), // everything moves
            ]);

            await Task.Delay(2000);
            log.WriteLine($"subscribe commands: {before} -> {server.SubscribeCommands}");
            Assert.Equal(before, server.SubscribeCommands);
        }
    }

    [Theory]
    [InlineData(MaintenanceNotificationMode.Disabled)] // only the unsolicited SUNSUBSCRIBE path can act
    [InlineData(MaintenanceNotificationMode.Enabled)] // both paths see the same migration
    public async Task RealMigrationRecoversTheSubscriptionWithoutStorming(MaintenanceNotificationMode mode)
    {
        // Until the fake announced its own migrations this could not be tested at all, and it is the case that
        // matters: a real slot migration produces *both* an unsolicited SUNSUBSCRIBE (ordinary cluster
        // behaviour, sent to every subscriber) and SMIGRATED (only to clients that opted in). Both paths lead
        // to ResubscribeToServer, so the question is whether the subscription ends up established exactly
        // once rather than twice or not at all.
        var (server, conn) = await ConnectAsync(log, mode: mode);
        using (server)
        await using (conn)
        {
            GetHost(server.DefaultEndPoint, out var port);
            var doomed = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));

            var sub = conn.GetSubscriber();
            var channel = RedisChannel.Sharded("migrating-channel");
            var received = 0;
            await sub.SubscribeAsync(channel, (_, _) => Interlocked.Increment(ref received));

            var slot = ((ConnectionMultiplexer)conn).ServerSelectionStrategy.HashSlot(channel);
            var before = server.SubscribeCommands;

            server.NotifyOnMigrate = true;
            server.Migrate(slot, doomed); // the whole realistic sequence, from one call

            Assert.True(await Poll.UntilAsync(() => server.SubscribeCommands > before), "should have resubscribed");
            await Task.Delay(2000); // and settle, so a second resubscribe would show up

            var resubscribes = server.SubscribeCommands - before;
            log.WriteLine($"{mode}: {resubscribes} (re)subscribe command(s) after a real migration");

            // The property is boundedness, not an absolute count: the unsolicited-unsubscribe path has its own
            // pre-existing retry-and-redirect behaviour. Measured at 4 with notifications off and 5 with them
            // on - the extra one being the fallback acting on a subscription that path left attached to
            // nothing, which is the feature working. What must not happen is one attempt per notification.
            // Bounded, not exact: the unsolicited-unsubscribe path retries and follows redirects, and how
            // many of those land depends on timing (measured 4 unloaded, 7 on a constrained runner). The
            // property is that the count does not scale with notifications - one migration, a handful of
            // attempts - so the bound is deliberately loose rather than pinned to a number that will drift.
            Assert.InRange(resubscribes, 1, 15);

            // Delivery is asserted by publishing *repeatedly*, because pub/sub is fire and forget: a message
            // published while the subscription is still in flux is simply dropped. Losing messages during the
            // tremor is expected; never delivering again is not, and one publish cannot tell those apart.
            var delivered = await Poll.UntilAsync(
                () =>
                {
                    if (Volatile.Read(ref received) > 0) return true;
                    conn.GetSubscriber().Publish(channel, "hello");
                    return Volatile.Read(ref received) > 0;
                },
                timeoutMilliseconds: 10_000,
                pollMilliseconds: 250);

            log.WriteLine($"{mode}: delivered after migration = {delivered}");
            Assert.True(delivered, "the subscription should deliver again once things settle");
        }
    }

    [Fact]
    public async Task NodeThatLeavesTheClusterIsRetired()
    {
        // This is also the regression test for idleness counting *caller* work only. Before that, the
        // reconfigure's own probes to the departed node piled into its backlog (~170 per pass, unbounded), so
        // it looked busy because we were looking for it, and could never be retired.

        // The narrowed form of D5's "retire endpoints serving no slots". Serving nothing is *not* the
        // condition: a node still listed in CLUSTER NODES is a live member that may be given slots again, and
        // dropping its connection would be churn (go-redis does not). Having *left* the cluster is the
        // condition, and the notification-driven refresh is what makes us notice.
        EndPoint doomed = null!;
        var (server, conn) = await ConnectAsync(log, configure: s =>
        {
            GetHost(s.DefaultEndPoint, out var p);
            doomed = s.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, p + 1));
            s.Migrate((RedisKey)"leaving", doomed); // owns something, so it is discovered at connect
        });

        using (server)
        await using (conn)
        {
            Assert.True(
                await Poll.UntilAsync(() => conn.GetEndPoints().Contains(doomed), timeoutMilliseconds: 10_000),
                $"{doomed} was never discovered, so this test would prove nothing");

            GetHost(server.DefaultEndPoint, out var basePort);
            var third = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, basePort + 2));

            // hand its slot back and then remove it from the cluster outright
            server.NotifyOnMigrate = true;
            server.Migrate((RedisKey)"leaving", server.DefaultEndPoint);
            Assert.True(server.RemoveNode(doomed), "the node should have been removed from the fake");

            // Pruning wants several consecutive generations of absence. Driving those by *awaiting* topology
            // passes rather than by sending notifications and sleeping: the notification path is covered by
            // the tests above, and depending on it here would make this test wait out a jittered refresh per
            // generation - which is what made it fail under a two-core runner. What is being tested is the
            // retirement, so drive the generations deterministically.
            // Retirement also requires the server to be *idle*, so a busy moment can defer it past a given
            // pass - which is why this drives passes until it happens rather than asserting after a fixed
            // count. Bounded, so a regression fails rather than hangs.
            GC.KeepAlive(third);
            var mux = (ConnectionMultiplexer)conn;
            for (int i = 0; i < 20 && conn.GetEndPoints().Contains(doomed); i++)
            {
                await mux.ReconfigureAsync(first: false, reconfigureAll: true, log: null, blame: null, cause: $"test-generation-{i}");
                await Task.Delay(50); // retirement drains before removing, so give it a moment to complete

            }

            log.WriteLine($"endpoints: {string.Join(", ", conn.GetEndPoints().Select(x => x.ToString()))}");
            Assert.DoesNotContain(doomed, conn.GetEndPoints());
        }
    }

    [Fact]
    public async Task RefreshIsCoalescedRatherThanRepeated()
    {
        // A burst of notifications must not be a burst of topology reads. Note ReconfigureIfNeeded's own
        // coalescing is *not* what achieves this: it declines only while a refresh is actually in flight, and
        // the jitter spreads a burst out far enough that each pass finishes before the next starts. This test
        // is what caught that, and why the coalescing happens before the delay rather than after.
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            GetHost(server.DefaultEndPoint, out var port);
            var before = server.ClusterCommands;

            for (int i = 0; i < 10; i++)
            {
                server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
                [
                    ($"127.0.0.1:{port}", $"127.0.0.1:{port + 1}", $"{i * 10}-{(i * 10) + 9}"),
                ]);
            }

            Assert.True(await UntilRefreshedAsync(server, before));
            await Task.Delay(2000); // let any stragglers land

            // a refresh reads both SLOTS and NODES, so allow a small multiple - the point is that ten
            // notifications did not produce ten topology passes
            var added = server.ClusterCommands - before;
            log.WriteLine($"{added} cluster command(s) for 10 notifications");
            Assert.InRange(added, 1, 8);
        }
    }
}
