using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectFailTimeout : TestBase
    {
        public ConnectFailTimeout(ITestOutputHelper output) : base (output) { }

#if DEBUG
        [Fact]
        public async Task NoticesConnectFail()
        {
            SetExpectedAmbientFailureCount(-1);
            using (var conn = Create(allowAdmin: true))
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                conn.ConnectionFailed += (s, a) =>
                    Log("Disconnected: " + EndPointCollection.ToString(a.EndPoint));
                conn.ConnectionRestored += (s, a) =>
                    Log("Reconnected: " + EndPointCollection.ToString(a.EndPoint));

                // No need to delay, we're going to try a disconnected connection immediately so it'll fail...
                conn.IgnoreConnect = true;
                server.SimulateConnectionFailure();
                conn.IgnoreConnect = false;
                Assert.Throws<RedisConnectionException>(() => server.Ping());

                // Heartbeat should reconnect by now
                await Task.Delay(5000).ConfigureAwait(false);

                var time = server.Ping();
                Log(time.ToString());
            }
        }
#endif
    }
}
