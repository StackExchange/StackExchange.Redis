using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Config : TestBase
    {

        [Test]
        public void VerifyReceiveConfigChangeBroadcast()
        {

            var config = this.GetConfiguration();
            using (var sender = Create(allowAdmin: true))
            using (var receiver = Create(syncTimeout: 2000))
            {
                int total = 0;
                receiver.ConfigurationChangedBroadcast += (s, a) =>
                {
                    Console.WriteLine("Config changed: " + (a.EndPoint == null ? "(none)" : a.EndPoint.ToString()));
                    Interlocked.Increment(ref total);
                };
                Thread.Sleep(500);
                // send a reconfigure/reconnect message
                long count = sender.PublishReconfigure();
                GetServer(receiver).Ping();
                GetServer(receiver).Ping();
                Assert.IsTrue(count == -1 || count >= 2, "subscribers");
                Assert.IsTrue(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (1st)");

                Interlocked.Exchange(ref total, 0);

                // and send a second time via a re-master operation
                var server = GetServer(sender);
                if (server.IsSlave) Assert.Inconclusive("didn't expect a slave");
                server.MakeMaster(ReplicationChangeOptions.Broadcast);
                Thread.Sleep(100);
                GetServer(receiver).Ping();
                GetServer(receiver).Ping();
                Assert.IsTrue(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (2nd)");
            }
        }

        [Test]
        public void TalkToNonsenseServer()
        {
            var config = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                EndPoints =
                {
                    { "127.0.0.1:1234" }
                },
                ConnectTimeout = 200
            };
            var log = new StringWriter();
            using(var conn = ConnectionMultiplexer.Connect(config, log))
            {
                Console.WriteLine(log);
                Assert.IsFalse(conn.IsConnected);
            }
        }

        [Test]
        public void TestManaulHeartbeat()
        {
            using (var muxer = Create(keepAlive: 2))
            {
                var conn = muxer.GetDatabase();
                conn.Ping();

                var before = muxer.OperationCount;

                Console.WriteLine("sleeping to test heartbeat...");
                Thread.Sleep(TimeSpan.FromSeconds(5));

                var after = muxer.OperationCount;

                Assert.IsTrue(after >= before + 4);

            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(200)]
        public void GetSlowlog(int count)
        {
            using(var muxer = Create(allowAdmin: true))
            {
                var rows = GetServer(muxer).SlowlogGet(count);
                Assert.IsNotNull(rows);
            }
        }
        [Test]
        public void ClearSlowlog()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                GetServer(muxer).SlowlogReset();
            }
        }

        [Test]
        public void ClientName()
        {
            using (var muxer = Create(clientName: "Test Rig", allowAdmin: true))
            {
                Assert.AreEqual("Test Rig", muxer.ClientName);

                var conn = muxer.GetDatabase();
                conn.Ping();
#if DEBUG
                var name = GetServer(muxer).ClientGetName();
                Assert.AreEqual("TestRig", name);
#endif
            }
        }

        [Test]
        public void DefaultClientName()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                Assert.AreEqual(Environment.MachineName, muxer.ClientName);
                var conn = muxer.GetDatabase();
                conn.Ping();
#if DEBUG
                var name = GetServer(muxer).ClientGetName();
                Assert.AreEqual(Environment.MachineName, name);
#endif
            }
        }
        
        [Test]
        public void ReadConfigWithConfigDisabled()
        {
            using (var muxer = Create(allowAdmin: true, disabledCommands: new[] { "config", "info" }))
            {
                var conn = GetServer(muxer);
                Assert.Throws<RedisCommandException>(() =>
                {
                    var all = conn.ConfigGet();
                },
                "This operation has been disabled in the command-map and cannot be used: CONFIG");
            }
        }
        [Test]
        public void ReadConfig()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                Console.WriteLine("about to get config");
                var conn = GetServer(muxer);
                var all = conn.ConfigGet();
                Assert.IsTrue(all.Length > 0, "any");

#if !CORE_CLR
                var pairs = all.ToDictionary(x => (string)x.Key, x => (string)x.Value, StringComparer.InvariantCultureIgnoreCase);
#else
                var pairs = all.ToDictionary(x => (string)x.Key, x => (string)x.Value, StringComparer.OrdinalIgnoreCase);
#endif

                Assert.AreEqual(all.Length, pairs.Count);
                Assert.IsTrue(pairs.ContainsKey("timeout"), "timeout");
                var val = int.Parse(pairs["timeout"]);

                Assert.IsTrue(pairs.ContainsKey("port"), "port");
                val = int.Parse(pairs["port"]);
                Assert.AreEqual(PrimaryPort, val);
            }
        }

        [Test]
        public async System.Threading.Tasks.Task TestConfigureAsync()
        {
            using(var muxer = Create())
            {
                Thread.Sleep(1000);
                Debug.WriteLine("About to reconfigure.....");
                await muxer.ConfigureAsync().ConfigureAwait(false);
                Debug.WriteLine("Reconfigured");
            }
        }
        [Test]
        public void TestConfigureSync()
        {
            using (var muxer = Create())
            {
                Thread.Sleep(1000);
                Debug.WriteLine("About to reconfigure.....");
                muxer.Configure();
                Debug.WriteLine("Reconfigured");
            }
        }

        [Test]
        public void GetTime()
        {
            using (var muxer = Create())
            {
                var server = GetServer(muxer);
                var serverTime = server.Time();
                Console.WriteLine(serverTime);
                var delta = Math.Abs((DateTime.UtcNow - serverTime).TotalSeconds);

                Assert.IsTrue(delta < 5);
            }
        }

        [Test]
        public void DebugObject()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var db = muxer.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringIncrement(key, flags: CommandFlags.FireAndForget);
                var debug = (string)db.DebugObject(key);
                Assert.IsNotNull(debug);
                Assert.IsTrue(debug.Contains("encoding:int serializedlength:2"));
            }
        }

        [Test]
        public void GetInfo()
        {
            using(var muxer = Create(allowAdmin: true))
            {
                var server = GetServer(muxer);
                var info1 = server.Info();
                Assert.IsTrue(info1.Length > 5);
                Console.WriteLine("All sections");
                foreach(var group in info1)
                {
                    Console.WriteLine(group.Key);
                }
                var first = info1.First();
                Console.WriteLine("Full info for: " + first.Key);
                foreach (var setting in first)
                {
                    Console.WriteLine("{0}  ==>  {1}", setting.Key, setting.Value);
                }

                var info2 = server.Info("cpu");
                Assert.AreEqual(1, info2.Length);
                var cpu = info2.Single();
                Assert.IsTrue(cpu.Count() > 2);
                Assert.AreEqual("CPU", cpu.Key);
                Assert.IsTrue(cpu.Any(x => x.Key == "used_cpu_sys"));
                Assert.IsTrue(cpu.Any(x => x.Key == "used_cpu_user"));
            }
        }

        [Test]
        public void GetInfoRaw()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var server = GetServer(muxer);
                var info = server.InfoRaw();
                Assert.IsTrue(info.Contains("used_cpu_sys"));
                Assert.IsTrue(info.Contains("used_cpu_user"));
            }
        }

        [Test]
        public void GetClients()
        {
            var name = Guid.NewGuid().ToString();
            using (var muxer = Create(clientName:  name, allowAdmin: true))
            {
                var server = GetServer(muxer);
                var clients = server.ClientList();
                Assert.IsTrue(clients.Length > 0, "no clients"); // ourselves!
                Assert.IsTrue(clients.Any(x => x.Name == name), "expected: " + name);
            }
        }

        [Test]
        public void SlowLog()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var server = GetServer(muxer);
                var slowlog = server.SlowlogGet();
                server.SlowlogReset();
            }
        }

        [Test]
        public void TestAutomaticHeartbeat()
        {
            RedisValue oldTimeout = RedisValue.Null;
            using (var configMuxer = Create(allowAdmin: true))
            {
                try
                {
                    var conn = configMuxer.GetDatabase();
                    var srv = GetServer(configMuxer);
                    oldTimeout = srv.ConfigGet("timeout")[0].Value;
                    srv.ConfigSet("timeout", 5);

                    using(var innerMuxer = Create())
                    {
                        var innerConn = innerMuxer.GetDatabase();
                        innerConn.Ping(); // need to wait to pick up configuration etc

                        var before = innerMuxer.OperationCount;

                        Console.WriteLine("sleeping to test heartbeat...");
                        Thread.Sleep(TimeSpan.FromSeconds(8));

                        var after = innerMuxer.OperationCount;
                        Assert.IsTrue(after >= before + 4);

                    }
                }
                finally
                {
                    if(!oldTimeout.IsNull)
                    {
                        var srv = GetServer(configMuxer);
                        srv.ConfigSet("timeout", oldTimeout);
                    }
                }
            }
        }
    }
}
