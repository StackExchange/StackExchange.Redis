using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class ConnectionShutdown : TestBase
    {
        protected override string GetConfiguration()
        {
            return PrimaryServer + ":" + PrimaryPortString;
        }

        [Test]
        public void ShutdownRaisesConnectionFailedAndRestore()
        {
            using(var conn = Create(allowAdmin: true))
            {
                int failed = 0, restored = 0;
                Stopwatch watch = Stopwatch.StartNew();
                conn.ConnectionFailed += (sender,args)=>
                {
                    Console.WriteLine(watch.Elapsed + ": failed: " + EndPointCollection.ToString(args.EndPoint) + "/" + args.ConnectionType);
                    Interlocked.Increment(ref failed);
                };
                conn.ConnectionRestored += (sender, args) =>
                {
                    Console.WriteLine(watch.Elapsed + ": restored: " + EndPointCollection.ToString(args.EndPoint) + "/" + args.ConnectionType);
                    Interlocked.Increment(ref restored);
                };
                var db = conn.GetDatabase();
                db.Ping();
                Assert.AreEqual(0, Interlocked.CompareExchange(ref failed, 0, 0));
                Assert.AreEqual(0, Interlocked.CompareExchange(ref restored, 0, 0));

#if DEBUG
                conn.AllowConnect = false;
                var server = conn.GetServer(PrimaryServer, PrimaryPort);

                SetExpectedAmbientFailureCount(2);
                server.SimulateConnectionFailure();

                db.Ping(CommandFlags.FireAndForget);
                Thread.Sleep(250);
                Assert.AreEqual(2, Interlocked.CompareExchange(ref failed, 0, 0), "failed");
                Assert.AreEqual(0, Interlocked.CompareExchange(ref restored, 0, 0), "restored");
                conn.AllowConnect = true;
                db.Ping(CommandFlags.FireAndForget);
                Thread.Sleep(1500);
                Assert.AreEqual(2, Interlocked.CompareExchange(ref failed, 0, 0), "failed");
                Assert.AreEqual(2, Interlocked.CompareExchange(ref restored, 0, 0), "restored");
#endif
                watch.Stop();
            }

        }
    }
}
