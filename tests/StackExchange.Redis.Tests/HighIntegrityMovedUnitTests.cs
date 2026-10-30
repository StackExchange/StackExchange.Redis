using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class HighIntegrityMovedUnitTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HighIntegritySurvivesMovedResponse(bool highIntegrity)
    {
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        var secondary = server.AddEmptyNode();

        var config = server.GetClientConfig();
        config.HighIntegrity = highIntegrity;
        using var client = await ConnectionMultiplexer.ConnectAsync(config);

        RedisKey a = "a", b = "b"; // known to be in different slots
        Assert.NotEqual(ServerSelectionStrategy.GetHashSlot(a), ServerSelectionStrategy.GetHashSlot(b));

        var db = client.GetDatabase();
        var x = db.StringIncrementAsync(a);
        var y = db.StringIncrementAsync(b);
        await x;
        await y;
        Assert.Equal(1, await db.StringGetAsync(a));
        Assert.Equal(1, await db.StringGetAsync(b));

        // now force a -MOVED response
        server.Migrate(a, secondary);
        x = db.StringIncrementAsync(a);
        y = db.StringIncrementAsync(b);
        await x;
        await y;
        Assert.Equal(2, await db.StringGetAsync(a));
        Assert.Equal(2, await db.StringGetAsync(b));

        // and the mode was actually in force: every interactive connection checked its replies, and the node
        // the redirect went to has validated checks of its own - a MOVED must not leave either side out of step
        long checks = 0;
        foreach (var endpoint in client.GetEndPoints())
        {
            var connection = client.Connections.InteractiveEndpoint(endpoint).ConnectionForDiagnostics;
            if (connection is null) continue;
            Assert.False(connection.IsClosed);
            var passed = connection.SequenceChecksPassed;
            if (highIntegrity) Assert.True(passed > 0, $"no checks validated on {endpoint}");
            else Assert.Equal(0, passed);
            checks += passed;
        }

        if (highIntegrity) Assert.True(checks >= 8, $"expected a check per command, saw {checks}");
        log.WriteLine($"sequence checks validated: {checks}");
    }
}
