using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The parsed view of <c>CLUSTER NODES</c>, specifically the part after the <c>@</c> that we used to
/// truncate away: the cluster bus port, the announced hostname, and any auxiliary fields. Documented form
/// is <c>ip:port@cport[,hostname[,aux-field=value]*]</c>.
/// </summary>
public class ClusterNodeParseUnitTests(ITestOutputHelper log)
{
    private const string Hostname = "host-1.redis.example.com";
    private static readonly EndPoint Origin = new IPEndPoint(IPAddress.Loopback, 7000);

    private static ClusterNode Parse(string line)
    {
        var config = new ClusterConfiguration(serverSelectionStrategy: null!, line, Origin);
        return config.Nodes.Single();
    }

    private const string Flags = " myself,master - 0 0 1 connected 0-16383";

    [Fact]
    public void BusPortIsParsed()
    {
        var node = Parse("abc 127.0.0.1:7000@17000" + Flags);

        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 7000), node.EndPoint);
        Assert.Equal(17000, node.ClusterBusPort);
        Assert.Null(node.Hostname);
        Assert.Empty(node.AuxFields);
    }

    [Fact]
    public void PreFourZeroLineHasNoBusPort()
    {
        // servers older than 4.0 report no "@cport" at all
        var node = Parse("abc 127.0.0.1:7000" + Flags);

        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 7000), node.EndPoint);
        Assert.Null(node.ClusterBusPort);
        Assert.Null(node.Hostname);
        Assert.Empty(node.AuxFields);
    }

    [Fact]
    public void HostnameIsParsed()
    {
        var node = Parse($"abc 127.0.0.1:7000@17000,{Hostname}" + Flags);

        Assert.Equal(17000, node.ClusterBusPort);
        Assert.Equal(Hostname, node.Hostname);

        // the hostname is an additional identity, not a replacement for the address
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 7000), node.EndPoint);
    }

    [Fact]
    public void AuxFieldsAreParsed()
    {
        var node = Parse($"abc 127.0.0.1:7000@17000,{Hostname},shard-id=abc123,human-nodename=alpha" + Flags);

        Assert.Equal(Hostname, node.Hostname);
        Assert.Collection(
            node.AuxFields,
            x => Assert.Equal(new("shard-id", "abc123"), x),
            x => Assert.Equal(new("human-nodename", "alpha"), x));
    }

    [Fact]
    public void AuxFieldsSurviveAnEmptyHostnameSlot()
    {
        // the hostname slot is positional, so it can be empty while aux fields follow it
        var node = Parse("abc 127.0.0.1:7000@17000,,shard-id=abc123" + Flags);

        Assert.Null(node.Hostname);
        Assert.Equal(new("shard-id", "abc123"), Assert.Single(node.AuxFields));
    }

    [Fact]
    public void UnrecognizedAuxFieldsArePreserved()
    {
        // the set is documented as extensible, so a field we have never heard of must round-trip
        var node = Parse($"abc 127.0.0.1:7000@17000,{Hostname},not-invented-yet=42" + Flags);

        Assert.Equal(new("not-invented-yet", "42"), Assert.Single(node.AuxFields));
    }

    [Fact]
    public void MalformedTrailerDoesNotThrow()
    {
        // an exception here does silent damage to topology, so parsing is deliberately lenient
        var node = Parse($"abc 127.0.0.1:7000@not-a-port,{Hostname},no-equals-sign,=novalue,k=v" + Flags);

        Assert.Null(node.ClusterBusPort);
        Assert.Equal(Hostname, node.Hostname);
        Assert.Equal(new("k", "v"), Assert.Single(node.AuxFields));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RoundTripsThroughTheServer(bool announceHostname, bool auxFields)
    {
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        var endpoint = server.DefaultEndPoint;
        if (announceHostname) server.SetHostname(endpoint, Hostname);
        if (auxFields) server.SetAuxField(endpoint, "shard-id", "abc123");

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var config = await conn.GetServer(endpoint).ClusterNodesAsync();
        Assert.NotNull(config);

        var node = config.Nodes.Single();
        log.WriteLine(node.Raw);

        Server.RedisServer.GetHost(endpoint, out var port);
        Assert.Equal(port + 10000, node.ClusterBusPort);
        Assert.Equal(announceHostname ? Hostname : null, node.Hostname);
        if (auxFields)
        {
            Assert.Equal(new("shard-id", "abc123"), Assert.Single(node.AuxFields));
        }
        else
        {
            Assert.Empty(node.AuxFields);
        }
    }

    [Fact]
    public async Task PreSevenZeroServerReportsNoHostname()
    {
        using var server = new InProcessTestServer(log)
        {
            ServerType = ServerType.Cluster,
            RedisVersion = new System.Version(6, 2, 0),
        };
        server.SetHostname(server.DefaultEndPoint, Hostname);

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var config = await conn.GetServer(server.DefaultEndPoint).ClusterNodesAsync();
        Assert.NotNull(config);

        var node = config.Nodes.Single();
        Assert.Null(node.Hostname);
        Assert.Equal(6379 + 10000, node.ClusterBusPort); // the cport predates hostnames
    }
}
