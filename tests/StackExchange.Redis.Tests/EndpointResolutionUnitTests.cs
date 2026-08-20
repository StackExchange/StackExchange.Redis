using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Reaching one node by any of the names it answers to. <c>servers</c> is keyed on exact endpoint equality,
/// so before this an address-keyed node was simply unreachable by its announced hostname - and a redirect
/// naming it that way became a second <see cref="ServerEndPoint"/> for the same node (#2826).
/// </summary>
public class EndpointResolutionUnitTests(ITestOutputHelper log)
{
    private const string Hostname = "host-1.redis.example.com";

    private static InProcessTestServer CreateServer(ITestOutputHelper log, ClusterEndpointType preferred = ClusterEndpointType.Ip)
    {
        var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster, PreferredEndpointType = preferred };
        server.SetHostname(server.DefaultEndPoint, Hostname);
        return server;
    }

    [Theory]
    [InlineData(ClusterEndpointType.Ip)]
    [InlineData(ClusterEndpointType.Hostname)]
    public async Task ServerResolvesByEitherIdentity(ClusterEndpointType preferred)
    {
        using var server = CreateServer(log, preferred);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var canonical = server.DefaultEndPoint;
        GetHost(canonical, out var port);
        var byName = new DnsEndPoint(Hostname, port);

        // the multiplexer is keyed on the address it was configured with...
        Assert.Equal(canonical, Assert.Single(conn.GetEndPoints()));

        // ...but the node also answers to its announced hostname, and that now resolves to the same server
        var viaAddress = conn.GetServer(canonical);
        var viaName = conn.GetServer(byName);
        Assert.Equal(viaAddress.EndPoint, viaName.EndPoint);

        // and resolving does not invent an endpoint
        Assert.Equal(canonical, Assert.Single(conn.GetEndPoints()));
    }

    /// <summary>
    /// The invariant behind all of this: one node, one endpoint. Asserted from the shadow topology rather than
    /// from a scenario, so it holds regardless of the order things were learned in - which is what makes it a
    /// useful guard while two sources can still create servers (autoconfigure, and the independent
    /// CLUSTER NODES read in ReconfigureAsync).
    /// </summary>
    internal static void AssertOneEndpointPerNode(IConnectionMultiplexer conn, ITestOutputHelper log)
    {
        var mux = (IInternalConnectionMultiplexer)conn;
        var seen = new Dictionary<string, EndPoint>();
        foreach (var endpoint in conn.GetEndPoints())
        {
            var topology = mux.GetServerEndPoint(endpoint).ClusterTopology;
            if (topology is null) continue;

            foreach (var node in topology.Nodes)
            {
                // does this endpoint identify this node?
                if (!node.Identities.Contains(endpoint)) continue;

                if (seen.TryGetValue(node.NodeId, out var already) && !Equals(already, endpoint))
                {
                    Assert.Fail($"node {node.NodeId} is held under two endpoints: {already} and {endpoint}");
                }
                seen[node.NodeId] = endpoint;
                log.WriteLine($"{node.NodeId} <- {endpoint}");
            }
        }
    }

    [Fact]
    public async Task UnknownIdentityStillThrows()
    {
        using var server = CreateServer(log);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var ex = Assert.Throws<ArgumentException>(() => conn.GetServer(new DnsEndPoint("not-this-node.example.com", 6379)));
        log.WriteLine(ex.Message);
        Assert.Contains("not defined", ex.Message);
    }

    [Fact]
    public async Task RedirectToANewNodeDoesNotDuplicateIt()
    {
        // The hazard in full: a hostname-preferring cluster redirects to a node, which then autoconfigures and
        // reports itself by *address* via CLUSTER NODES. Left to itself that produces two ServerEndPoints for
        // one node - doubled connections, with backlog and subscription state split across the pair.
        //
        // What prevents it is that autoconfigure asks for CLUSTER SLOTS before CLUSTER NODES: replies arrive
        // in request order, so the identities are registered before NODES can create anything by address.
        // That ordering is load-bearing - see the comment in ServerEndPoint.AutoConfigureAsync - and this is
        // the test that fails if someone reorders the burst.
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        GetHost(server.DefaultEndPoint, out var port);

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var before = conn.GetEndPoints().Length;

        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.SetHostname(other, "host-2.redis.example.com");
        server.Migrate((RedisKey)"resolution-key", other);

        // follow the redirect; the target is named by hostname, since that is what this cluster prefers
        await conn.GetDatabase().StringSetAsync("resolution-key", "value");
        Assert.Equal("value", await conn.GetDatabase().StringGetAsync("resolution-key"));

        foreach (var ep in conn.GetEndPoints())
        {
            log.WriteLine($"endpoint: {ep}");
        }

        AssertOneEndpointPerNode(conn, log);

        // one new node, one endpoint for it
        var added = conn.GetEndPoints().Length - before;
        log.WriteLine($"added {added} endpoint(s) for one node");
        Assert.Equal(1, added);
        Assert.Equal(1, conn.GetEndPoints().Count(ep => PortOf(ep) == port + 1));

        // both names reach a server, which is the part resolution does deliver
        Assert.NotNull(conn.GetServer(new IPEndPoint(IPAddress.Loopback, port + 1)));
        Assert.NotNull(conn.GetServer(new DnsEndPoint("host-2.redis.example.com", port + 1)));

        static int PortOf(EndPoint ep) => ep switch
        {
            IPEndPoint ip => ip.Port,
            DnsEndPoint dns => dns.Port,
            _ => 0,
        };
    }

    [Fact]
    public async Task ResolutionSurvivesReconnect()
    {
        // identities are learned from topology, so they must be re-registered when it is re-read
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        GetHost(server.DefaultEndPoint, out var port);

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        Assert.NotNull(conn.GetServer(new DnsEndPoint(Hostname, port)));

        await conn.GetServer(server.DefaultEndPoint).PingAsync();
        Assert.NotNull(conn.GetServer(new DnsEndPoint(Hostname, port)));
    }
}
