using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectingFailDetection : TestBase
    {
        public ConnectingFailDetection(ITestOutputHelper output) : base (output) { }

#if DEBUG
        [Fact]
        public void FastNoticesFailOnConnectingSyncComlpetion()
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
                    SocketManager.ConnectCompletionType = CompletionType.Sync;

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
                    Output.WriteLine("Waiting for reconnect");
                    Thread.Sleep(2000);

                    Assert.True(muxer.IsConnected);
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
                ClearAmbientFailures();
            }
        }

        [Fact]
        public void ConnectsWhenBeginConnectCompletesSynchronously()
        {
            try
            {
                SocketManager.ConnectCompletionType = CompletionType.Sync;

                using (var muxer = Create(keepAlive: 1, connectTimeout: 3000))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    Assert.True(muxer.IsConnected);
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
                ClearAmbientFailures();
            }
        }

        [Fact]
        public void FastNoticesFailOnConnectingAsyncComlpetion()
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
                    SocketManager.ConnectCompletionType = CompletionType.Async;

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
                    Output.WriteLine("Waiting for reconnect");
                    Thread.Sleep(2000);

                    Assert.True(muxer.IsConnected);
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
                ClearAmbientFailures();
            }
        }

        [Fact]
        public void ReconnectsOnStaleConnection()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 3000))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    Assert.True(muxer.IsConnected);

                    PhysicalConnection.EmulateStaleConnection = true;
                    Thread.Sleep(500);
                    Assert.False(muxer.IsConnected);

                    PhysicalConnection.EmulateStaleConnection = false;
                    Thread.Sleep(1000);
                    Assert.True(muxer.IsConnected);
                }
            }
            finally
            {
                PhysicalConnection.EmulateStaleConnection = false;
                ClearAmbientFailures();
            }
        }
#endif
    }
}
