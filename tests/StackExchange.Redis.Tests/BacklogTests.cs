using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class BacklogTests : TestBase
    {
        public BacklogTests(ITestOutputHelper output) : base (output) { }

        protected override string GetConfiguration() => TestConfig.Current.MasterServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

        [Fact]
        public async Task BasicTest()
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
    }
}
