using System.Linq;
using System.Threading.Tasks;
using RESPite.StackExchange.Redis;
using Xunit;

namespace RESPite.Tests;

public class RespMultiplexerTests(ITestOutputHelper log)
{
    private readonly LogWriter logWriter = new(log);

    [Fact]
    public async Task CanConnect()
    {
        await using var muxer = new RespMultiplexer();
        await muxer.ConnectAsync(log: logWriter);
        Assert.True(muxer.IsConnected);

        var server = muxer.GetServer(muxer.GetEndPoints().Single());
        Assert.IsType<NodeServer>(server); // we expect this to *not* use routing
        server.Ping();
        await server.PingAsync();
    }
}
