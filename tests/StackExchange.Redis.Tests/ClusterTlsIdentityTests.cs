using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// TLS across a cluster whose nodes are named differently from their addresses. The certificate is
/// deliberately *narrow* here: by default the in-process certificate covers every alias, which is convenient
/// but means a test cannot catch the client dialling or presenting the wrong name - everything validates. With
/// a narrow certificate, dialling the wrong form fails the way a real deployment would.
/// </summary>
public class ClusterTlsIdentityTests(ITestOutputHelper log)
{
    private const string Hostname = "host-1.redis.example.com";

    private static InProcessTestServer CreateServer(ITestOutputHelper log, ClusterEndpointType preferred)
        => new(log, useSsl: true) { ServerType = ServerType.Cluster, PreferredEndpointType = preferred };

    [Fact]
    public async Task HostnamePreferredClusterValidatesAgainstAHostnameOnlyCertificate()
    {
#if NETFRAMEWORK
        log.WriteLine("TLS is not exercised in-process on .NET Framework");
        Assert.Skip("TLS is not exercised in-process on .NET Framework");
#else
        // the deployment this work targets: nodes advertise hostnames, and the certificate names only those.
        // If discovery dialled the address instead, validation would fail - which is the regression this test
        // exists to catch
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        server.SetHostname(server.DefaultEndPoint, Hostname);
        GetHost(server.DefaultEndPoint, out var port);
        server.CertificateNames = [new DnsEndPoint(Hostname, port)];

        var config = server.GetClientConfig(defaultOnly: true);
        config.EndPoints.Clear();
        config.EndPoints.Add(new DnsEndPoint(Hostname, port));

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var db = conn.GetDatabase();
        await db.StringSetAsync("tls-identity", "ok");
        Assert.Equal("ok", await db.StringGetAsync("tls-identity"));

        foreach (var ep in conn.GetEndPoints())
        {
            log.WriteLine($"endpoint: {ep}");
        }
#endif
    }

    [Fact]
    public async Task DiallingTheAddressFailsAgainstAHostnameOnlyCertificate()
    {
#if NETFRAMEWORK
        Assert.Skip("TLS is not exercised in-process on .NET Framework");
#else
        // the negative half, and the reason the test above means anything: with a hostname-only certificate,
        // reaching the same node by address must fail validation. If this passes, the harness is not capable of
        // detecting a name regression and the positive test above is worthless
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        server.SetHostname(server.DefaultEndPoint, Hostname);
        GetHost(server.DefaultEndPoint, out var port);
        server.CertificateNames = [new DnsEndPoint(Hostname, port)];

        var config = server.GetClientConfig(defaultOnly: true);
        config.ConnectTimeout = 2000;
        config.ConnectRetry = 1;

        var ex = await Assert.ThrowsAnyAsync<RedisConnectionException>(
            async () => await ConnectionMultiplexer.ConnectAsync(config));
        log.WriteLine(ex.Message);

        // and specifically for the *right* reason: a name mismatch, not a timeout or a refused socket, or
        // this test would still pass if the harness stopped being able to detect one
        Assert.Contains("Authentication", ex.ToString(), StringComparison.OrdinalIgnoreCase);
#endif
    }

    [Fact]
    public async Task SslHostOverridesTheDialledFormForValidation()
    {
#if NETFRAMEWORK
        Assert.Skip("TLS is not exercised in-process on .NET Framework");
#else
        // how a real single-certificate deployment works today, and the precedence that must not change: the
        // address is what gets dialled, while SslHost is what gets validated
        using var server = CreateServer(log, ClusterEndpointType.Ip);
        server.SetHostname(server.DefaultEndPoint, Hostname);
        GetHost(server.DefaultEndPoint, out var port);
        server.CertificateNames = [new DnsEndPoint(Hostname, port)];

        var config = server.GetClientConfig(defaultOnly: true);
        config.SslHost = Hostname; // dial the address, present and validate the name

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        Assert.Equal("ok", await Roundtrip(conn));

        // ...and the endpoint really is the address, so this is separation rather than coincidence
        Assert.IsType<IPEndPoint>(Assert.Single(conn.GetEndPoints()));

        static async Task<string?> Roundtrip(IConnectionMultiplexer conn)
        {
            var db = conn.GetDatabase();
            await db.StringSetAsync("tls-sslhost", "ok");
            return await db.StringGetAsync("tls-sslhost");
        }
#endif
    }
}
