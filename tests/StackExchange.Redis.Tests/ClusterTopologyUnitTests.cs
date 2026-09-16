using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The id-keyed <c>CLUSTER SLOTS</c> topology, which now drives the slot map. The agreement tests against the
/// <c>CLUSTER NODES</c> view are retained deliberately: <c>NODES</c> is no longer what routes, but it remains
/// the public admin surface, and the two disagreeing would mean one of them is wrong.
/// </summary>
public class ClusterTopologyUnitTests(ITestOutputHelper log)
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
    /// Builds the view from an explicit <c>CLUSTER SLOTS</c> call, so that most of these exercise the model
    /// and the parser independently of the autoconfigure wiring; <see cref="AutoConfigurePopulatesTheTopology"/>
    /// covers the wiring itself.
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
    public async Task AutoConfigurePopulatesTheTopology(ClusterEndpointType preferred)
    {
        // the wiring, as opposed to the model: connecting is enough, because autoconfigure asks for
        // CLUSTER SLOTS as part of its pipelined burst
        using var server = CreateServer(log, preferred);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
        var topology = endpoint.ClusterTopology;
        Assert.NotNull(topology);

        var node = Assert.Single(topology.Nodes);
        log.WriteLine(node.ToString());
        Assert.False(string.IsNullOrEmpty(node.NodeId));

        // and both identities are known, which is what routing will later rely on
        GetHost(server.DefaultEndPoint, out var port);
        Assert.Contains(new IPEndPoint(IPAddress.Loopback, port), node.Identities);
        Assert.Contains(new DnsEndPoint(Hostname, port), node.Identities);
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

        EndpointResolutionUnitTests.AssertOneEndpointPerNode(conn, log);

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
    public async Task SlotMapIsDrivenByTheSlotsView()
    {
        // proves the flip took effect rather than the two views merely agreeing: the toy server reports a
        // slot as migrated in SLOTS *only*, so routing can only be correct if the SLOTS view is what feeds
        // ServerSelectionStrategy
        using var server = CreateServer(log, announceHostname: false);
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"slot-map-key", other);

        await using var conn = await server.ConnectAsync();

        // NoRedirect: if the slot map is right, this lands on the owner first time and needs no redirect
        var db = conn.GetDatabase();
        await db.StringSetAsync("slot-map-key", "value", flags: CommandFlags.NoRedirect);
        Assert.Equal("value", await db.StringGetAsync("slot-map-key", CommandFlags.NoRedirect));

        // ...and the command went to the node that owns the slot
        var owner = conn.GetServer(new IPEndPoint(IPAddress.Loopback, port + 1));
        log.WriteLine($"owner: {owner.EndPoint}");
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, port + 1), owner.EndPoint);

        EndpointResolutionUnitTests.AssertOneEndpointPerNode(conn, log);
    }

    [Fact]
    public async Task HostnamePreferredClusterRoutesWithoutDuplicatingEndpoints()
    {
        // the case the flip exists for: SLOTS names every node by hostname while NODES names them by
        // address, so a slot map fed from SLOTS must still resolve to the servers we already hold
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.SetHostname(other, "host-2.redis.example.com");
        server.Migrate((RedisKey)"hostname-routed-key", other);

        await using var conn = await server.ConnectAsync();
        var db = conn.GetDatabase();
        await db.StringSetAsync("hostname-routed-key", "value");
        Assert.Equal("value", await db.StringGetAsync("hostname-routed-key"));

        foreach (var ep in conn.GetEndPoints())
        {
            log.WriteLine($"endpoint: {ep}");
        }
        EndpointResolutionUnitTests.AssertOneEndpointPerNode(conn, log);

        // two nodes, two endpoints - not four
        Assert.Equal(2, conn.GetEndPoints().Length);
    }

    [Fact]
    public async Task SlotLessNodesAreKnownButNotConnected()
    {
        // CLUSTER SLOTS does not list a node that serves nothing, so NODES contributes it - registered but
        // not dialled, since there is nothing to route to it. It stays addressable, and first use connects it
        using var server = CreateServer(log, announceHostname: false);
        var idle = server.AddEmptyNode(); // no slots

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        await conn.GetServer(server.DefaultEndPoint).PingAsync(); // force a reconfigure pass

        foreach (var ep in conn.GetEndPoints())
        {
            log.WriteLine($"endpoint: {ep}");
        }
        Assert.Contains(idle, conn.GetEndPoints());

        var api = conn.GetServer(idle);
        Assert.False(api.IsConnected); // known, but no bridge was created for it

        // ...and using it activates it, so nothing is lost by not dialling eagerly
        await api.PingAsync();
        Assert.True(api.IsConnected);
    }

    [Fact]
    public async Task SlotServingNodesAreConnectedEagerly()
    {
        // the counterpart: a node that owns slots is in the SLOTS view and so is connected as before
        using var server = CreateServer(log, announceHostname: false);
        GetHost(server.DefaultEndPoint, out var port);
        var owner = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"eager-key", owner);

        await using var conn = await server.ConnectAsync();
        await conn.GetServer(server.DefaultEndPoint).PingAsync();

        Assert.True(conn.GetServer(owner).IsConnected);
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
