﻿using System;
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

        [Fact]
        public async Task FastNoticesFailOnConnectingSyncCompletion()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);
                    var server2 = muxer.GetServer(muxer.GetEndPoints()[1]);

                    muxer.AllowConnect = false;

                    // muxer.IsConnected is true of *any* are connected, simulate failure for all cases.
                    server.SimulateConnectionFailure(SimulatedFailureType.All);
                    Assert.False(server.IsConnected);
                    Assert.True(server2.IsConnected);
                    Assert.True(muxer.IsConnected);

                    server2.SimulateConnectionFailure(SimulatedFailureType.All);
                    Assert.False(server.IsConnected);
                    Assert.False(server2.IsConnected);
                    Assert.False(muxer.IsConnected);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    Log("Waiting for reconnect");
                    await UntilCondition(TimeSpan.FromSeconds(2), () => muxer.IsConnected).ForAwait();

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
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);
                    var server2 = muxer.GetServer(muxer.GetEndPoints()[1]);

                    muxer.AllowConnect = false;

                    // muxer.IsConnected is true of *any* are connected, simulate failure for all cases.
                    server.SimulateConnectionFailure(SimulatedFailureType.All);
                    Assert.False(server.IsConnected);
                    Assert.True(server2.IsConnected);
                    Assert.True(muxer.IsConnected);

                    server2.SimulateConnectionFailure(SimulatedFailureType.All);
                    Assert.False(server.IsConnected);
                    Assert.False(server2.IsConnected);
                    Assert.False(muxer.IsConnected);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    Log("Waiting for reconnect");
                    await UntilCondition(TimeSpan.FromSeconds(2), () => muxer.IsConnected).ForAwait();

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
            config.KeepAlive = 1;
            config.SyncTimeout = 1000;
            config.AsyncTimeout = 1000;
            config.ReconnectRetryPolicy = new ExponentialRetry(5000);
            config.AllowAdmin = true;
            config.BacklogPolicy = BacklogPolicy.FailFast;

            int failCount = 0, restoreCount = 0;

            using (var muxer = ConnectionMultiplexer.Connect(config))
            {
                muxer.ConnectionFailed += (s, e) =>
                {
                    Interlocked.Increment(ref failCount);
                    Log($"Connection Failed ({e.ConnectionType}, {e.FailureType}): {e.Exception}");
                };
                muxer.ConnectionRestored += (s, e) =>
                {
                    Interlocked.Increment(ref restoreCount);
                    Log($"Connection Restored ({e.ConnectionType}, {e.FailureType})");
                };

                muxer.GetDatabase();
                Assert.Equal(0, Volatile.Read(ref failCount));
                Assert.Equal(0, Volatile.Read(ref restoreCount));

                var server = muxer.GetServer(TestConfig.Current.MasterServerAndPort);
                server.SimulateConnectionFailure(SimulatedFailureType.All);

                await UntilCondition(TimeSpan.FromSeconds(10), () => Volatile.Read(ref failCount) >= 2 && Volatile.Read(ref restoreCount) >= 2);

                // interactive+subscriber = 2
                var failCountSnapshot = Volatile.Read(ref failCount);
                Assert.True(failCountSnapshot >= 2, $"failCount {failCountSnapshot} >= 2");

                var restoreCountSnapshot = Volatile.Read(ref restoreCount);
                Assert.True(restoreCountSnapshot >= 2, $"restoreCount ({restoreCountSnapshot}) >= 2");
            }
        }

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
