using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class BacklogTests : TestBase
{
    public BacklogTests(ITestOutputHelper output) : base (output) { }

    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

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
                Writer.WriteLine($"        IsConnected: {bridge?.IsConnected}");
                Writer.WriteLine($"        ConnectionState: {bridge?.ConnectionState}");
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
            options.EndPoints.Add(TestConfig.Current.PrimaryServerAndPort);

            using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);

            var db = conn.GetDatabase();
            Writer.WriteLine("Test: Initial (connected) ping");
            await db.PingAsync();

            var server = conn.GetServerSnapshot()[0];
            var stats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.Equal(0, stats.BacklogMessagesPending); // Everything's normal

            // Fail the connection
            Writer.WriteLine("Test: Simulating failure");
            conn.AllowConnect = false;
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(conn.IsConnected);

            // Queue up some commands
            Writer.WriteLine("Test: Disconnected pings");
            await Assert.ThrowsAsync<RedisConnectionException>(() => db.PingAsync());

            var disconnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.False(conn.IsConnected);
            Assert.Equal(0, disconnectedStats.BacklogMessagesPending);

            Writer.WriteLine("Test: Allowing reconnect");
            conn.AllowConnect = true;
            Writer.WriteLine("Test: Awaiting reconnect");
            await UntilConditionAsync(TimeSpan.FromSeconds(3), () => conn.IsConnected).ForAwait();

            Writer.WriteLine("Test: Reconnecting");
            Assert.True(conn.IsConnected);
            Assert.True(server.IsConnected);
            var reconnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.Equal(0, reconnectedStats.BacklogMessagesPending);

            _ = db.PingAsync();
            _ = db.PingAsync();
            var lastPing = db.PingAsync();

            // For debug, print out the snapshot and server states
            PrintSnapshot(conn);

            Assert.NotNull(conn.SelectServer(Message.Create(-1, CommandFlags.None, RedisCommand.PING)));

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
    public async Task QueuesAndFlushesAfterReconnectingAsync()
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
            options.EndPoints.Add(TestConfig.Current.PrimaryServerAndPort);

            using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
            conn.ErrorMessage += (s, e) => Log($"Error Message {e.EndPoint}: {e.Message}");
            conn.InternalError += (s, e) => Log($"Internal Error {e.EndPoint}: {e.Exception.Message}");
            conn.ConnectionFailed += (s, a) => Log("Disconnected: " + EndPointCollection.ToString(a.EndPoint));
            conn.ConnectionRestored += (s, a) => Log("Reconnected: " + EndPointCollection.ToString(a.EndPoint));

            var db = conn.GetDatabase();
            Writer.WriteLine("Test: Initial (connected) ping");
            await db.PingAsync();

            var server = conn.GetServerSnapshot()[0];
            var stats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.Equal(0, stats.BacklogMessagesPending); // Everything's normal

            // Fail the connection
            Writer.WriteLine("Test: Simulating failure");
            conn.AllowConnect = false;
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(conn.IsConnected);

            // Queue up some commands
            Writer.WriteLine("Test: Disconnected pings");
            var ignoredA = db.PingAsync();
            var ignoredB = db.PingAsync();
            var lastPing = db.PingAsync();

            // TODO: Add specific server call

            var disconnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.False(conn.IsConnected);
            Assert.True(disconnectedStats.BacklogMessagesPending >= 3, $"Expected {nameof(disconnectedStats.BacklogMessagesPending)} > 3, got {disconnectedStats.BacklogMessagesPending}");

            Writer.WriteLine("Test: Allowing reconnect");
            conn.AllowConnect = true;
            Writer.WriteLine("Test: Awaiting reconnect");
            await UntilConditionAsync(TimeSpan.FromSeconds(3), () => conn.IsConnected).ForAwait();

            Writer.WriteLine("Test: Checking reconnected 1");
            Assert.True(conn.IsConnected);

            Writer.WriteLine("Test: ignoredA Status: " + ignoredA.Status);
            Writer.WriteLine("Test: ignoredB Status: " + ignoredB.Status);
            Writer.WriteLine("Test: lastPing Status: " + lastPing.Status);
            var afterConnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Writer.WriteLine($"Test: BacklogStatus: {afterConnectedStats.BacklogStatus}, BacklogMessagesPending: {afterConnectedStats.BacklogMessagesPending}, IsWriterActive: {afterConnectedStats.IsWriterActive}, MessagesSinceLastHeartbeat: {afterConnectedStats.MessagesSinceLastHeartbeat}, TotalBacklogMessagesQueued: {afterConnectedStats.TotalBacklogMessagesQueued}");

            Writer.WriteLine("Test: Awaiting lastPing 1");
            await lastPing;

            Writer.WriteLine("Test: Checking reconnected 2");
            Assert.True(conn.IsConnected);
            var reconnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
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
            options.EndPoints.Add(TestConfig.Current.PrimaryServerAndPort);

            using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
            conn.ErrorMessage += (s, e) => Log($"Error Message {e.EndPoint}: {e.Message}");
            conn.InternalError += (s, e) => Log($"Internal Error {e.EndPoint}: {e.Exception.Message}");
            conn.ConnectionFailed += (s, a) => Log("Disconnected: " + EndPointCollection.ToString(a.EndPoint));
            conn.ConnectionRestored += (s, a) => Log("Reconnected: " + EndPointCollection.ToString(a.EndPoint));

            var db = conn.GetDatabase();
            Writer.WriteLine("Test: Initial (connected) ping");
            await db.PingAsync();

            var server = conn.GetServerSnapshot()[0];
            var stats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.Equal(0, stats.BacklogMessagesPending); // Everything's normal

            // Fail the connection
            Writer.WriteLine("Test: Simulating failure");
            conn.AllowConnect = false;
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(conn.IsConnected);

            // Queue up some commands
            Writer.WriteLine("Test: Disconnected pings");

            Task[] pings = new Task[3];
            pings[0] = RunBlockingSynchronousWithExtraThreadAsync(() => disconnectedPings(1));
            pings[1] = RunBlockingSynchronousWithExtraThreadAsync(() => disconnectedPings(2));
            pings[2] = RunBlockingSynchronousWithExtraThreadAsync(() => disconnectedPings(3));
            void disconnectedPings(int id)
            {
                // No need to delay, we're going to try a disconnected connection immediately so it'll fail...
                Log($"Pinging (disconnected - {id})");
                var result = db.Ping();
                Log($"Pinging (disconnected - {id}) - result: " + result);
            }
            Writer.WriteLine("Test: Disconnected pings issued");

            Assert.False(conn.IsConnected);
            // Give the tasks time to queue
            await UntilConditionAsync(TimeSpan.FromSeconds(5), () => server.GetBridgeStatus(ConnectionType.Interactive).BacklogMessagesPending >= 3);

            var disconnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Log($"Test Stats: (BacklogMessagesPending: {disconnectedStats.BacklogMessagesPending}, TotalBacklogMessagesQueued: {disconnectedStats.TotalBacklogMessagesQueued})");
            Assert.True(disconnectedStats.BacklogMessagesPending >= 3, $"Expected {nameof(disconnectedStats.BacklogMessagesPending)} > 3, got {disconnectedStats.BacklogMessagesPending}");

            Writer.WriteLine("Test: Allowing reconnect");
            conn.AllowConnect = true;
            Writer.WriteLine("Test: Awaiting reconnect");
            await UntilConditionAsync(TimeSpan.FromSeconds(3), () => conn.IsConnected).ForAwait();

            Writer.WriteLine("Test: Checking reconnected 1");
            Assert.True(conn.IsConnected);

            var afterConnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Writer.WriteLine($"Test: BacklogStatus: {afterConnectedStats.BacklogStatus}, BacklogMessagesPending: {afterConnectedStats.BacklogMessagesPending}, IsWriterActive: {afterConnectedStats.IsWriterActive}, MessagesSinceLastHeartbeat: {afterConnectedStats.MessagesSinceLastHeartbeat}, TotalBacklogMessagesQueued: {afterConnectedStats.TotalBacklogMessagesQueued}");

            Writer.WriteLine("Test: Awaiting 3 pings");
            await Task.WhenAll(pings);

            Writer.WriteLine("Test: Checking reconnected 2");
            Assert.True(conn.IsConnected);
            var reconnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.Equal(0, reconnectedStats.BacklogMessagesPending);

            Writer.WriteLine("Test: Pinging again...");
            pings[0] = RunBlockingSynchronousWithExtraThreadAsync(() => disconnectedPings(4));
            pings[1] = RunBlockingSynchronousWithExtraThreadAsync(() => disconnectedPings(5));
            pings[2] = RunBlockingSynchronousWithExtraThreadAsync(() => disconnectedPings(6));
            Writer.WriteLine("Test: Last Ping queued");

            // We should see none queued
            Writer.WriteLine("Test: BacklogMessagesPending check");
            Assert.Equal(0, stats.BacklogMessagesPending);
            Writer.WriteLine("Test: Awaiting 3 more pings");
            await Task.WhenAll(pings);
            Writer.WriteLine("Test: Done");
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    [Fact]
    public async Task QueuesAndFlushesAfterReconnectingClusterAsync()
    {
        try
        {
            var options = ConfigurationOptions.Parse(TestConfig.Current.ClusterServersAndPorts);
            options.BacklogPolicy = BacklogPolicy.Default;
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 1000;
            options.ConnectRetry = 2;
            options.SyncTimeout = 10000;
            options.KeepAlive = 10000;
            options.AsyncTimeout = 5000;
            options.AllowAdmin = true;
            options.SocketManager = SocketManager.ThreadPool;

            using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
            conn.ErrorMessage += (s, e) => Log($"Error Message {e.EndPoint}: {e.Message}");
            conn.InternalError += (s, e) => Log($"Internal Error {e.EndPoint}: {e.Exception.Message}");
            conn.ConnectionFailed += (s, a) => Log("Disconnected: " + EndPointCollection.ToString(a.EndPoint));
            conn.ConnectionRestored += (s, a) => Log("Reconnected: " + EndPointCollection.ToString(a.EndPoint));

            var db = conn.GetDatabase();
            Writer.WriteLine("Test: Initial (connected) ping");
            await db.PingAsync();

            RedisKey meKey = Me();
            var getMsg = Message.Create(0, CommandFlags.None, RedisCommand.GET, meKey);

            ServerEndPoint? server = null; // Get the server specifically for this message's hash slot
            await UntilConditionAsync(TimeSpan.FromSeconds(10), () => (server = conn.SelectServer(getMsg)) != null);

            Assert.NotNull(server);
            var stats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.Equal(0, stats.BacklogMessagesPending); // Everything's normal

            static Task<TimeSpan> PingAsync(ServerEndPoint server, CommandFlags flags = CommandFlags.None)
            {
                var message = ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.PING);

                server.Multiplexer.CheckMessage(message);
                return server.Multiplexer.ExecuteAsyncImpl(message, ResultProcessor.ResponseTimer, null, server);
            }

            // Fail the connection
            Writer.WriteLine("Test: Simulating failure");
            conn.AllowConnect = false;
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(server.IsConnected); // Server isn't connected
            Assert.True(conn.IsConnected); // ...but the multiplexer is

            // Queue up some commands
            Writer.WriteLine("Test: Disconnected pings");
            var ignoredA = PingAsync(server);
            var ignoredB = PingAsync(server);
            var lastPing = PingAsync(server);

            var disconnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.False(server.IsConnected);
            Assert.True(conn.IsConnected);
            Assert.True(disconnectedStats.BacklogMessagesPending >= 3, $"Expected {nameof(disconnectedStats.BacklogMessagesPending)} > 3, got {disconnectedStats.BacklogMessagesPending}");

            Writer.WriteLine("Test: Allowing reconnect");
            conn.AllowConnect = true;
            Writer.WriteLine("Test: Awaiting reconnect");
            await UntilConditionAsync(TimeSpan.FromSeconds(3), () => server.IsConnected).ForAwait();

            Writer.WriteLine("Test: Checking reconnected 1");
            Assert.True(server.IsConnected);
            Assert.True(conn.IsConnected);

            Writer.WriteLine("Test: ignoredA Status: " + ignoredA.Status);
            Writer.WriteLine("Test: ignoredB Status: " + ignoredB.Status);
            Writer.WriteLine("Test: lastPing Status: " + lastPing.Status);
            var afterConnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Writer.WriteLine($"Test: BacklogStatus: {afterConnectedStats.BacklogStatus}, BacklogMessagesPending: {afterConnectedStats.BacklogMessagesPending}, IsWriterActive: {afterConnectedStats.IsWriterActive}, MessagesSinceLastHeartbeat: {afterConnectedStats.MessagesSinceLastHeartbeat}, TotalBacklogMessagesQueued: {afterConnectedStats.TotalBacklogMessagesQueued}");

            Writer.WriteLine("Test: Awaiting lastPing 1");
            await lastPing;

            Writer.WriteLine("Test: Checking reconnected 2");
            Assert.True(server.IsConnected);
            Assert.True(conn.IsConnected);
            var reconnectedStats = server.GetBridgeStatus(ConnectionType.Interactive);
            Assert.Equal(0, reconnectedStats.BacklogMessagesPending);

            Writer.WriteLine("Test: Pinging again...");
            _ = PingAsync(server);
            _ = PingAsync(server);
            Writer.WriteLine("Test: Last Ping issued");
            lastPing = PingAsync(server);

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
