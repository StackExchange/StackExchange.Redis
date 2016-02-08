using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class ConnectFailTimeout : TestBase
    {
#if DEBUG
        [TestCase]
        public void NoticesConnectFail()
        {
            SetExpectedAmbientFailureCount(-1);
            using (var conn = Create(allowAdmin: true))
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                conn.IgnoreConnect = true;
                conn.ConnectionFailed += (s,a) => {
                    System.Console.WriteLine("Disconnected: " + EndPointCollection.ToString(a.EndPoint));
                };
                conn.ConnectionRestored += (s,a) => {
                    System.Console.WriteLine("Reconnected: " + EndPointCollection.ToString(a.EndPoint));
                };
                server.SimulateConnectionFailure();
                Thread.Sleep(2000);
                try
                {
                    server.Ping();
                    Assert.Fail("Did not expect PING to succeed");
                } catch(RedisConnectionException) { /* expected */ }

                conn.IgnoreConnect = false;
                Thread.Sleep(2000);
                var time = server.Ping();
                System.Console.WriteLine(time);
            }
        }
#endif
    }
}
