using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    [Flags]
    public enum HandshakeFlags
    {
        None = 0,
        Authenticated = 1 << 0,
        TieBreaker = 1 << 1,
        ConfigChannel = 1 << 2,
        UsePubSub = 1 << 3,
        UseDatabase = 1 << 4,
    }

    private static readonly int HandshakeFlagsCount = Enum.GetValues(typeof(HandshakeFlags)).Length - 1;
    public static IEnumerable<object[]> GetHandshakeParameters()
    {
        // all server-response modes; all flag permutations
        foreach (ServerResponse response in Enum.GetValues(typeof(ServerResponse)))
        {
            int count = 1 << HandshakeFlagsCount;
            for (int i = 0; i < count; i++)
            {
                yield return [response, (HandshakeFlags)i];
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetHandshakeParameters))]
    public async Task Handshake(ServerResponse response, HandshakeFlags flags)
    {
        using var server = new HandshakeServer(response, log);
        server.Password = (flags & HandshakeFlags.Authenticated) == 0 ? null : "mypassword";
        var config = server.GetClientConfig();
        config.TieBreaker = (flags & HandshakeFlags.TieBreaker) == 0 ? "" : "tiebreaker_key";
        config.ConfigurationChannel = (flags & HandshakeFlags.ConfigChannel) == 0 ? "" : "broadcast_channel";

        using var client = await ConnectionMultiplexer.ConnectAsync(config);

        var sub = client.GetSubscriber();
        var db = client.GetDatabase();
        ConcurrentBag<string> received = [];
        RedisChannel channel = RedisChannel.Literal("mychannel");
        RedisKey key = "mykey";
        bool useDatabase = (flags & HandshakeFlags.UseDatabase) != 0;
        bool usePubSub = (flags & HandshakeFlags.UsePubSub) != 0;

        if (usePubSub)
        {
            await sub.SubscribeAsync(channel, (x, y) => received.Add(y!));
        }
        if (useDatabase)
        {
            await db.StringSetAsync(key, "myvalue");
        }
        if (usePubSub)
        {
            await sub.PublishAsync(channel, "msg payload");
            for (int i = 0; i < 5 && received.IsEmpty; i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                await sub.PingAsync();
            }
            Assert.Equal("msg payload", Assert.Single(received));
        }

        if (useDatabase)
        {
            Assert.Equal("myvalue", await db.StringGetAsync(key));
        }
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
