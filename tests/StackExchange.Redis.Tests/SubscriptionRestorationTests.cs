using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class SubscriptionRestorationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OverlappingRestoresCannotSubscribeOnTwoServers(bool pattern, bool fireAndForget)
    {
        using var server = new PausedSubscribeServer(output);
        await using var connection = await server.ConnectAsync();
        var subscriber = connection.DefaultSubscriber;
        var channel = pattern ? RedisChannel.Pattern("restoration.*") : RedisChannel.Literal("restoration.channel");
        var subscription = new ConnectionMultiplexer.SingleNodeSubscription(fireAndForget ? CommandFlags.FireAndForget : CommandFlags.None);
        var firstServer = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        var secondServer = connection.GetServerEndPoint(server.SecondEndPoint, ServerProvenance.Configured);
        var first = subscription.EnsureSubscribedToServerAsync(subscriber, channel,
            fireAndForget ? CommandFlags.FireAndForget : CommandFlags.None, true, firstServer);
        try
        {
            await server.FirstSubscribe.Task.WithTimeout(5000);
            var second = subscription.EnsureSubscribedToServerAsync(subscriber, channel, CommandFlags.None, true, secondServer);
            var ping = Message.Create(-1, CommandFlags.None, RedisCommand.PING);
            ping.SetForSubscriptionBridge();
            await connection.ExecuteAsyncImpl(ping, ResultProcessor.ResponseTimer, null, secondServer).WithTimeout(5000);
            Assert.Equal(0, Volatile.Read(ref server.SecondSubscribeCount));
            server.Release.Set();
            await Task.WhenAll(first, second).WithTimeout(5000);
            Assert.Equal(1, Volatile.Read(ref server.FirstSubscribeCount));
            Assert.Equal(0, Volatile.Read(ref server.SecondSubscribeCount));
            Assert.Same(firstServer, subscription.GetAnyCurrentServer());
        }
        finally
        {
            server.Release.Set();
            try { await first.WithTimeout(5000); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousFireAndForgetRestoreCoalescesWithPendingAcknowledgement(bool pattern)
    {
        using var server = new PausedSubscribeServer(output);
        await using var connection = await server.ConnectAsync();
        var channel = pattern ? RedisChannel.Pattern("restoration.*") : RedisChannel.Literal("restoration.channel");
        var subscription = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.None);
        var firstServer = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        var first = subscription.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true, firstServer);
        try
        {
            await server.FirstSubscribe.Task.WithTimeout(5000);
            Assert.Equal(0, subscription.EnsureSubscribedToServer(connection.DefaultSubscriber, channel, CommandFlags.FireAndForget, true));
            server.Release.Set();
            await first.WithTimeout(5000);
            Assert.Equal(1, server.FirstSubscribeCount + server.SecondSubscribeCount);
        }
        finally { server.Release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsubscribeWaitsForAnInFlightSubscribe(bool pattern)
    {
        using var server = new PausedSubscribeServer(output);
        await using var connection = await server.ConnectAsync();
        var channel = pattern ? RedisChannel.Pattern("restoration.*") : RedisChannel.Literal("restoration.channel");
        var subscription = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.None);
        var firstServer = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        var pending = subscription.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true, firstServer);
        try
        {
            await server.FirstSubscribe.Task.WithTimeout(5000);
            var unsubscribe = subscription.UnsubscribeFromServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, null, false);
            Assert.False(unsubscribe.IsCompleted);
            server.Release.Set();
            await Task.WhenAll(pending, unsubscribe).WithTimeout(5000);
            await server.FirstUnsubscribe.Task.WithTimeout(5000);
            Assert.Null(subscription.GetAnyCurrentServer());
            Assert.Equal(0, await subscription.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true));
        }
        finally { server.Release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateAcknowledgementCannotReplaceANewerSubscription(bool pattern)
    {
        using var server = new PausedSubscribeServer(output);
        var config = server.GetClientConfig();
        config.AsyncTimeout = 200;
        config.HeartbeatInterval = TimeSpan.FromMilliseconds(20);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var channel = pattern ? RedisChannel.Pattern("restoration.*") : RedisChannel.Literal("restoration.channel");
        var subscription = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.None);
        var firstServer = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        var secondServer = connection.GetServerEndPoint(server.SecondEndPoint, ServerProvenance.Configured);
        var first = subscription.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true, firstServer);
        try
        {
            await server.FirstSubscribe.Task.WithTimeout(5000);
            await Assert.ThrowsAsync<RedisTimeoutException>(() => first.WithTimeout(5000));
            await subscription.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true, secondServer).WithTimeout(5000);
            Assert.Same(secondServer, subscription.GetAnyCurrentServer());
            server.Release.Set();
            await server.FirstUnsubscribe.Task.WithTimeout(5000);
            Assert.Same(secondServer, subscription.GetAnyCurrentServer());
            Assert.Equal(1, subscription.GetConnectionCount());
        }
        finally { server.Release.Set(); }
    }

    [Fact]
    public async Task ReconnectedSubscriptionUsesTheNewPhysicalConnection()
    {
        using var server = new InProcessTestServer(output);
        var config = server.GetClientConfig();
        config.SharedSubscriptionConnection = false;
        config.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        connection.AllowConnect = true;
        var channel = RedisChannel.Literal("connection-identity");
        var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await connection.GetSubscriber().SubscribeAsync(channel, (_, value) => delivered.TrySetResult(true));
        Assert.True(connection.TryGetSubscription(channel, out var record));
        var subscription = Assert.IsType<ConnectionMultiplexer.SingleNodeSubscription>(record);
        var endpoint = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        var previous = endpoint.GetBridge(ConnectionType.Subscription)!.Physical!;
        Assert.True(subscription.Accepts(previous));
        endpoint.SimulateConnectionFailure(SimulatedFailureType.AllSubscription);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (ReferenceEquals(endpoint.GetBridge(ConnectionType.Subscription)!.Physical, previous) || !subscription.IsConnectedAny())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Subscription did not reconnect.");
            await Task.Delay(20);
        }
        var current = endpoint.GetBridge(ConnectionType.Subscription)!.Physical!;
        Assert.NotSame(previous, current);
        Assert.False(subscription.Accepts(previous));
        Assert.True(subscription.Accepts(current));
        await connection.GetDatabase().PublishAsync(channel, "after-reconnect");
        await delivered.Task.WithTimeout(5000);
    }

    private sealed class PausedSubscribeServer : InProcessTestServer
    {
        public EndPoint SecondEndPoint { get; }
        public readonly ManualResetEventSlim Release = new();
        public readonly TaskCompletionSource<bool> FirstSubscribe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FirstSubscribeCount, SecondSubscribeCount;
        public readonly TaskCompletionSource<bool> FirstUnsubscribe = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PausedSubscribeServer(ITestOutputHelper output) : base(output) => SecondEndPoint = AddEmptyNode();

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            if ((request.IsString(0, "SUBSCRIBE"u8) || request.IsString(0, "PSUBSCRIBE"u8))
                && request.GetString(1).StartsWith("restoration.", StringComparison.Ordinal))
            {
                TryGetNode(DefaultEndPoint, out var firstNode);
                if (ReferenceEquals(client.Node, firstNode))
                {
                    Interlocked.Increment(ref FirstSubscribeCount);
                    FirstSubscribe.TrySetResult(true);
                    if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test subscription barrier timed out.");
                }
                else Interlocked.Increment(ref SecondSubscribeCount);
            }
            var result = base.Execute(client, in request);
            if ((request.IsString(0, "UNSUBSCRIBE"u8) || request.IsString(0, "PUNSUBSCRIBE"u8))
                && request.GetString(1).StartsWith("restoration.", StringComparison.Ordinal))
            {
                TryGetNode(DefaultEndPoint, out var firstNode);
                if (ReferenceEquals(client.Node, firstNode)) FirstUnsubscribe.TrySetResult(true);
            }
            return result;
        }
    }
}
