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
