using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectingFailDetection : TestBase
    {
        public ConnectingFailDetection(ITestOutputHelper output) : base (output) { }

        protected override string GetConfiguration() => TestConfig.Current.MasterServerAndPort + "," + TestConfig.Current.SlaveServerAndPort;

#if DEBUG
        [Fact]
        public async Task FastNoticesFailOnConnectingSyncComlpetion()
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

        [Fact]
        public async Task FastNoticesFailOnConnectingAsyncComlpetion()
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
#endif
    }
}
