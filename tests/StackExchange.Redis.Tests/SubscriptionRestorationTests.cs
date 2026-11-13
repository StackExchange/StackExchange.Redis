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
            Assert.True(await Poll.UntilAsync(() => subscription.IsConnectedAny(), timeoutMilliseconds: 5000));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimedOutSynchronousUnsubscribeStillDetachesThePendingSubscription(bool pattern)
    {
        using var server = new PausedSubscribeServer(output);
        var config = server.GetClientConfig();
        config.SyncTimeout = 100;
        config.AsyncTimeout = 5000;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var subscriber = connection.GetSubscriber();
        var channel = pattern ? RedisChannel.Pattern("restoration.*") : RedisChannel.Literal("restoration.channel");
        var subscription = connection.GetOrAddSubscription(channel, CommandFlags.None);
        subscription.Add((_, _) => { }, null);
        var pending = subscription.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, false,
            connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured));
        try
        {
            await server.FirstSubscribe.Task.WithTimeout(5000);
            subscriber.Unsubscribe(channel);
            Assert.False(connection.TryGetSubscription(channel, out _));
            server.Release.Set();
            await pending.WithTimeout(5000);
            await server.FirstUnsubscribe.Task.WithTimeout(5000);
        }
        finally { server.Release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsubscribeAllContinuesPastAnInFlightSubscription(bool pattern)
    {
        using var server = new PausedSubscribeServer(output);
        var config = server.GetClientConfig();
        config.SyncTimeout = 100;
        config.AsyncTimeout = 5000;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var subscriber = connection.DefaultSubscriber;
        var firstChannel = pattern ? RedisChannel.Pattern("restoration.first.*") : RedisChannel.Literal("restoration.first");
        var secondChannel = pattern ? RedisChannel.Pattern("restoration.second.*") : RedisChannel.Literal("restoration.second");
        var first = connection.GetOrAddSubscription(firstChannel, CommandFlags.None);
        var second = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.None);
        var pending = first.EnsureSubscribedToServerAsync(subscriber, firstChannel, CommandFlags.None, false,
            connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured));
        try
        {
            await server.FirstSubscribe.Task.WithTimeout(5000);
            await second.EnsureSubscribedToServerAsync(subscriber, secondChannel, CommandFlags.None, false,
                connection.GetServerEndPoint(server.SecondEndPoint, ServerProvenance.Configured)).WithTimeout(5000);
            Assert.True(connection.GetSubscriptions().TryAdd(secondChannel, second));
            subscriber.UnsubscribeAll();
            Assert.False(connection.TryGetSubscription(firstChannel, out _));
            Assert.False(connection.TryGetSubscription(secondChannel, out _));
            server.Release.Set();
            await pending.WithTimeout(5000);
            Assert.True(await Poll.UntilAsync(() => Volatile.Read(ref server.UnsubscribeCount) >= 2, timeoutMilliseconds: 5000));
        }
        finally { server.Release.Set(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SubscriptionFireAndForgetFlagsApplyToLaterRestoreCalls(bool pattern, bool asynchronous)
    {
        using var server = new PausedSubscribeServer(output);
        await using var connection = await server.ConnectAsync();
        var subscriber = connection.DefaultSubscriber;
        var channel = pattern ? RedisChannel.Pattern("restoration.*") : RedisChannel.Literal("restoration.channel");
        var subscription = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.FireAndForget);
        var endpoint = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        await subscription.EnsureSubscribedToServerAsync(subscriber, channel, CommandFlags.FireAndForget, true, endpoint);
        try
        {
            await server.FirstSubscribe.Task.WithTimeout(5000);
            if (asynchronous)
                Assert.Equal(0, await subscription.EnsureSubscribedToServerAsync(subscriber, channel, CommandFlags.None, true).WithTimeout(1000));
            else
                Assert.Equal(0, subscription.EnsureSubscribedToServer(subscriber, channel, CommandFlags.None, true));
        }
        finally { server.Release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveRouteDeliversUntilTheReplacementAcknowledgement(bool pattern)
    {
        using var server = new PausedSubscribeServer(output);
        server.Release.Set();
        await using var connection = await server.ConnectAsync();
        var channel = pattern ? RedisChannel.Pattern("restoration.*") : RedisChannel.Literal("restoration.channel");
        var subscription = Assert.IsType<ConnectionMultiplexer.SingleNodeSubscription>(connection.GetOrAddSubscription(channel, CommandFlags.None));
        var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscription.Add((_, value) => { if (value == "during-restore") delivered.TrySetResult(true); }, null);
        var first = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        var second = connection.GetServerEndPoint(server.SecondEndPoint, ServerProvenance.Configured);
        await subscription.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true, first);
        var physical = first.GetBridge(ConnectionType.Subscription)!.Physical!;
        server.PauseSecond = true;
        server.Release.Reset();
        Assert.True(subscription.TryRemoveEndpoint(first));
        var pending = subscription.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true, second);
        try
        {
            await server.SecondSubscribe.Task.WithTimeout(5000);
            Assert.True(subscription.IsConnectedAny());
            Assert.True(subscription.Accepts(physical));
            Assert.Same(first, subscription.GetAnyCurrentServer());
            await connection.GetDatabase().PublishAsync(RedisChannel.Literal("restoration.channel"), "during-restore");
            await delivered.Task.WithTimeout(5000);
            server.Release.Set();
            await pending.WithTimeout(5000);
            Assert.False(subscription.Accepts(physical));
            Assert.True(subscription.Accepts(second.GetBridge(ConnectionType.Subscription)!.Physical!));
            await server.FirstUnsubscribe.Task.WithTimeout(5000);
        }
        finally { server.Release.Set(); }
    }

    [Fact]
    public async Task SubscribeAcknowledgementCompletesWhenBookkeepingThrows()
    {
        using var server = new InProcessTestServer(output);
        await using var connection = await server.ConnectAsync();
        var reported = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.InternalError += (_, e) => { if (e.Exception is InvalidOperationException) reported.TrySetResult(true); };
        var subscription = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.None);
        var message = subscription.GetSubscriptionMessage(RedisChannel.Literal("ack-failure"), ConnectionMultiplexer.SubscriptionAction.Subscribe,
            CommandFlags.None, false, trackReply: true);
        var processor = new ResultProcessor.TrackSubscriptionsProcessor(subscription, _ => throw new InvalidOperationException("test bookkeeping failure"));
        var endpoint = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        Assert.True(await connection.ExecuteAsyncImpl(message, processor, null, endpoint).WithTimeout(5000));
        await reported.Task.WithTimeout(5000);
        await connection.GetSubscriber().PingAsync().WithTimeout(5000);
    }

    [Fact]
    public async Task UnsubscribeTaskPreservesTheCallersAsyncState()
    {
        using var server = new InProcessTestServer(output);
        await using var connection = await server.ConnectAsync();
        var state = new object();
        var subscriber = connection.GetSubscriber(state);
        var channel = RedisChannel.Literal("async-state");
        await subscriber.SubscribeAsync(channel, (_, _) => { });
        var pending = subscriber.UnsubscribeAsync(channel);
        Assert.Same(state, pending.AsyncState);
        await pending.WithTimeout(5000);
    }

    [Fact]
    public async Task LateCleanupDoesNotUnsubscribeTheReplacementOwner()
    {
        using var server = new PausedSubscribeServer(output);
        await using var connection = await server.ConnectAsync();
        var channel = RedisChannel.Literal("restoration.owner");
        var old = connection.GetOrAddSubscription(channel, CommandFlags.None);
        var first = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        var second = connection.GetServerEndPoint(server.SecondEndPoint, ServerProvenance.Configured);
        var pending = old.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true, first);
        try
        {
            await server.FirstSubscribe.Task.WithTimeout(5000);
            await connection.GetSubscriber().UnsubscribeAsync(channel, flags: CommandFlags.FireAndForget);
            var replacement = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.None);
            var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            replacement.Add((_, value) => { if (value == "new-owner") received.TrySetResult(true); }, null);
            await replacement.EnsureSubscribedToServerAsync(connection.DefaultSubscriber, channel, CommandFlags.None, true, second);
            Assert.True(connection.GetSubscriptions().TryAdd(channel, replacement));
            server.Release.Set();
            await pending.WithTimeout(5000);
            await server.FirstUnsubscribe.Task.WithTimeout(5000);
            Assert.True(replacement.IsConnectedAny());
            Assert.Same(second, replacement.GetAnyCurrentServer());
            await connection.GetDatabase().PublishAsync(channel, "new-owner");
            await received.Task.WithTimeout(5000);
        }
        finally { server.Release.Set(); }
    }

    [Fact]
    public async Task RepeatedSubscriptionReplacementDoesNotCallStoppedHandlers()
    {
        using var server = new InProcessTestServer(output);
        await using var connection = await server.ConnectAsync();
        var subscriber = connection.GetSubscriber();
        var channel = RedisChannel.Literal("repeated-owner");
        int deliveries = 0, stoppedDeliveries = 0;
        for (int i = 0; i < 25; i++)
        {
            int stopped = 0;
            var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<RedisChannel, RedisValue> handler = (_, _) =>
            {
                if (Volatile.Read(ref stopped) != 0) Interlocked.Increment(ref stoppedDeliveries);
                Interlocked.Increment(ref deliveries);
                received.TrySetResult(true);
            };
            await subscriber.SubscribeAsync(channel, handler);
            await subscriber.PublishAsync(channel, i);
            await received.Task.WithTimeout(5000);
            await subscriber.UnsubscribeAsync(channel, handler);
            Volatile.Write(ref stopped, 1);
        }
        Assert.Equal(25, Volatile.Read(ref deliveries));
        Assert.Equal(0, Volatile.Read(ref stoppedDeliveries));
    }

    private sealed class PausedSubscribeServer : InProcessTestServer
    {
        public EndPoint SecondEndPoint { get; }
        public readonly ManualResetEventSlim Release = new();
        public readonly TaskCompletionSource<bool> FirstSubscribe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FirstSubscribeCount, SecondSubscribeCount, UnsubscribeCount;
        public bool PauseSecond;
        public readonly TaskCompletionSource<bool> SecondSubscribe = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                    if (!PauseSecond && !Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test subscription barrier timed out.");
                }
                else
                {
                    Interlocked.Increment(ref SecondSubscribeCount);
                    SecondSubscribe.TrySetResult(true);
                    if (PauseSecond && !Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test subscription barrier timed out.");
                }
            }
            var result = base.Execute(client, in request);
            if ((request.IsString(0, "UNSUBSCRIBE"u8) || request.IsString(0, "PUNSUBSCRIBE"u8))
                && request.GetString(1).StartsWith("restoration.", StringComparison.Ordinal))
            {
                TryGetNode(DefaultEndPoint, out var firstNode);
                Interlocked.Increment(ref UnsubscribeCount);
                if (ReferenceEquals(client.Node, firstNode)) FirstUnsubscribe.TrySetResult(true);
            }
            return result;
        }
    }
}
