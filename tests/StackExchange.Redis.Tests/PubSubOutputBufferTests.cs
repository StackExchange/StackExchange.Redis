using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The server classifies any connection with a live subscription as a pub/sub client, and applies the pub/sub
/// output-buffer limits to it (<c>client-output-buffer-limit pubsub 32mb 8mb 60</c> by default) - far tighter
/// than the (unlimited) normal limits, and well within what a busy interactive connection can legitimately need.
/// A reply bigger than the hard limit gets the connection closed, taking every in-flight command with it. See #3263.
/// </summary>
/// <remarks>
/// Hence a dedicated subscription connection by default, even under RESP3; sharing the interactive connection
/// is opt-in via <see cref="ConfigurationOptions.SharedSubscriptionConnection"/>, and these tests pin both
/// sides of that trade. The oversized reply is an <c>MGET</c> of the same small value many times over: the
/// whole reply is buffered (and checked against the limit) before any of it is written, so this needs only a
/// small value and no change to the server's configuration.
/// </remarks>
[RunPerProtocol]
public class PubSubOutputBufferTests(ITestOutputHelper output) : TestBase(output)
{
    private const int ValueSize = 64 * 1024;

    [Fact]
    public Task LargeReplyWithDedicatedSubscriptionConnection() => LargeReplyAsync(sharedSubscriptionConnection: false);

    [Fact]
    public Task LargeReplyWithSharedSubscriptionConnection() => LargeReplyAsync(sharedSubscriptionConnection: true); // fatal under RESP3

    private async Task LargeReplyAsync(bool sharedSubscriptionConnection)
    {
        await using var conn = Create(
            allowAdmin: true,
            shared: false,
            configuration: sharedSubscriptionConnection ? GetConfiguration() + ",sharedSubscriptionConnection=true" : null);
        var server = GetServer(conn);
        var endpoint = conn.GetServerEndPoint(server.EndPoint);

        var hardLimit = await GetPubSubHardLimitAsync(server);
        Assert.SkipUnless(hardLimit > 0, "Skipping because the server has no pub/sub output-buffer hard limit");
        var repeat = checked((int)(hardLimit / ValueSize)) + 16;
        Log($"pub/sub hard limit: {hardLimit} bytes; fetching {repeat} x {ValueSize} bytes");

        // a subscription of our own, so the connection carrying it is a pub/sub client whatever else is going on
        await conn.GetSubscriber().SubscribeAsync(RedisChannel.Literal(Me()), (_, _) => { });

        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.StringSetAsync(key, new byte[ValueSize]);
        var keys = Enumerable.Repeat(key, repeat).ToArray();
        try
        {
            var mux = (IInternalConnectionMultiplexer)conn;
            var interactiveId = mux.GetConnectionId(server.EndPoint, ConnectionType.Interactive);
            Assert.NotNull(interactiveId);

            if (endpoint.SharesSubscriptionConnection())
            {
                SetExpectedAmbientFailureCount(-1);
                await Assert.ThrowsAsync<RedisConnectionException>(() => db.StringGetAsync(keys));
            }
            else
            {
                var values = await db.StringGetAsync(keys);
                Assert.Equal(repeat, values.Length);
                Assert.All(values, value => Assert.Equal(ValueSize, ((byte[])value!).Length));

                // the same connection, which the server still sees as an ordinary client
                Assert.Equal(interactiveId, mux.GetConnectionId(server.EndPoint, ConnectionType.Interactive));
                var self = Assert.Single(await server.ClientListAsync(), x => x.Id == interactiveId);
                Assert.Equal(ClientType.Normal, self.ClientType);
            }
        }
        finally
        {
            await db.KeyDeleteAsync(key);
        }
    }

    /// <summary>The pub/sub hard limit in bytes, or zero if there is none (or we cannot ask).</summary>
    private static async Task<long> GetPubSubHardLimitAsync(IServer server)
    {
        KeyValuePair<string, string>[] config;
        try
        {
            config = await server.ConfigGetAsync("client-output-buffer-limit");
        }
        catch (RedisServerException)
        {
            return 0; // managed services commonly disallow CONFIG
        }

        // "normal 0 0 0 slave 268435456 67108864 60 pubsub 33554432 8388608 60"
        var tokens = config.FirstOrDefault().Value?.Split([' '], StringSplitOptions.RemoveEmptyEntries) ?? [];
        var index = Array.IndexOf(tokens, "pubsub");
        return index >= 0 && index + 1 < tokens.Length && long.TryParse(tokens[index + 1], out var hard) ? hard : 0;
    }
}
