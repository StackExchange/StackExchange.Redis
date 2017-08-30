using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectFailTimeout : TestBase
    {
        public ConnectFailTimeout(ITestOutputHelper output) : base (output) { }

#if DEBUG
        [Fact]
        public void NoticesConnectFail()
        {
            SetExpectedAmbientFailureCount(-1);
            using (var conn = Create(allowAdmin: true))
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                conn.IgnoreConnect = true;
                conn.ConnectionFailed += (s, a) =>
                    Output.WriteLine("Disconnected: " + EndPointCollection.ToString(a.EndPoint));
                conn.ConnectionRestored += (s, a) =>
                    Output.WriteLine("Reconnected: " + EndPointCollection.ToString(a.EndPoint));
                server.SimulateConnectionFailure();
                Thread.Sleep(2000);
                try
                {
                    server.Ping();
                    Assert.True(false, "Did not expect PING to succeed");
                }
                catch (RedisConnectionException) { /* expected */ }

                conn.IgnoreConnect = false;
                Thread.Sleep(2000);
                var time = server.Ping();
                Output.WriteLine(time.ToString());
            }
        }
#endif
    }
}
