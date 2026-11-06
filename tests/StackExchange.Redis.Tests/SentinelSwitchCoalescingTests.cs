using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class SentinelSwitchCoalescingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BusyReconfigurationSkipsSentinelDiscovery()
    {
        var primary = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 6379);
        var sentinelEndpoint = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 26379);
        using var data = new InProcessTestServer(output, primary);
        using var sentinel = new InProcessSentinelServer(data, "mymaster", primary, sentinelEndpoint, output);
        var config = data.GetClientConfig();
        config.EndPoints.Clear();
        config.EndPoints.Add(sentinelEndpoint);
        config.ServiceName = "mymaster";
        config.Tunnel = InProcessSentinelServer.CreateTunnel(data, sentinel);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var owner = Assert.IsType<ConnectionMultiplexer>(connection.sentinelConnection);
        Assert.True(connection.TryClaimReconfigure("test in-flight switch"));
        try
        {
            int queries = sentinel.PrimaryQueries;
            for (int i = 0; i < 16; i++) Assert.False(owner.SwitchPrimary(primary, connection));
            Assert.Equal(queries, sentinel.PrimaryQueries);
        }
        finally { connection.ReleaseReconfigureClaim(); }
    }

    [Fact]
    public async Task FailedDiscoveryReleasesTheReconfigurationClaim()
    {
        var primary = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 6379);
        var sentinelEndpoint = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 26379);
        using var data = new InProcessTestServer(output, primary);
        using var sentinel = new InProcessSentinelServer(data, "mymaster", primary, sentinelEndpoint, output);
        var config = data.GetClientConfig();
        config.EndPoints.Clear();
        config.EndPoints.Add(sentinelEndpoint);
        config.ServiceName = "mymaster";
        config.Tunnel = InProcessSentinelServer.CreateTunnel(data, sentinel);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var owner = Assert.IsType<ConnectionMultiplexer>(connection.sentinelConnection);
        connection.RawConfig.ServiceName = "missing";
        Assert.Throws<RedisConnectionException>(() => owner.SwitchPrimary(primary, connection));
        Assert.True(connection.TryClaimReconfigure("test released claim"));
        connection.ReleaseReconfigureClaim();
    }
}
