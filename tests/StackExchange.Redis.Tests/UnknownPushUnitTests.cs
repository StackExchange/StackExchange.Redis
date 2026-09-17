using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Verifies that an unrecognized RESP3 push frame is ignored rather than being matched to a pending
/// command. Maintenance notifications (MOVING, MIGRATING, SMIGRATED, ...) arrive as push frames whose
/// second element is an integer sequence id, not a pub/sub channel, so they exercise this path.
/// </summary>
public class UnknownPushUnitTests(ITestOutputHelper log)
{
    /// <summary>
    /// Injects an unknown push frame immediately before the reply to a command, so the client sees
    /// <c>&gt;3 SMIGRATING 1 123</c> and then the real response, with the command still outstanding.
    /// </summary>
    private sealed class UnknownPushServer(ITestOutputHelper? log) : InProcessTestServer(log)
    {
        public int Injected { get; private set; }

        public Task<ConnectionMultiplexer> ConnectResp3Async()
        {
            var config = GetClientConfig();
            config.Protocol = RedisProtocol.Resp3;
            return ConnectionMultiplexer.ConnectAsync(config);
        }

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            // only inject for the command under test, and only when the client negotiated RESP3
            if (client.Protocol is RedisProtocol.Resp3 && request.Count >= 2 && request.IsString(0, "GET"u8))
            {
                var push = TypedRedisValue.Rent(3, out var span, RespPrefix.Push);
                span[0] = TypedRedisValue.SimpleString("SMIGRATING");
                span[1] = TypedRedisValue.Integer(1);
                span[2] = TypedRedisValue.SimpleString("123,456,789-1000");
                client.AddOutbound(push);
                Injected++;
                Log($"[{client}] injected unknown push before {request.Command} reply");
            }

            return base.Execute(client, in request);
        }
    }

    [Fact]
    public async Task UnknownPushIsIgnoredWhileCommandOutstanding()
    {
        using var server = new UnknownPushServer(log);
        await using var conn = await server.ConnectResp3Async();

        var db = conn.GetDatabase();
        RedisKey key = "unknown-push";
        await db.StringSetAsync(key, "abc");

        // the GET reply is preceded on the wire by an unrecognized push frame; the GET must still
        // receive its own reply
        var value = await db.StringGetAsync(key);

        Assert.Equal(1, server.Injected);
        Assert.Equal("abc", value);
    }

    [Fact]
    public async Task ConnectionSurvivesUnknownPushAndKeepsWorking()
    {
        using var server = new UnknownPushServer(log);
        await using var conn = await server.ConnectResp3Async();

        var db = conn.GetDatabase();
        RedisKey key = "unknown-push-repeat";
        await db.StringSetAsync(key, "abc");

        // repeat: if a push frame is consumed as a command reply, the response stream desynchronizes
        // and subsequent commands see the wrong replies (or the connection faults)
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("abc", await db.StringGetAsync(key));
        }

        Assert.True(conn.IsConnected);
        Assert.Equal(5, server.Injected);
    }
}
