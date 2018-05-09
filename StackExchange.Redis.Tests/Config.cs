using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class Config : TestBase
    {
        public Config(ITestOutputHelper output) : base (output) { }

        [Fact]
        public async Task VerifyReceiveConfigChangeBroadcast()
        {
            var config = GetConfiguration();
            using (var sender = Create(allowAdmin: true))
            using (var receiver = Create(syncTimeout: 2000))
            {
                int total = 0;
                receiver.ConfigurationChangedBroadcast += (s, a) =>
                {
                    Output.WriteLine("Config changed: " + (a.EndPoint == null ? "(none)" : a.EndPoint.ToString()));
                    Interlocked.Increment(ref total);
                };
                // send a reconfigure/reconnect message
                long count = sender.PublishReconfigure();
                GetServer(receiver).Ping();
                GetServer(receiver).Ping();
                await Task.Delay(10).ConfigureAwait(false);
                Assert.True(count == -1 || count >= 2, "subscribers");
                Assert.True(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (1st)");

                Interlocked.Exchange(ref total, 0);

                // and send a second time via a re-master operation
                var server = GetServer(sender);
                if (server.IsSlave) Skip.Inconclusive("didn't expect a slave");
                server.MakeMaster(ReplicationChangeOptions.Broadcast);
                await Task.Delay(100).ConfigureAwait(false);
                GetServer(receiver).Ping();
                GetServer(receiver).Ping();
                Assert.True(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (2nd)");
            }
        }

        [Fact]
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
            using (var conn = ConnectionMultiplexer.Connect(config, log))
            {
                Output.WriteLine(log.ToString());
                Assert.False(conn.IsConnected);
            }
        }

        [Fact]
        public void TestManaulHeartbeat()
        {
            using (var muxer = Create(keepAlive: 2))
            {
                var conn = muxer.GetDatabase();
                conn.Ping();

                var before = muxer.OperationCount;

                Output.WriteLine("sleeping to test heartbeat...");
                Thread.Sleep(TimeSpan.FromSeconds(5));

                var after = muxer.OperationCount;

                Assert.True(after >= before + 4);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(200)]
        public void GetSlowlog(int count)
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var rows = GetAnyMaster(muxer).SlowlogGet(count);
                Assert.NotNull(rows);
            }
        }

        [Fact]
        public void ClearSlowlog()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                GetAnyMaster(muxer).SlowlogReset();
            }
        }

        [Fact]
        public void ClientName()
        {
            using (var muxer = Create(clientName: "Test Rig", allowAdmin: true))
            {
                Assert.Equal("Test Rig", muxer.ClientName);

                var conn = muxer.GetDatabase();
                conn.Ping();
#if DEBUG
                var name = GetAnyMaster(muxer).ClientGetName();
                Assert.Equal("TestRig", name);
#endif
            }
        }

        [Fact]
        public void DefaultClientName()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                Assert.Equal(Environment.MachineName, muxer.ClientName);
                var conn = muxer.GetDatabase();
                conn.Ping();
#if DEBUG
                var name = GetAnyMaster(muxer).ClientGetName();
                Assert.Equal(Environment.MachineName, name);
#endif
            }
        }

        [Fact]
        public void ReadConfigWithConfigDisabled()
        {
            using (var muxer = Create(allowAdmin: true, disabledCommands: new[] { "config", "info" }))
            {
                var conn = GetAnyMaster(muxer);
                var ex = Assert.Throws<RedisCommandException>(() => conn.ConfigGet());
                Assert.Equal("This operation has been disabled in the command-map and cannot be used: CONFIG", ex.Message);
            }
        }

        [Fact]
        public void ReadConfig()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                Output.WriteLine("about to get config");
                var conn = GetAnyMaster(muxer);
                var all = conn.ConfigGet();
                Assert.True(all.Length > 0, "any");

#if !NETCOREAPP1_0
                var pairs = all.ToDictionary(x => (string)x.Key, x => (string)x.Value, StringComparer.InvariantCultureIgnoreCase);
#else
                var pairs = all.ToDictionary(x => (string)x.Key, x => (string)x.Value, StringComparer.OrdinalIgnoreCase);
#endif

                Assert.Equal(all.Length, pairs.Count);
                Assert.True(pairs.ContainsKey("timeout"), "timeout");
                var val = int.Parse(pairs["timeout"]);

                Assert.True(pairs.ContainsKey("port"), "port");
                val = int.Parse(pairs["port"]);
                Assert.Equal(TestConfig.Current.MasterPort, val);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task TestConfigureAsync()
        {
            using (var muxer = Create())
            {
                Thread.Sleep(1000);
                Debug.WriteLine("About to reconfigure.....");
                await muxer.ConfigureAsync().ForAwait();
                Debug.WriteLine("Reconfigured");
            }
        }

        [Fact]
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

        [Fact]
        public void GetTime()
        {
            using (var muxer = Create())
            {
                var server = GetAnyMaster(muxer);
                var serverTime = server.Time();
                Output.WriteLine(serverTime.ToString());
                var delta = Math.Abs((DateTime.UtcNow - serverTime).TotalSeconds);

                Assert.True(delta < 5);
            }
        }

        [Fact]
        public void DebugObject()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var db = muxer.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringIncrement(key, flags: CommandFlags.FireAndForget);
                var debug = (string)db.DebugObject(key);
                Assert.NotNull(debug);
                Assert.Contains("encoding:int serializedlength:2", debug);
            }
        }

        [Fact]
        public void GetInfo()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var server = GetAnyMaster(muxer);
                var info1 = server.Info();
                Assert.True(info1.Length > 5);
                Output.WriteLine("All sections");
                foreach (var group in info1)
                {
                    Output.WriteLine(group.Key);
                }
                var first = info1[0];
                Output.WriteLine("Full info for: " + first.Key);
                foreach (var setting in first)
                {
                    Output.WriteLine("{0}  ==>  {1}", setting.Key, setting.Value);
                }

                var info2 = server.Info("cpu");
                Assert.Single(info2);
                var cpu = info2.Single();
                var cpuCount = cpu.Count();
                Assert.True(cpuCount > 2);
                Assert.Equal("CPU", cpu.Key);
                Assert.Contains(cpu, x => x.Key == "used_cpu_sys");
                Assert.Contains(cpu, x => x.Key == "used_cpu_user");
            }
        }

        [Fact]
        public void GetInfoRaw()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var server = GetAnyMaster(muxer);
                var info = server.InfoRaw();
                Assert.Contains("used_cpu_sys", info);
                Assert.Contains("used_cpu_user", info);
            }
        }

        [Fact]
        public void GetClients()
        {
            var name = Guid.NewGuid().ToString();
            using (var muxer = Create(clientName: name, allowAdmin: true))
            {
                var server = GetAnyMaster(muxer);
                var clients = server.ClientList();
                Assert.True(clients.Length > 0, "no clients"); // ourselves!
                Assert.True(clients.Any(x => x.Name == name), "expected: " + name);
            }
        }

        [Fact]
        public void SlowLog()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var server = GetAnyMaster(muxer);
                var slowlog = server.SlowlogGet();
                server.SlowlogReset();
            }
        }

        [Fact]
        public void TestAutomaticHeartbeat()
        {
            RedisValue oldTimeout = RedisValue.Null;
            using (var configMuxer = Create(allowAdmin: true))
            {
                try
                {
                    var conn = configMuxer.GetDatabase();
                    var srv = GetAnyMaster(configMuxer);
                    oldTimeout = srv.ConfigGet("timeout")[0].Value;
                    srv.ConfigSet("timeout", 5);

                    using (var innerMuxer = Create())
                    {
                        var innerConn = innerMuxer.GetDatabase();
                        innerConn.Ping(); // need to wait to pick up configuration etc

                        var before = innerMuxer.OperationCount;

                        Output.WriteLine("sleeping to test heartbeat...");
                        Thread.Sleep(TimeSpan.FromSeconds(8));

                        var after = innerMuxer.OperationCount;
                        Assert.True(after >= before + 4);
                    }
                }
                finally
                {
                    if (!oldTimeout.IsNull)
                    {
                        var srv = GetAnyMaster(configMuxer);
                        srv.ConfigSet("timeout", oldTimeout);
                    }
                }
            }
        }
    }
}
