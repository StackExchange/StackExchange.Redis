using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class PubSubMultiserver : TestBase
    {
        public PubSubMultiserver(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }
        protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts + ",connectTimeout=10000";

        [Fact]
        public void ChannelSharding()
        {
            using var muxer = Create(channelPrefix: Me()) as ConnectionMultiplexer;

            var defaultSlot = muxer.ServerSelectionStrategy.HashSlot(default(RedisChannel));
            var slot1 = muxer.ServerSelectionStrategy.HashSlot((RedisChannel)"hey");
            var slot2 = muxer.ServerSelectionStrategy.HashSlot((RedisChannel)"hey2");

            Assert.NotEqual(defaultSlot, slot1);
            Assert.NotEqual(ServerSelectionStrategy.NoSlot, slot1);
            Assert.NotEqual(slot1, slot2);
        }

        [Fact]
        public async Task SubscriptionNodeReconnecting()
        {
            Log("Connecting...");
            using var muxer = Create(allowAdmin: true) as ConnectionMultiplexer;
            var sub = muxer.GetSubscriber();
            var channel = (RedisChannel)Me();

            Log("Subscribing...");
            await sub.SubscribeAsync(channel, (channel, val) => Log("Message: " + val));

            Assert.True(sub.IsConnected(channel));

            var endpoint = sub.SubscribedEndpoint(channel);
            var subscribedServer = muxer.GetServer(endpoint);
            var subscribedServerEndpoint = muxer.GetServerEndPoint(endpoint);

            Assert.True(subscribedServer.IsConnected, "subscribedServer.IsConnected");
            Assert.True(subscribedServerEndpoint.IsConnected, "subscribedServerEndpoint.IsConnected");
            Assert.True(subscribedServerEndpoint.IsSubscriberConnected, "subscribedServerEndpoint.IsSubscriberConnected");

            Assert.True(muxer.TryGetSubscription(channel, out var subscription));
            var initialServer = subscription.GetCurrentServer();
            Assert.NotNull(initialServer);
            Assert.True(initialServer.IsConnected);
            Log($"Connected to: " + initialServer);

            muxer.AllowConnect = false;
            subscribedServerEndpoint.SimulateConnectionFailure(SimulatedFailureType.AllSubscription);

            Assert.True(subscribedServerEndpoint.IsConnected, "subscribedServerEndpoint.IsConnected");
            Assert.False(subscribedServerEndpoint.IsSubscriberConnected, "subscribedServerEndpoint.IsSubscriberConnected");

            await UntilCondition(TimeSpan.FromSeconds(5), () => subscription.IsConnected);
            Assert.True(subscription.IsConnected);

            var newServer = subscription.GetCurrentServer();
            Assert.NotNull(newServer);
            Assert.NotEqual(newServer, initialServer);
            Log($"Now connected to: " + initialServer);
        }

        //      04:14:23.7955: Connection failed(InternalFailure): 127.0.0.1:7002/Subscription: StackExchange.Redis.RedisConnectionException: InternalFailure on 127.0.0.1:7002/Subscription, Initializing/NotStarted, last: SUBSCRIBE, origin: ConnectedAsync, outstanding: 0, last-read: 0s ago, last-write: 0s ago, keep-alive: 60s, state: Connecting, mgr: 9 of 10 available, last-heartbeat: never, last-mbeat: 0s ago, global: 23s ago, v: 2.5.49.64454 ---> StackExchange.Redis.RedisConnectionException: debugging
        //at StackExchange.Redis.PhysicalConnection.OnDebugAbort() in C:\git\StackExchange\StackExchange.Redis\src\StackExchange.Redis\PhysicalConnection.cs:line 1560
        // at StackExchange.Redis.PhysicalConnection.<ConnectedAsync>d__104.MoveNext() in C:\git\StackExchange\StackExchange.Redis\src\StackExchange.Redis\PhysicalConnection.cs:line 1389
        // --- End of inner exception stack trace ---

        // TODO: Primary/Replica failover
        // TODO: Subscribe failover, but with CommandFlags
    }
}
