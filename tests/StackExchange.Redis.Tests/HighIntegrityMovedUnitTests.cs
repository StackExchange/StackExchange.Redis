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
    }
}
