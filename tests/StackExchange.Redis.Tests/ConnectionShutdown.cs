using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectionShutdown : TestBase
    {
        protected override string GetConfiguration() => TestConfig.Current.MasterServerAndPort;
        public ConnectionShutdown(ITestOutputHelper output) : base(output) { }

        [Fact(Skip = "Unfriendly")]
        public async Task ShutdownRaisesConnectionFailedAndRestore()
        {
            using (var conn = Create(allowAdmin: true))
            {
                int failed = 0, restored = 0;
                Stopwatch watch = Stopwatch.StartNew();
                conn.ConnectionFailed += (sender, args) =>
                {
                    Log(watch.Elapsed + ": failed: " + EndPointCollection.ToString(args.EndPoint) + "/" + args.ConnectionType + ": " + args);
                    Interlocked.Increment(ref failed);
                };
                conn.ConnectionRestored += (sender, args) =>
                {
                    Log(watch.Elapsed + ": restored: " + EndPointCollection.ToString(args.EndPoint) + "/" + args.ConnectionType + ": " + args);
                    Interlocked.Increment(ref restored);
                };
                var db = conn.GetDatabase();
                db.Ping();
                Assert.Equal(0, Interlocked.CompareExchange(ref failed, 0, 0));
                Assert.Equal(0, Interlocked.CompareExchange(ref restored, 0, 0));
                await Task.Delay(1).ForAwait(); // To make compiler happy in Release

                conn.AllowConnect = false;
                var server = conn.GetServer(TestConfig.Current.MasterServer, TestConfig.Current.MasterPort);

                SetExpectedAmbientFailureCount(2);
                server.SimulateConnectionFailure();

                db.Ping(CommandFlags.FireAndForget);
                await Task.Delay(250).ForAwait();
                Assert.Equal(2, Interlocked.CompareExchange(ref failed, 0, 0));
                Assert.Equal(0, Interlocked.CompareExchange(ref restored, 0, 0));
                conn.AllowConnect = true;
                db.Ping(CommandFlags.FireAndForget);
                await Task.Delay(1500).ForAwait();
                Assert.Equal(2, Interlocked.CompareExchange(ref failed, 0, 0));
                Assert.Equal(2, Interlocked.CompareExchange(ref restored, 0, 0));
                watch.Stop();
            }
        }
    }
}
