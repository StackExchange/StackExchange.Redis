using System.Linq;
using System.Threading.Tasks;
using RESPite.StackExchange.Redis;
using StackExchange.Redis;
using Xunit;

namespace RESPite.Tests;

public class RespMultiplexerTests(ITestOutputHelper log)
{
    private readonly LogWriter logWriter = new(log);

    [Fact]
    public async Task CanConnect()
    {
        await using var muxer = new RespMultiplexer();
        await muxer.ConnectAsync("localhost:6379", log: logWriter);
        Assert.True(muxer.IsConnected);

        var server = muxer.GetServer(default(RedisKey));
        Assert.IsType<RespContextServer>(server); // we expect this to *not* use routing
        server.Ping();
        await server.PingAsync();

        var db = muxer.GetDatabase();
        var proxied = Assert.IsType<RespContextDatabase>(db);
        // since this is a single-node instance, we expect the proxied database to use the interactive connection
        db.Ping();
        await db.PingAsync();

        // ReSharper disable once MethodHasAsyncOverload
        proxied.Ping();
        await proxied.PingAsync();
    }
}
