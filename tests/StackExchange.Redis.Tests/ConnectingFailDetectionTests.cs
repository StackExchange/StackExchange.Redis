using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ConnectingFailDetectionTests(ITestOutputHelper output) : TestBase(output)
{
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

    [Fact]
    public async Task FastNoticesFailOnConnectingSyncCompletion()
    {
        try
        {
            await using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false);
            conn.RawConfig.ReconnectRetryPolicy = new LinearRetry(200);

            var db = conn.GetDatabase();
            await db.PingAsync();

            var server = conn.GetServer(conn.GetEndPoints()[0]);
            var server2 = conn.GetServer(conn.GetEndPoints()[1]);

            conn.AllowConnect = false;

            // muxer.IsConnected is true of *any* are connected, simulate failure for all cases.
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(server.IsConnected);
            Assert.True(server2.IsConnected);
            Assert.True(conn.IsConnected);

            server2.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(server.IsConnected);
            Assert.False(server2.IsConnected);
            Assert.False(conn.IsConnected);

            // should reconnect within 1 keepalive interval
            conn.AllowConnect = true;
            Log("Waiting for reconnect");
            await UntilConditionAsync(TimeSpan.FromSeconds(2), () => conn.IsConnected).ForAwait();

            Assert.True(conn.IsConnected);
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    [Fact]
    public async Task FastNoticesFailOnConnectingAsyncCompletion()
    {
        try
        {
            await using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false);
            conn.RawConfig.ReconnectRetryPolicy = new LinearRetry(200);

            var db = conn.GetDatabase();
            await db.PingAsync();

            var server = conn.GetServer(conn.GetEndPoints()[0]);
            var server2 = conn.GetServer(conn.GetEndPoints()[1]);

            conn.AllowConnect = false;

            // muxer.IsConnected is true of *any* are connected, simulate failure for all cases.
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(server.IsConnected);
            Assert.True(server2.IsConnected);
            Assert.True(conn.IsConnected);

            server2.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(server.IsConnected);
            Assert.False(server2.IsConnected);
            Assert.False(conn.IsConnected);

            // should reconnect within 1 keepalive interval
            conn.AllowConnect = true;
            Log("Waiting for reconnect");
            await UntilConditionAsync(TimeSpan.FromSeconds(2), () => conn.IsConnected).ForAwait();

            Assert.True(conn.IsConnected);
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    [Fact]
    public async Task Issue922_ReconnectRaised()
    {
        var config = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        config.AbortOnConnectFail = true;
        config.KeepAlive = 1;
        config.SyncTimeout = 1000;
        config.AsyncTimeout = 1000;
        config.ReconnectRetryPolicy = new ExponentialRetry(5000);
        config.AllowAdmin = true;
        config.BacklogPolicy = BacklogPolicy.FailFast;

        int failCount = 0, restoreCount = 0;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        conn.ConnectionFailed += (s, e) =>
        {
            Interlocked.Increment(ref failCount);
            Log($"Connection Failed ({e.ConnectionType}, {e.FailureType}): {e.Exception}");
        };
        conn.ConnectionRestored += (s, e) =>
        {
            Interlocked.Increment(ref restoreCount);
            Log($"Connection Restored ({e.ConnectionType}, {e.FailureType})");
        };

        conn.GetDatabase();
        Assert.Equal(0, Volatile.Read(ref failCount));
        Assert.Equal(0, Volatile.Read(ref restoreCount));

        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
        server.SimulateConnectionFailure(SimulatedFailureType.All);

        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => Volatile.Read(ref failCount) >= 2 && Volatile.Read(ref restoreCount) >= 2);

        // interactive+subscriber = 2
        var failCountSnapshot = Volatile.Read(ref failCount);
        Assert.True(failCountSnapshot >= 2, $"failCount {failCountSnapshot} >= 2");

        var restoreCountSnapshot = Volatile.Read(ref restoreCount);
        Assert.True(restoreCountSnapshot >= 2, $"restoreCount ({restoreCountSnapshot}) >= 2");
    }

    [Fact]
    public async Task ConnectsWhenBeginConnectCompletesSynchronously()
    {
        try
        {
            await using var conn = Create(keepAlive: 1, connectTimeout: 3000);

            var db = conn.GetDatabase();
            await db.PingAsync();

            Assert.True(conn.IsConnected);
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    [Fact]
    public async Task ConnectIncludesSubscriber()
    {
        await using var conn = Create(keepAlive: 1, connectTimeout: 3000, shared: false);

        var db = conn.GetDatabase();
        await db.PingAsync();
        Assert.True(conn.IsConnected);

        foreach (var server in conn.GetServerSnapshot())
        {
            Assert.Equal(PhysicalBridge.State.ConnectedEstablished, server.InteractiveConnectionState);
            Assert.Equal(PhysicalBridge.State.ConnectedEstablished, server.SubscriptionConnectionState);
        }
    }
}
