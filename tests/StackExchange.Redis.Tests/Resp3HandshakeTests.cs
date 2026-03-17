using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

public class Resp3HandshakeTests(ITestOutputHelper log)
{
    public enum ServerResponse
    {
        Resp3,
        Resp2,
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
        // all client protocols, all server-response modes; all flag permutations
        var clients = (RedisProtocol[])Enum.GetValues(typeof(RedisProtocol));
        var servers = (ServerResponse[])Enum.GetValues(typeof(ServerResponse));
        foreach (var client in clients)
        {
            foreach (var server in servers)
            {
                if (client is RedisProtocol.Resp2 & server is not ServerResponse.Resp2)
                {
                    // we don't issue HELLO for this, nothing to test
                }
                else
                {
                    int count = 1 << HandshakeFlagsCount;
                    for (int i = 0; i < count; i++)
                    {
                        yield return [client, server, (HandshakeFlags)i];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetHandshakeParameters))]
    public async Task Handshake(RedisProtocol client, ServerResponse server, HandshakeFlags flags)
    {
        using var serverObj = new HandshakeServer(server, log);
        serverObj.Password = (flags & HandshakeFlags.Authenticated) == 0 ? null : "mypassword";
        var config = serverObj.GetClientConfig();
        config.Protocol = client;
        config.TieBreaker = (flags & HandshakeFlags.TieBreaker) == 0 ? "" : "tiebreaker_key";
        config.ConfigurationChannel = (flags & HandshakeFlags.ConfigChannel) == 0 ? "" : "broadcast_channel";

        using var clientObj = await ConnectionMultiplexer.ConnectAsync(config);

        var sub = clientObj.GetSubscriber();
        var db = clientObj.GetDatabase();
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

    private static readonly EndPoint EP = new DnsEndPoint("home", 8000);
    private sealed class HandshakeServer(ServerResponse response, ITestOutputHelper log)
        : InProcessTestServer(log, EP)
    {
        protected override RedisProtocol MaxProtocol => response switch
        {
            ServerResponse.Resp3 => RedisProtocol.Resp3,
            _ => RedisProtocol.Resp2,
        };

        protected override TypedRedisValue Hello(RedisClient client, in RedisRequest request)
            => response is ServerResponse.UnknownCommand
                ? request.CommandNotFound()
                : base.Hello(client, in request);
    }
}
