using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Server;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Reacting to a completed slot migration by re-reading the topology, rather than waiting to be told by a
/// <c>-MOVED</c>. Asserted from the server's side - did another <c>CLUSTER</c> command actually arrive - because
/// that is the thing the fleet pays for, and the thing a scoping bug would multiply.
/// </summary>
public class MaintenanceTopologyRefreshTests(ITestOutputHelper log)
{
    /// <summary>
    /// Counts inbound <c>CLUSTER</c> commands, so a test can tell a refresh happened from the outside.
    /// </summary>
    private sealed class CountingServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        private int _clusterCommands;

        public int ClusterCommands => Volatile.Read(ref _clusterCommands);

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            if (request.Count > 0 && string.Equals(request.GetString(0), "cluster", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _clusterCommands);
            }

            return base.Execute(client, in request);
        }
    }

    private static async Task<(CountingServer Server, ConnectionMultiplexer Connection)> ConnectAsync(ITestOutputHelper log, Action<CountingServer>? configure = null)
    {
        var server = new CountingServer(log) { ServerType = ServerType.Cluster };
        configure?.Invoke(server);
        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.MaintenanceNotifications = MaintenanceNotificationMode.Enabled;

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
