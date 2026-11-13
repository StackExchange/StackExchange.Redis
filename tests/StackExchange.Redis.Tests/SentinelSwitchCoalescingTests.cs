using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class SentinelSwitchCoalescingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BusyReconfigurationStillRecordsTheReportedPrimary()
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
            var reported = new IPEndPoint(IPAddress.Parse("192.0.2.2"), 6379);
            for (int i = 0; i < 16; i++) Assert.False(owner.SwitchPrimary(primary, connection, reportedPrimary: reported));
            Assert.Equal(queries, sentinel.PrimaryQueries);
            Assert.Equal(reported, connection.currentSentinelPrimaryEndPoint);
        }
        finally { connection.ReleaseReconfigureClaim(); }
    }

    [Fact]
    public async Task DiscoveryDoesNotHoldTheReconfigurationClaim()
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
        bool sawQuery = false, claimAvailable = false;
        connection.RawConfig.ServiceName = "missing";
        sentinel.BeforePrimaryQuery = () =>
        {
            sawQuery = true;
            claimAvailable = connection.TryClaimReconfigure("test during discovery");
            if (claimAvailable) connection.ReleaseReconfigureClaim();
        };
        try { Assert.Throws<RedisConnectionException>(() => owner.SwitchPrimary(primary, connection)); }
        finally { sentinel.BeforePrimaryQuery = null; }
        Assert.True(sawQuery);
        Assert.True(claimAvailable);
    }

    [Fact]
    public async Task SlowDiscoveryCoalescesSwitchesWithoutHoldingTheReconfigurationClaim()
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
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        sentinel.BeforePrimaryQuery = () =>
        {
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Sentinel discovery barrier timed out.");
        };
        int before = sentinel.PrimaryQueries;
        var pending = Task.Run(() => Assert.Throws<RedisConnectionException>(() => owner.SwitchPrimary(primary, connection)));
        try
        {
            await entered.Task.WithTimeout(5000);
            Assert.True(connection.TryClaimReconfigure("test unrelated reconfiguration"));
            connection.ReleaseReconfigureClaim();
            for (int i = 0; i < 16; i++) Assert.False(owner.SwitchPrimary(primary, connection, reportedPrimary: primary));
            Assert.Equal(before, sentinel.PrimaryQueries);
            Assert.Equal(primary, connection.currentSentinelPrimaryEndPoint);
        }
        finally
        {
            release.Set();
            await pending.WithTimeout(5000);
            sentinel.BeforePrimaryQuery = null;
        }
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
