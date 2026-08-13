using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Coverage for a node having more than one identity - the enabler for the endpoint-identity work
/// (#2826), and a prerequisite for reacting to endpoints we did not choose the form of.
/// </summary>
public class EndpointIdentityUnitTests(ITestOutputHelper log)
{
    private const string Hostname = "host-1.redis.example.com";

    private static DnsEndPoint AliasFor(EndPoint endpoint, string host = Hostname)
    {
        Server.RedisServer.GetHost(endpoint, out var port);
        return new DnsEndPoint(host, port);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NodeIsReachableByAlias(bool useSsl)
    {
        using var server = new InProcessTestServer(log, useSsl: useSsl);
        var canonical = server.DefaultEndPoint; // an IPEndPoint
        var alias = AliasFor(canonical);
        server.AddAlias(alias, canonical);

        // dial the *alias*; before the alias map this fell through to a real socket connect
        var config = server.GetClientConfig(defaultOnly: true);
        config.EndPoints.Clear();
        config.EndPoints.Add(alias);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var db = conn.GetDatabase();
        await db.StringSetAsync(nameof(NodeIsReachableByAlias), "abc");

        Assert.Equal("abc", await db.StringGetAsync(nameof(NodeIsReachableByAlias)));
        Assert.Equal(alias, Assert.Single(conn.GetEndPoints()));
    }

    [Fact]
    public async Task AliasAndCanonicalEndpointSeeTheSameData()
    {
        using var server = new InProcessTestServer(log);
        var canonical = server.DefaultEndPoint;
        var alias = AliasFor(canonical);
        server.AddAlias(alias, canonical);

        var viaCanonical = server.GetClientConfig(defaultOnly: true);
        var viaAlias = server.GetClientConfig(defaultOnly: true);
        viaAlias.EndPoints.Clear();
        viaAlias.EndPoints.Add(alias);

        await using var byIp = await ConnectionMultiplexer.ConnectAsync(viaCanonical);
        await using var byName = await ConnectionMultiplexer.ConnectAsync(viaAlias);

        await byIp.GetDatabase().StringSetAsync(nameof(AliasAndCanonicalEndpointSeeTheSameData), "xyz");

        // one node, two names: the value written via one identity is visible via the other
        Assert.Equal("xyz", await byName.GetDatabase().StringGetAsync(nameof(AliasAndCanonicalEndpointSeeTheSameData)));
    }

    [Fact]
    public void SetHostnameRegistersTheNameAsAnAlias()
    {
        using var server = new InProcessTestServer(log);
        var canonical = server.DefaultEndPoint;
        server.SetHostname(canonical, Hostname);

        Assert.True(server.TryGetNode(AliasFor(canonical), out var byName));
        Assert.True(server.TryGetNode(canonical, out var byAddress));
        Assert.Same(byAddress, byName);

        // GetEndPoints stays one-per-node; the alias is reported separately
        Assert.Equal(canonical, Assert.Single(server.GetEndPoints()));
        Assert.Equal(AliasFor(canonical), Assert.Single(server.GetAliases()));
    }

    [Fact]
    public async Task ClusterNodesAnnouncesHostname()
    {
        // hostnames need a 7.0+ server, which the in-process server declares by default
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        var canonical = server.DefaultEndPoint;
        server.SetHostname(canonical, Hostname);

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var raw = await conn.GetServer(canonical).ClusterNodesRawAsync();
        Assert.NotNull(raw);
        log.WriteLine(raw);

        var host = Server.RedisServer.GetHost(canonical, out var port);

        // <id> <ip:port@cport,hostname> ...
        Assert.Contains($"{host}:{port}@{port + 10000},{Hostname} ", raw);
    }

    [Fact]
    public async Task ClusterNodesOmitsHostnameWhenNoneAnnounced()
    {
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        var canonical = server.DefaultEndPoint;

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var raw = await conn.GetServer(canonical).ClusterNodesRawAsync();
        Assert.NotNull(raw);
        log.WriteLine(raw);

        var host = Server.RedisServer.GetHost(canonical, out var port);
        Assert.Contains($"{host}:{port}@{port + 10000} ", raw);

        // no trailing hostname on the endpoint token (the flags field has commas of its own)
        Assert.DoesNotContain($"@{port + 10000},", raw);
    }

#if !NETFRAMEWORK
    [Fact]
    public async Task TlsNameMismatchIsNotForgiven()
    {
        // the in-process certificate covers every identity the node can be dialled by, so a mismatch
        // has to be forced; this asserts the validation callback no longer waves one through on
        // thumbprint alone, which is what would let the SslHost handling regress unnoticed
        using var server = new InProcessTestServer(log, useSsl: true);
        var config = server.GetClientConfig(defaultOnly: true);
        config.SslHost = "not-in-the-certificate.example.com";
        config.ConnectTimeout = 2000;
        config.ConnectRetry = 1;

        var ex = await Assert.ThrowsAnyAsync<RedisConnectionException>(
            async () => await ConnectionMultiplexer.ConnectAsync(config));
        log.WriteLine(ex.Message);
    }
#endif
}
