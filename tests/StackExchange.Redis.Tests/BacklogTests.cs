using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class BacklogTests : TestBase
    {
        public BacklogTests(ITestOutputHelper output) : base (output) { }

        protected override string GetConfiguration() => TestConfig.Current.MasterServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

        // TODO: Sync route testing (e.g. Ping() for TryWriteSync path)
        // TODO: Specific server calls

        [Fact]
        public async Task FailFast()
        {
            try
            {
                // Ensuring the FailFast policy errors immediate with no connection available exceptions
                var options = new ConfigurationOptions()
                {
                    BacklogPolicy = BacklogPolicy.FailFast,
                    AbortOnConnectFail = false,
                    ConnectTimeout = 1000,
                    ConnectRetry = 2,
                    SyncTimeout = 10000,
                    KeepAlive = 10000,
                    AsyncTimeout = 5000,
                    AllowAdmin = true,
                };
                options.EndPoints.Add(TestConfig.Current.MasterServerAndPort);

                using var muxer = await ConnectionMultiplexer.ConnectAsync(options, Writer);

                var db = muxer.GetDatabase();
                Writer.WriteLine("Test: Initial (connected) ping");
                await db.PingAsync();

                var server = muxer.GetServerSnapshot()[0];
                var stats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.Equal(0, stats.BacklogMessagesPending); // Everything's normal

                // Fail the connection
                Writer.WriteLine("Test: Simulating failure");
                muxer.AllowConnect = false;
                server.SimulateConnectionFailure(SimulatedFailureType.All);
                Assert.False(muxer.IsConnected);

                // Queue up some commands
                Writer.WriteLine("Test: Disconnected pings");
                await Assert.ThrowsAsync<RedisConnectionException>(() => db.PingAsync());

                var disconnectedStats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.False(muxer.IsConnected);
                Assert.Equal(0, disconnectedStats.BacklogMessagesPending);

                Writer.WriteLine("Test: Allowing reconnect");
                muxer.AllowConnect = true;
                Writer.WriteLine("Test: Awaiting reconnect");
                await UntilCondition(TimeSpan.FromSeconds(3), () => muxer.IsConnected).ForAwait();

                Writer.WriteLine("Test: Reconnecting");
                Assert.True(muxer.IsConnected);
                var reconnectedStats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.Equal(0, reconnectedStats.BacklogMessagesPending);
                Assert.Equal(0, reconnectedStats.BacklogMessagesPendingGeneral);
                Assert.Equal(0, reconnectedStats.BacklogMessagesPendingSpecificServer);

                _ = db.PingAsync();
                _ = db.PingAsync();
                var lastPing = db.PingAsync();

                // We should see none queued
                Assert.Equal(0, stats.BacklogMessagesPending);
                await lastPing;
            }
            finally
            {
                ClearAmbientFailures();
            }
        }


        [Fact]
        public async Task QueuesAndFlushesAfterReconnecting()
        {
            try
            {
                var options = new ConfigurationOptions()
                {
                    BacklogPolicy = BacklogPolicy.Default,
                    AbortOnConnectFail = false,
                    ConnectTimeout = 1000,
                    ConnectRetry = 2,
                    SyncTimeout = 10000,
                    KeepAlive = 10000,
                    AsyncTimeout = 5000,
                    AllowAdmin = true,
                };                
                options.EndPoints.Add(TestConfig.Current.MasterServerAndPort);

                using var muxer = await ConnectionMultiplexer.ConnectAsync(options, Writer);

                var db = muxer.GetDatabase();
                Writer.WriteLine("Test: Initial (connected) ping");
                await db.PingAsync();

                var server = muxer.GetServerSnapshot()[0];
                var stats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.Equal(0, stats.BacklogMessagesPending); // Everything's normal

                // Fail the connection
                Writer.WriteLine("Test: Simulating failure");
                muxer.AllowConnect = false;
                server.SimulateConnectionFailure(SimulatedFailureType.All);
                Assert.False(muxer.IsConnected);

                // Queue up some commands
                Writer.WriteLine("Test: Disconnected pings");
                _ = db.PingAsync();
                _ = db.PingAsync();
                var lastPing = db.PingAsync();

                // TODO: Add specific server call

                var disconnectedStats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.False(muxer.IsConnected);
                Assert.True(disconnectedStats.BacklogMessagesPending >= 3, $"Expected {nameof(disconnectedStats.BacklogMessagesPending)} > 3, got {disconnectedStats.BacklogMessagesPending}");

                Writer.WriteLine("Test: Allowing reconnect");
                muxer.AllowConnect = true;
                Writer.WriteLine("Test: Awaiting reconnect");
                await UntilCondition(TimeSpan.FromSeconds(3), () => muxer.IsConnected).ForAwait();

                Writer.WriteLine("Test: Awaiting ping1");
                await lastPing;

                Writer.WriteLine("Test: Reconnecting");
                Assert.True(muxer.IsConnected);
                var reconnectedStats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.Equal(0, reconnectedStats.BacklogMessagesPending);
                Assert.Equal(0, reconnectedStats.BacklogMessagesPendingGeneral);
                Assert.Equal(0, reconnectedStats.BacklogMessagesPendingSpecificServer);

                _ = db.PingAsync();
                _ = db.PingAsync();
                lastPing = db.PingAsync();

                // We should see none queued
                Assert.Equal(0, stats.BacklogMessagesPending);
                await lastPing;
            }
            finally
            {
                ClearAmbientFailures();
            }
        }
    }
}
