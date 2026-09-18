using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Does the client work against a real deployment at all: connect, negotiate RESP3, opt in, run a command.
/// </summary>
/// <remarks>
/// The floor for everything else. Worth having as its own class because when a scenario test fails, the first
/// question is whether the deployment was reachable in the first place, and a green smoke test answers it
/// without reading logs.
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "smoke")]
public class RealDeploymentSmokeTests(ExistingDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ExistingDatabaseFixture>
{
    [Theory]
    [InlineData("standalone")]
    [InlineData("cluster")]
    public async Task OptInIsAcceptedAndTheConnectionWorks(string key)
    {
        var database = fixture.Require(key);
        log.WriteLine($"{database}");
        log.WriteLine($"  advertised addresses: {string.Join(", ", database.Addresses)}");
        log.WriteLine($"  endpoint type: {database.EndpointType ?? "(unset)"}");

        // Enabled refuses the connection if the server will not give us notifications, so reaching the
        // assertions at all is the opt-in having been accepted - there is nothing weaker to check. A stub +OK
        // would pass this, which is what the scenario tests are for.
        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig(fixture.Environment));
        Assert.True(conn.IsConnected);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        log.WriteLine($"  connected: {server.Version}, {server.ServerType}, protocol {server.Protocol}");
        Assert.Equal(RedisProtocol.Resp3, server.Protocol);

        var rtt = await conn.GetDatabase().PingAsync();
        log.WriteLine($"  ping: {rtt.TotalMilliseconds:0.0}ms");
        Assert.True(rtt > TimeSpan.Zero);

        // and the feature is actually live on this connection, not merely requested
        var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(conn.GetEndPoints()[0]);
        Assert.True(endpoint.MaintenanceNotificationsActive, "maintenance notifications should be active");
    }

    [Fact]
    public async Task AdvertisedAddressCountMatchesTheProxyPolicy()
    {
        // Not a client assertion: a check that the environment is the shape the handoff tests assume. The count
        // follows proxy *placement* rather than the policy name, so this records what this deployment actually
        // is - and a handoff test that expects a sibling to step to needs more than one.
        var standalone = fixture.Require("standalone");
        log.WriteLine($"{standalone.Key}: policy {standalone.ProxyPolicy}, {standalone.AdvertisedAddressCount} address(es)");

        Assert.NotEmpty(standalone.Addresses);
        if (standalone.ProxyPolicy is "single")
        {
            Assert.Equal(1, standalone.AdvertisedAddressCount);
        }
    }
}
