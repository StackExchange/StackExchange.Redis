using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The configuration-change channel is how a client is told, by hand, that the topology moved: a <c>PUBLISH</c> to
/// it makes every subscribed client refresh. Under RESP2 the dedicated subscription connection subscribes to it as
/// part of its handshake; a RESP3 interactive connection shared with pub/sub has no such connection, and the
/// subscription was never made. See #3254.
/// </summary>
[RunPerProtocol]
public class ConfigurationChannelUnitTests(ITestOutputHelper log)
{
    private const string Channel = "__Booksleeve_MasterChanged";

    private static ConfigurationOptions Configure(InProcessTestServer server, bool prefix, bool shared)
    {
        var config = server.GetClientConfig();
        config.ConfigurationChannel = Channel; // the test server turns this off by default
        config.SharedSubscriptionConnection = shared;
        if (prefix) config.ChannelPrefix = RedisChannel.Literal("testuser-");
        return config;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PublishingToTheConfigurationChannelIsHeard(bool prefix, bool shared)
    {
        using var server = new InProcessTestServer(log);
        var config = Configure(server, prefix, shared);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        var heard = new TaskCompletionSource<EndPointEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.ConfigurationChangedBroadcast += (_, e) => heard.TrySetResult(e);

        // the subscription is made in the background after connecting, so nobody is listening for an instant:
        // publish until someone is, rather than assume the order
        var subscriber = conn.GetSubscriber();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!heard.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            await subscriber.PublishAsync(RedisChannel.Literal(Channel), "*");
            await Task.WhenAny(heard.Task, Task.Delay(100));
        }

        Assert.True(heard.Task.IsCompleted, "the configuration channel has no subscriber");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TheLibrarysOwnBroadcastIsHeard(bool prefix, bool shared)
    {
        // the channel is subscribed with the channel prefix applied, so it must be fired with it too - or a
        // client that changes a server's role announces it to a channel that nobody is listening on
        using var server = new InProcessTestServer(log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(Configure(server, prefix, shared));

        var heard = new TaskCompletionSource<EndPointEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.ConfigurationChangedBroadcast += (_, e) => heard.TrySetResult(e);

        var admin = conn.GetServer(server.DefaultEndPoint);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!heard.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            await admin.ReplicaOfAsync(null!); // broadcasts the change once it is made
            await Task.WhenAny(heard.Task, Task.Delay(100));
        }

        Assert.True(heard.Task.IsCompleted, "the broadcast was not heard");
    }
}
