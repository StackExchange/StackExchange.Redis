using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>The circular op-count snapshot in each endpoint's profile, sampled on the heartbeat as v3 sampled it.</summary>
public class EndpointProfileTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task TheProfileRecordsOperationsAcrossHeartbeats()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        var db = conn.GetDatabase();
        await db.PingAsync();
        conn.OnHeartbeat();

        for (var i = 0; i < 20; i++) await db.StringSetAsync($"profile:{i}", i);
        conn.OnHeartbeat();

        var profile = conn.GetServerEndPoint(conn.GetEndPoints()[0], ServerProvenance.Configured).GetProfile();
        Log(profile);
        Assert.Contains("Circular op-count snapshot; int: ", profile);
        Assert.DoesNotContain("int: n/a", profile);
        Assert.Contains("ops/s; spans 10s)", profile);
        Assert.Contains("+", profile); // the count moved between samples
    }
}
