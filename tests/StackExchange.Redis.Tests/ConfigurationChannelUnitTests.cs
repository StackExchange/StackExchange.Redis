using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The configuration-change channel is how a client is told, by hand, that the topology moved: a <c>PUBLISH</c> to
/// it makes every subscribed client refresh. Under RESP2 the dedicated subscription connection subscribes to it as
/// part of its handshake; RESP3 has no such connection, and the subscription was never made. See #3254.
/// </summary>
[RunPerProtocol]
public class ConfigurationChannelUnitTests(ITestOutputHelper log)
{
    private const string Channel = "__Booksleeve_MasterChanged";

    [Fact]
    public async Task PublishingToTheConfigurationChannelIsHeard()
    {
        using var server = new InProcessTestServer(log);
        var config = server.GetClientConfig();
        config.ConfigurationChannel = Channel; // the test server turns this off by default
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
}
