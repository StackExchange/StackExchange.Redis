using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// After a failover the slot map can be right while the primary/replica role of the two nodes is stale, and a
/// write routed to a node still believed to be a replica is refused client-side - so no <c>-MOVED</c> comes back
/// and nothing prompts a refresh, leaving writes failing until the next scheduled role check. See #3254.
/// </summary>
[RunPerProtocol]
public class ClusterFailoverRolesUnitTests(ITestOutputHelper log)
{
    private static (InProcessTestServer Server, EndPoint Primary, EndPoint Replica) Create(ITestOutputHelper log)
    {
        var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        GetHost(server.DefaultEndPoint, out var port);
        var replica = server.AddReplicaNode(new IPEndPoint(IPAddress.Loopback, port + 1), server.DefaultEndPoint);
        return (server, server.DefaultEndPoint, replica);
    }

    [Fact]
    public async Task MovedToAPromotedReplicaIsFollowed()
    {
        var (server, primary, replica) = Create(log);
        using (server)
        {
            await using var conn = await server.ConnectAsync(withPubSub: false);
            var db = conn.GetDatabase();
            Assert.True(conn.GetServer(replica).IsReplica);
            await db.StringSetAsync("before", "1");

            server.Failover(replica);

            // the old primary answers -MOVED naming a node we still believe is a replica; that redirect is
            // evidence it is not, and the resend must not be refused as a write to a replica
            await db.StringSetAsync("after", "2");
            Assert.Equal("2", await db.StringGetAsync("after"));
            Assert.False(conn.GetServer(replica).IsReplica);
        }
    }

    [Fact]
    public async Task TopologyRefreshDoesNotRequireAllowAdmin()
    {
        var (server, primary, replica) = Create(log);
        using (server)
        {
            // every other test here inherits AllowAdmin=true from the harness, which is exactly how the
            // gap hid: the refresh re-reads CLUSTER SLOTS through the ordinary endpoint executor, and the
            // AllowAdmin gate there refused the library's own housekeeping for any ordinarily-configured
            // client - so after a failover, no map repair, ever (the #3323 investigation)
            var config = server.GetClientConfig(withPubSub: false);
            config.AllowAdmin = false;
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            Assert.False(conn.GetServer(primary).IsReplica);
            Assert.True(conn.GetServer(replica).IsReplica);

            server.Failover(replica);
            await conn.ReconfigureAsync("test");

            Assert.True(conn.GetServer(primary).IsReplica);
            Assert.False(conn.GetServer(replica).IsReplica);
            await conn.GetDatabase().StringSetAsync("after", "2");
        }
    }

    [Fact]
    public async Task RolesLearnedBeforeTheMapDoNotStrandWrites()
    {
        var (server, primary, replica) = Create(log);
        using (server)
        {
            // AllowAdmin=false, as an ordinary client: with it true (the harness default) the topology
            // refresh always repaired the map before the deadlock could be seen, which is how both halves
            // of this hid from every other test in this class
            var config = server.GetClientConfig(withPubSub: false);
            config.AllowAdmin = false;
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            var db = conn.GetDatabase();
            await db.StringSetAsync("before", "1");

            server.Failover(replica);

            // the role arrives before the map catches up - as a reconnect handshake delivers it - so the
            // stale map routes writes at a known replica and refuses them before any byte goes out: no
            // -MOVED is coming to heal the map, and the refusal itself must prompt the refresh instead
            // (the #3323 investigation: writes to a failed-over shard stayed refused until a restart)
            conn.ConnectionsIfCreated!.OnRole(primary, isReplica: true);
            Assert.True(conn.GetServer(primary).IsReplica);

            Assert.True(await Poll.UntilAsync(() =>
            {
                try
                {
                    db.StringSet("after", "2");
                    return true;
                }
                catch (RedisCommandException)
                {
                    return false;
                }
            }));
            Assert.Equal("2", await db.StringGetAsync("after"));
        }
    }

    /// <summary>An <see cref="InProcessTestServer"/> whose endpoints can individually refuse new connections.</summary>
    private sealed class BlackholeableServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        public volatile EndPoint? Dark;

        protected override ValueTask OnAcceptClientAsync(EndPoint endpoint)
            => Equals(endpoint, Dark)
                ? throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)
                : default;
    }

    [Fact]
    public async Task LosingAnEstablishedConnectionPromptsATopologyRefresh()
    {
        var server = new BlackholeableServer(log) { ServerType = ServerType.Cluster };
        GetHost(server.DefaultEndPoint, out var port);
        var primary = server.DefaultEndPoint;
        var replica = server.AddReplicaNode(new IPEndPoint(IPAddress.Loopback, port + 1), primary);
        using (server)
        {
            var config = server.GetClientConfig(withPubSub: false);
            config.AllowAdmin = false;
            var capture = new CapturingLogger();
            config.LoggerFactory = capture;
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            var db = conn.GetDatabase();
            await db.StringSetAsync("before", "1");
            var slot = ServerSelectionStrategy.GetHashSlot("before");
            Assert.Equal(primary, conn.ConnectionsIfCreated!.SlotOwnerForTest(slot));

            // let the connect-time reconfigure passes drain first: ReconfigureIfNeeded coalesces while one
            // is in flight, and this test is about the TRIGGER, not the coalescing
            await Poll.ReconfigureAsync(conn, "settle");

            // the node is promoted away from AND goes dark, as an Azure-style failover looks from a QUIET
            // client: no command in flight to be redirected, no write to be refused - the only news is the
            // dropped connection. The reconnect cannot succeed (so no restore-time reconfigure), the
            // repeated-connect-failure re-read is tens of seconds away, and the periodic check up to
            // configCheckSeconds. Losing an established connection must itself prompt the re-read, which
            // is the v3 bridge's reconfigureNextFailure contract.
            server.Failover(replica);
            server.Dark = primary;
            Assert.True(conn.ConnectionsIfCreated!.SimulateConnectionFailure(primary, SimulatedFailureType.AllInteractive));

            var healed = await Poll.UntilAsync(() =>
                Equals(conn.ConnectionsIfCreated!.SlotOwnerForTest(slot), replica));
            if (!healed) log.WriteLine("CAPTURED:\n" + capture.All);
            Assert.True(healed);
            await db.StringSetAsync("after", "2");
            Assert.Equal("2", await db.StringGetAsync("after"));
        }
    }

    [Fact]
    public async Task TopologyRefreshRepairsRolesAfterAFailover()
    {
        var (server, primary, replica) = Create(log);
        using (server)
        {
            await using var conn = await server.ConnectAsync(withPubSub: false);
            Assert.False(conn.GetServer(primary).IsReplica);
            Assert.True(conn.GetServer(replica).IsReplica);

            server.Failover(replica);
            await conn.ReconfigureAsync("test");

            // previously the slot map followed but the flags did not, until the periodic INFO replication check
            Assert.True(conn.GetServer(primary).IsReplica);
            Assert.False(conn.GetServer(replica).IsReplica);

            await conn.GetDatabase().StringSetAsync("after", "2");
        }
    }
}
