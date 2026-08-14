using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The id-keyed <c>CLUSTER SLOTS</c> topology is currently populated *alongside* the <c>CLUSTER NODES</c>
/// view that drives routing, so that the two can be compared before anything depends on the new one. These
/// are the comparison: they assert the shadow view agrees with what routing actually uses, and that it
/// unifies identities where the old view cannot.
/// </summary>
public class ClusterTopologyShadowUnitTests(ITestOutputHelper log)
{
    private const string Hostname = "host-1.redis.example.com";

    private static InProcessTestServer CreateServer(
        ITestOutputHelper log,
        ClusterEndpointType preferred = ClusterEndpointType.Ip,
        bool announceHostname = true)
    {
        var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster, PreferredEndpointType = preferred };
        if (announceHostname) server.SetHostname(server.DefaultEndPoint, Hostname);
        return server;
    }

    /// <summary>
    /// Builds the view from an explicit <c>CLUSTER SLOTS</c> call. Autoconfigure does not ask for it yet - see
    /// the comment in <c>ServerEndPoint.AutoConfigureAsync</c> - so these exercise the model and the parser
    /// rather than the wiring; the wiring is covered where it is enabled.
    /// </summary>
    private static async Task<ClusterTopology> GetShadowAsync(IConnectionMultiplexer conn, EndPoint endpoint)
    {
        var slots = await conn.GetServer(endpoint).ClusterSlotsAsync();
        var topology = ClusterTopology.From(slots);
        Assert.NotNull(topology);
        return topology;
    }

    [Theory]
    [InlineData(ClusterEndpointType.Ip)]
    [InlineData(ClusterEndpointType.Hostname)]
    public async Task ShadowTopologyIsBuiltFromTheReply(ClusterEndpointType preferred)
    {
        using var server = CreateServer(log, preferred);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var topology = await GetShadowAsync(conn, server.DefaultEndPoint);
        var node = Assert.Single(topology.Nodes);
        log.WriteLine(node.ToString());

        GetHost(server.DefaultEndPoint, out var port);
        Assert.Equal(port, node.Port);
        Assert.False(node.IsReplica);
        Assert.False(string.IsNullOrEmpty(node.NodeId));
        Assert.Equal(0, node.Slots.Single().From);
        Assert.Equal(16383, node.Slots.Single().To);
    }

    [Theory]
    [InlineData(ClusterEndpointType.Ip)]
    [InlineData(ClusterEndpointType.Hostname)]
    public async Task ShadowTopologyKnowsBothIdentities(ClusterEndpointType preferred)
    {
        // whichever form the answering node prefers, the complement arrives as metadata - so one reply is
        // enough to know the node by both names, which is what the NODES-driven view cannot express
        using var server = CreateServer(log, preferred);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var node = Assert.Single((await GetShadowAsync(conn, server.DefaultEndPoint)).Nodes);
        var host = GetHost(server.DefaultEndPoint, out var port);

        Assert.Equal(host, node.Ip);
        Assert.Equal(Hostname, node.Hostname);
        Assert.Contains(new IPEndPoint(IPAddress.Loopback, port), node.Identities);
        Assert.Contains(new DnsEndPoint(Hostname, port), node.Identities);
    }

    [Fact]
    public async Task ShadowTopologyAgreesWithTheRoutingView()
    {
        using var server = CreateServer(log, announceHostname: false);
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"shadow-key", other);

        await using var conn = await server.ConnectAsync();
        var api = conn.GetServer(server.DefaultEndPoint);

        // force both views to be refreshed from the same server
        var nodes = await api.ClusterNodesAsync();
        Assert.NotNull(nodes);
        var topology = await GetShadowAsync(conn, server.DefaultEndPoint);

        var fromNodes = nodes.Nodes.Where(x => !x.IsReplica && x.Slots.Count > 0)
            .Select(x => x.NodeId).OrderBy(x => x).ToArray();
        var fromShadow = topology.Nodes.Where(x => !x.IsReplica)
            .Select(x => x.NodeId).OrderBy(x => x).ToArray();

        log.WriteLine($"NODES:  {string.Join(",", fromNodes)}");
        log.WriteLine($"SHADOW: {string.Join(",", fromShadow)}");
        Assert.Equal(fromNodes, fromShadow);

        // ...and the ranges agree per node, not merely in total: equal totals with different boundaries is
        // exactly what an off-by-one in range application looks like, and it is the property routing depends
        // on. Compared as sorted slot sets so that differing range *fragmentation* between the two views is
        // not treated as disagreement - only differing ownership is
        foreach (var node in topology.Nodes.Where(x => !x.IsReplica))
        {
            var expected = Slots(nodes.Nodes.Single(x => x.NodeId == node.NodeId).Slots);
            var actual = Slots(node.Slots);
            log.WriteLine($"{node.NodeId}: {actual.Length} slots");
            Assert.Equal(expected, actual);
        }

        static int[] Slots(System.Collections.Generic.IEnumerable<SlotRange> ranges)
            => ranges.SelectMany(r => Enumerable.Range(r.From, r.To - r.From + 1)).OrderBy(x => x).ToArray();
    }

    [Fact]
    public async Task ShadowTopologyDoesNotChangeRouting()
    {
        // the whole point of shadow mode: recorded, compared, not acted upon
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        Assert.NotNull(await GetShadowAsync(conn, server.DefaultEndPoint));

        // endpoints are still exactly what NODES gave us: addresses, not the preferred hostname form
        Assert.All(conn.GetEndPoints(), ep => Assert.IsType<IPEndPoint>(ep));
        await conn.GetDatabase().StringSetAsync("shadow-routing", "ok");
        Assert.Equal("ok", await conn.GetDatabase().StringGetAsync("shadow-routing"));
    }

    [Fact]
    public async Task PreFourZeroServerYieldsNoShadowTopology()
    {
        // no node ids to key on, so there is nothing we could reconcile; better absent than half-built
        using var server = new InProcessTestServer(log)
        {
            ServerType = ServerType.Cluster,
            RedisVersion = new System.Version(3, 2, 0),
        };
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var slots = await conn.GetServer(server.DefaultEndPoint).ClusterSlotsAsync();
        log.WriteLine($"topology: {ClusterTopology.From(slots)?.Nodes.Count.ToString() ?? "(none)"}");
    }
}
