using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The parsed <c>CLUSTER SLOTS</c> view, exercised against the toy server across the naming configurations a
/// real server can be in - including the placeholder endpoint values, which are the classic client bug.
/// </summary>
public class ClusterSlotsUnitTests(ITestOutputHelper log)
{
    private const string Hostname = "host-1.redis.example.com";
    private static readonly System.Version BeforeHostnames = new(6, 2, 0);

    private static InProcessTestServer CreateServer(
        ITestOutputHelper log,
        ClusterEndpointType preferred = ClusterEndpointType.Ip,
        bool announceHostname = true,
        AnnouncedAddress announced = AnnouncedAddress.Address,
        System.Version? version = null)
    {
        var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster, PreferredEndpointType = preferred };
        if (version is not null) server.RedisVersion = version;
        if (announceHostname) server.SetHostname(server.DefaultEndPoint, Hostname);
        if (announced != AnnouncedAddress.Address) server.SetAnnouncedAddress(server.DefaultEndPoint, announced);
        return server;
    }

    private static async Task<ClusterSlotNode> GetPrimaryAsync(InProcessTestServer server)
    {
        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var result = await conn.GetServer(server.DefaultEndPoint).ClusterSlotsAsync();
        Assert.NotNull(result);
        return Assert.Single(result.Assignments).Primary;
    }

    [Fact]
    public void NodeIdsAreUniqueEvenWhenCreatedInTheSameTick()
    {
        // regression: the toy server created a Random per id, and .NET Framework seeds that from the tick
        // count - so nodes created in the same tick shared an id. Keyed reconciliation then merged two nodes
        // into one, which surfaced as the *client* looking wrong
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        var ids = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        GetHost(server.DefaultEndPoint, out var port);
        Assert.True(server.TryGetNode(server.DefaultEndPoint, out var first));
        Assert.True(ids.Add(first.Id));

        for (int i = 1; i <= 25; i++)
        {
            var endpoint = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + i));
            Assert.True(server.TryGetNode(endpoint, out var node));
            Assert.True(ids.Add(node.Id), $"duplicate id at node {i}: {node.Id}");
        }
    }

    [Fact]
    public async Task WholeKeyspaceIsReportedWithNodeIdAndEndpoint()
    {
        using var server = CreateServer(log, announceHostname: false);
        var host = GetHost(server.DefaultEndPoint, out var port);

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var result = await conn.GetServer(server.DefaultEndPoint).ClusterSlotsAsync();
        Assert.NotNull(result);

        var assignment = Assert.Single(result.Assignments);
        Assert.Equal(0, assignment.Slots.From);
        Assert.Equal(16383, assignment.Slots.To);
        Assert.Empty(assignment.Replicas);

        var primary = assignment.Primary;
        Assert.Equal(host, primary.AnnouncedEndpoint);
        Assert.Equal(port, primary.Port);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, port), primary.EndPoint);
        Assert.False(string.IsNullOrEmpty(primary.NodeId));
        Assert.Empty(primary.Metadata);
    }

    [Fact]
    public async Task IpPreferredSurfacesHostnameFromMetadata()
    {
        using var server = CreateServer(log, ClusterEndpointType.Ip);
        var host = GetHost(server.DefaultEndPoint, out var port);

        var primary = await GetPrimaryAsync(server);

        Assert.Equal(host, primary.AnnouncedEndpoint);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, port), primary.EndPoint);
        Assert.Equal(Hostname, primary.Hostname);
        Assert.Null(primary.Ip); // the complement rule: it is already the primary field
    }

    [Fact]
    public async Task HostnamePreferredParsesAsADnsEndPointAndSurfacesIp()
    {
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        var host = GetHost(server.DefaultEndPoint, out var port);

        var primary = await GetPrimaryAsync(server);

        Assert.Equal(Hostname, primary.AnnouncedEndpoint);
        Assert.Equal(new DnsEndPoint(Hostname, port), primary.EndPoint);
        Assert.Equal(host, primary.Ip);
        Assert.Null(primary.Hostname);
    }

    [Fact]
    public async Task UnannouncedHostnameYieldsNoEndpointButKeepsTheIp()
    {
        using var server = CreateServer(log, ClusterEndpointType.Hostname, announceHostname: false);
        var host = GetHost(server.DefaultEndPoint, out _);

        var primary = await GetPrimaryAsync(server);

        // "?" is an explicitly unknown node, so it must not become an endpoint...
        Assert.Equal("?", primary.AnnouncedEndpoint);
        Assert.Null(primary.EndPoint);

        // ...but the reply still carries the address, so the union is not poorer than CLUSTER NODES
        Assert.Equal(host, primary.Ip);
    }

    [Fact]
    public async Task NullEndpointYieldsNoEndpoint()
    {
        using var server = CreateServer(log, ClusterEndpointType.UnknownEndpoint);
        var host = GetHost(server.DefaultEndPoint, out var port);

        var primary = await GetPrimaryAsync(server);

        // null means "connect to where you sent this, with this port" - a caller decision, not ours
        Assert.Null(primary.AnnouncedEndpoint);
        Assert.Null(primary.EndPoint);
        Assert.Equal(port, primary.Port);
        Assert.Equal(host, primary.Ip);
        Assert.Equal(Hostname, primary.Hostname);
    }

    [Fact]
    public async Task EmptyEndpointYieldsNoEndpoint()
    {
        using var server = CreateServer(log, ClusterEndpointType.Ip, announced: AnnouncedAddress.Empty);

        var primary = await GetPrimaryAsync(server);

        Assert.Equal("", primary.AnnouncedEndpoint);
        Assert.Null(primary.EndPoint);
    }

    [Fact]
    public async Task RecognizedKeysAreSurfacedAndUnknownOnesPreserved()
    {
        // known keys are matched over the raw bytes and surfaced as properties, so they cost no allocation
        // and do not appear in Metadata; the extensible remainder is kept as declared
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        server.SetSlotsMetadata(server.DefaultEndPoint, "not-invented-yet", "42");
        var host = GetHost(server.DefaultEndPoint, out _);

        var primary = await GetPrimaryAsync(server);

        Assert.Equal(host, primary.Ip); // recognized...
        Assert.Equal(new("not-invented-yet", "42"), Assert.Single(primary.Metadata)); // ...and the rest kept
    }

    [Fact]
    public async Task MetadataKeysAreMatchedWithoutRegardToCase()
    {
        // the contract renders these keys inconsistently between prose and examples, so casing cannot be
        // relied on; an upper-case key must still be recognized rather than landing in Metadata
        using var server = CreateServer(log, ClusterEndpointType.Ip, announceHostname: false);
        server.SetSlotsMetadata(server.DefaultEndPoint, "HOSTNAME", "shouty.redis.example.com");

        var primary = await GetPrimaryAsync(server);

        Assert.Equal("shouty.redis.example.com", primary.Hostname);
        Assert.Empty(primary.Metadata);
    }

    [Fact]
    public async Task PreSevenZeroServerReportsNoMetadata()
    {
        using var server = CreateServer(log, ClusterEndpointType.Hostname, version: BeforeHostnames);
        var host = GetHost(server.DefaultEndPoint, out var port);

        var primary = await GetPrimaryAsync(server);

        // three-element node block: endpoint, port, id - and the preference is inert below 7.0
        Assert.Equal(host, primary.AnnouncedEndpoint);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, port), primary.EndPoint);
        Assert.Empty(primary.Metadata);
        Assert.Null(primary.Hostname);
        Assert.False(string.IsNullOrEmpty(primary.NodeId));
    }

    [Fact]
    public async Task MigratedSlotsAreReportedAsSeparateAssignments()
    {
        using var server = CreateServer(log, announceHostname: false);
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"slots-key", other);

        await using var conn = await server.ConnectAsync();
        var result = await conn.GetServer(server.DefaultEndPoint).ClusterSlotsAsync();
        Assert.NotNull(result);

        log.WriteLine(string.Join(", ", result.Assignments.Select(x => $"{x.Slots}=>{x.Primary}")));

        // the migrated slot splits the original range, and every assignment names its own primary
        Assert.True(result.Assignments.Count > 1);
        Assert.All(result.Assignments, x => Assert.NotNull(x.Primary));
        Assert.Contains(result.Assignments, x => x.Primary.Port == port + 1);

        var migrated = Assert.Single(result.Assignments, x => x.Primary.Port == port + 1);
        Assert.Equal(migrated.Slots.From, migrated.Slots.To); // exactly the one slot moved
    }

    [Fact]
    public async Task NodeIdIsStableAcrossTheNamingForms()
    {
        // node-id is the one identity that does not depend on how the answering node renders endpoints,
        // which is what makes it the reliable reconciliation key
        using var server = CreateServer(log, ClusterEndpointType.Ip);
        var byAddress = await GetPrimaryAsync(server);

        server.PreferredEndpointType = ClusterEndpointType.Hostname;
        var byName = await GetPrimaryAsync(server);

        Assert.NotEqual(byAddress.AnnouncedEndpoint, byName.AnnouncedEndpoint);
        Assert.Equal(byAddress.NodeId, byName.NodeId);
    }
}
