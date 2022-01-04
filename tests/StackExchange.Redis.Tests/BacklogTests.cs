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
            void PrintSnapshot(ConnectionMultiplexer muxer)
            {
                Writer.WriteLine("Snapshot summary:");
                foreach (var server in muxer.GetServerSnapshot())
                {
                    Writer.WriteLine($"  {server.EndPoint}: ");
                    Writer.WriteLine($"     Type: {server.ServerType}");
                    Writer.WriteLine($"     IsConnected: {server.IsConnected}");
                    Writer.WriteLine($"      IsConnecting: {server.IsConnecting}");
                    Writer.WriteLine($"      IsSelectable(allowDisconnected: true): {server.IsSelectable(RedisCommand.PING, true)}");
                    Writer.WriteLine($"      IsSelectable(allowDisconnected: false): {server.IsSelectable(RedisCommand.PING, false)}");
                    Writer.WriteLine($"      UnselectableFlags: {server.GetUnselectableFlags()}");
                    var bridge = server.GetBridge(RedisCommand.PING, create: false);
                    Writer.WriteLine($"      GetBridge: {bridge}");
                    Writer.WriteLine($"        IsConnected: {bridge.IsConnected}");
                    Writer.WriteLine($"        ConnectionState: {bridge.ConnectionState}");
                }
            }

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
                Assert.True(server.IsConnected);
                var reconnectedStats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.Equal(0, reconnectedStats.BacklogMessagesPending);

                _ = db.PingAsync();
                _ = db.PingAsync();
                var lastPing = db.PingAsync();

                // For debug, print out the snapshot and server states
                PrintSnapshot(muxer);

                Assert.NotNull(muxer.SelectServer(Message.Create(-1, CommandFlags.None, RedisCommand.PING)));

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
                    SocketManager = SocketManager.ThreadPool,
                };
                options.EndPoints.Add(TestConfig.Current.MasterServerAndPort);

                using var muxer = await ConnectionMultiplexer.ConnectAsync(options, Writer);
                muxer.ErrorMessage += (s, e) => Writer.WriteLine($"Error Message {e.EndPoint}: {e.Message}");
                muxer.InternalError += (s, e) => Writer.WriteLine($"Internal Error {e.EndPoint}: {e.Exception.Message}");

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
                var ignoredA = db.PingAsync();
                var ignoredB = db.PingAsync();
                var lastPing = db.PingAsync();

                // TODO: Add specific server call

                var disconnectedStats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.False(muxer.IsConnected);
                Assert.True(disconnectedStats.BacklogMessagesPending >= 3, $"Expected {nameof(disconnectedStats.BacklogMessagesPending)} > 3, got {disconnectedStats.BacklogMessagesPending}");

                Writer.WriteLine("Test: Allowing reconnect");
                muxer.AllowConnect = true;
                Writer.WriteLine("Test: Awaiting reconnect");
                await UntilCondition(TimeSpan.FromSeconds(3), () => muxer.IsConnected).ForAwait();

                Writer.WriteLine("Test: Checking reconnected 1");
                Assert.True(muxer.IsConnected);

                Writer.WriteLine("Test: ignoredA Status: " + ignoredA.Status);
                Writer.WriteLine("Test: ignoredB Status: " + ignoredB.Status);
                Writer.WriteLine("Test: lastPing Status: " + lastPing.Status);
                var afterConnectedStats = server.GetBridgeStatus(RedisCommand.PING);
                Writer.WriteLine($"Test: BacklogStatus: {afterConnectedStats.BacklogStatus}, BacklogMessagesPending: {afterConnectedStats.BacklogMessagesPending}, IsWriterActive: {afterConnectedStats.IsWriterActive}, MessagesSinceLastHeartbeat: {afterConnectedStats.MessagesSinceLastHeartbeat}, TotalBacklogMessagesQueued: {afterConnectedStats.TotalBacklogMessagesQueued}");

                Writer.WriteLine("Test: Awaiting lastPing 1");
                await lastPing;

                Writer.WriteLine("Test: Checking reconnected 2");
                Assert.True(muxer.IsConnected);
                var reconnectedStats = server.GetBridgeStatus(RedisCommand.PING);
                Assert.Equal(0, reconnectedStats.BacklogMessagesPending);

                Writer.WriteLine("Test: Pinging again...");
                _ = db.PingAsync();
                _ = db.PingAsync();
                Writer.WriteLine("Test: Last Ping issued");
                lastPing = db.PingAsync();

                // We should see none queued
                Writer.WriteLine("Test: BacklogMessagesPending check");
                Assert.Equal(0, stats.BacklogMessagesPending);
                Writer.WriteLine("Test: Awaiting lastPing 2");
                await lastPing;
                Writer.WriteLine("Test: Done");
            }
            finally
            {
                ClearAmbientFailures();
            }
        }
    }
}
