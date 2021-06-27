using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectingFailDetection : TestBase
    {
        public ConnectingFailDetection(ITestOutputHelper output) : base (output) { }

        protected override string GetConfiguration() => TestConfig.Current.MasterServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

#if DEBUG
        [Fact]
        public async Task FastNoticesFailOnConnectingSyncCompletion()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);
                    var server2 = muxer.GetServer(muxer.GetEndPoints()[1]);

                    muxer.AllowConnect = false;

                    // muxer.IsConnected is true of *any* are connected, simulate failure for all cases.
                    server.SimulateConnectionFailure();
                    Assert.False(server.IsConnected);
                    Assert.True(server2.IsConnected);
                    Assert.True(muxer.IsConnected);

                    server2.SimulateConnectionFailure();
                    Assert.False(server.IsConnected);
                    Assert.False(server2.IsConnected);
                    Assert.False(muxer.IsConnected);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    Log("Waiting for reconnect");
                    await Task.Delay(2000).ForAwait();

                    Assert.True(muxer.IsConnected);
                }
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
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);
                    var server2 = muxer.GetServer(muxer.GetEndPoints()[1]);

                    muxer.AllowConnect = false;

                    // muxer.IsConnected is true of *any* are connected, simulate failure for all cases.
                    server.SimulateConnectionFailure();
                    Assert.False(server.IsConnected);
                    Assert.True(server2.IsConnected);
                    Assert.True(muxer.IsConnected);

                    server2.SimulateConnectionFailure();
                    Assert.False(server.IsConnected);
                    Assert.False(server2.IsConnected);
                    Assert.False(muxer.IsConnected);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    Log("Waiting for reconnect");
                    await Task.Delay(2000).ForAwait();

                    Assert.True(muxer.IsConnected);
                }
            }
            finally
            {
                ClearAmbientFailures();
            }
        }

        [Fact]
        public async Task Issue922_ReconnectRaised()
        {
            var config = ConfigurationOptions.Parse(TestConfig.Current.MasterServerAndPort);
            config.AbortOnConnectFail = true;
            config.KeepAlive = 10;
            config.SyncTimeout = 1000;
            config.ReconnectRetryPolicy = new ExponentialRetry(5000);
            config.AllowAdmin = true;

            int failCount = 0, restoreCount = 0;

            using (var muxer = ConnectionMultiplexer.Connect(config))
            {
                muxer.ConnectionFailed += delegate { Interlocked.Increment(ref failCount); };
                muxer.ConnectionRestored += delegate { Interlocked.Increment(ref restoreCount); };

                muxer.GetDatabase();
                Assert.Equal(0, Volatile.Read(ref failCount));
                Assert.Equal(0, Volatile.Read(ref restoreCount));

                var server = muxer.GetServer(TestConfig.Current.MasterServerAndPort);
                server.SimulateConnectionFailure();

                await UntilCondition(TimeSpan.FromSeconds(10), () => Volatile.Read(ref failCount) + Volatile.Read(ref restoreCount) == 4);
                // interactive+subscriber = 2
                Assert.Equal(2, Volatile.Read(ref failCount));
                Assert.Equal(2, Volatile.Read(ref restoreCount));
            }
        }
#endif

        [Fact]
        public void ConnectsWhenBeginConnectCompletesSynchronously()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 3000))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    Assert.True(muxer.IsConnected);
                }
            }
            finally
            {
                ClearAmbientFailures();
            }
        }
    }
}
