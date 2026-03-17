using System.Collections.Concurrent;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class Resp3HandshakeTests(ITestOutputHelper log)
{
    public enum ServerResponse
    {
        SuccessResp3,
        SuccessResp2,
        UnknownCommand,
    }

    [Theory]
    [InlineData(ServerResponse.SuccessResp3)]
    [InlineData(ServerResponse.SuccessResp2)]
    [InlineData(ServerResponse.UnknownCommand)]
    public async Task Handshake(ServerResponse response)
    {
        using var server = new HandshakeServer(response, log);
        using var client = await server.ConnectAsync();

        var sub = client.GetSubscriber();
        var db = client.GetDatabase();
        ConcurrentBag<string> received = [];
        RedisChannel channel = RedisChannel.Literal("mychannel");
        RedisKey key = "mykey";
        await sub.SubscribeAsync(channel, (x, y) => received.Add(y!));
        await db.StringSetAsync(key, "myvalue");
        await sub.PublishAsync(channel, "msg payload");
        for (int i = 0; i < 5 && received.IsEmpty; i++)
        {
            await sub.PingAsync();
        }
        Assert.Equal("msg payload", Assert.Single(received));
        Assert.Equal("myvalue", await db.StringGetAsync(key));
    }

    private sealed class HandshakeServer(ServerResponse response, ITestOutputHelper log)
        : InProcessTestServer(log)
    {
        protected override RedisProtocol MaxProtocol => response switch
        {
            ServerResponse.SuccessResp3 => RedisProtocol.Resp3,
            _ => RedisProtocol.Resp2,
        };

        protected override TypedRedisValue Hello(RedisClient client, in RedisRequest request)
            => response is ServerResponse.UnknownCommand
                ? request.CommandNotFound()
                : base.Hello(client, in request);
    }
}
