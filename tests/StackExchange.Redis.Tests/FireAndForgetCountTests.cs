using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>The multiplexer's status reports how many commands were issued fire-and-forget, as v3's did.</summary>
public class FireAndForgetCountTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task FireAndForgetCommandsAreCounted()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        var db = conn.GetDatabase();
        await db.PingAsync();
        Assert.Contains("; fire and forget: 0;", conn.GetStatus());

        db.StringSet("ff:sync", "a", flags: CommandFlags.FireAndForget);
        await db.StringSetAsync("ff:async", "b", flags: CommandFlags.FireAndForget);
        await db.StringSetAsync("ff:awaited", "c"); // not fire-and-forget, so not counted

        Assert.Contains("; fire and forget: 2;", conn.GetStatus());
    }
}
