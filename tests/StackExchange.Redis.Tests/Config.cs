using System;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Config : TestBase
    {
        public Config(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void SslProtocols_SingleValue()
        {
            var options = ConfigurationOptions.Parse("myhost,sslProtocols=Tls11");
            Assert.Equal(SslProtocols.Tls11, options.SslProtocols.Value);
        }

        [Fact]
        public void SslProtocols_MultipleValues()
        {
            var options = ConfigurationOptions.Parse("myhost,sslProtocols=Tls11|Tls12");
            Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, options.SslProtocols.Value);
        }

        [Fact]
        public void SslProtocols_UsingIntegerValue()
        {
            // The below scenario is for cases where the *targeted*
            // .NET framework version (e.g. .NET 4.0) doesn't define an enum value (e.g. Tls11)
            // but the OS has been patched with support
            const int integerValue = (int)(SslProtocols.Tls11 | SslProtocols.Tls12);
            var options = ConfigurationOptions.Parse("myhost,sslProtocols=" + integerValue);
            Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, options.SslProtocols.Value);
        }

        [Fact]
        public void SslProtocols_InvalidValue()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationOptions.Parse("myhost,sslProtocols=InvalidSslProtocol"));
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzure()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.windows.net");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsForAzureWhenSpecified()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.windows.net,abortConnect=true, version=2.1.1");
            Assert.True(options.DefaultVersion.Equals(new Version(2, 1, 1)));
            Assert.True(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureChina()
        {
            // added a few upper case chars to validate comparison
            var options = ConfigurationOptions.Parse("contoso.REDIS.CACHE.chinacloudapi.cn");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureGermany()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.cloudapi.de");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureUSGov()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.usgovcloudapi.net");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForNonAzure()
        {
            var options = ConfigurationOptions.Parse("redis.contoso.com");
            Assert.True(options.DefaultVersion.Equals(new Version(2, 0, 0)));
            Assert.True(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultWhenNoEndpointsSpecifiedYet()
        {
            var options = new ConfigurationOptions();
            Assert.True(options.DefaultVersion.Equals(new Version(2, 0, 0)));
            Assert.True(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsSyncTimeout()
        {
            // Default check
            var options = new ConfigurationOptions();
            Assert.Equal(5000, options.SyncTimeout);

            options = ConfigurationOptions.Parse("syncTimeout=20");
            Assert.Equal(20, options.SyncTimeout);
        }

        [Theory]
        [InlineData("127.1:6379", AddressFamily.InterNetwork, "127.0.0.1", 6379)]
        [InlineData("127.0.0.1:6379", AddressFamily.InterNetwork, "127.0.0.1", 6379)]
        [InlineData("2a01:9820:1:24::1:1:6379", AddressFamily.InterNetworkV6, "2a01:9820:1:24:0:1:1:6379", 0)]
        [InlineData("[2a01:9820:1:24::1:1]:6379", AddressFamily.InterNetworkV6, "2a01:9820:1:24::1:1", 6379)]
        public void ConfigurationOptionsIPv6Parsing(string configString, AddressFamily family, string address, int port)
        {
            var options = ConfigurationOptions.Parse(configString);
            Assert.Single(options.EndPoints);
            var ep = Assert.IsType<IPEndPoint>(options.EndPoints[0]);
            Assert.Equal(family, ep.AddressFamily);
            Assert.Equal(address, ep.Address.ToString());
            Assert.Equal(port, ep.Port);
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
                Log(log.ToString());
                Assert.False(conn.IsConnected);
            }
        }

        [Fact]
        public async Task TestManaulHeartbeat()
        {
            using (var muxer = Create(keepAlive: 2))
            {
                var conn = muxer.GetDatabase();
                conn.Ping();

                var before = muxer.OperationCount;

                Log("sleeping to test heartbeat...");
                await Task.Delay(5000).ForAwait();

                var after = muxer.OperationCount;

                Assert.True(after >= before + 2, $"after: {after}, before: {before}");
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

                var name = (string)GetAnyMaster(muxer).Execute("CLIENT", "GETNAME");
                Assert.Equal("TestRig", name);
            }
        }

        [Fact]
        public void DefaultClientName()
        {
            using (var muxer = Create(allowAdmin: true, caller: null)) // force default naming to kick in
            {
                Assert.Equal(Environment.MachineName, muxer.ClientName);
                var conn = muxer.GetDatabase();
                conn.Ping();

                var name = (string)GetAnyMaster(muxer).Execute("CLIENT", "GETNAME");
                Assert.Equal(Environment.MachineName, name);
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
                Log("about to get config");
                var conn = GetAnyMaster(muxer);
                var all = conn.ConfigGet();
                Assert.True(all.Length > 0, "any");

                var pairs = all.ToDictionary(x => (string)x.Key, x => (string)x.Value, StringComparer.InvariantCultureIgnoreCase);

                Assert.Equal(all.Length, pairs.Count);
                Assert.True(pairs.ContainsKey("timeout"), "timeout");
                var val = int.Parse(pairs["timeout"]);

                Assert.True(pairs.ContainsKey("port"), "port");
                val = int.Parse(pairs["port"]);
                Assert.Equal(TestConfig.Current.MasterPort, val);
            }
        }

        [Fact]
        public void GetTime()
        {
            using (var muxer = Create())
            {
                var server = GetAnyMaster(muxer);
                var serverTime = server.Time();
                Log(serverTime.ToString());
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
                Log("All sections");
                foreach (var group in info1)
                {
                    Log(group.Key);
                }
                var first = info1[0];
                Log("Full info for: " + first.Key);
                foreach (var setting in first)
                {
                    Log("{0}  ==>  {1}", setting.Key, setting.Value);
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
        public async Task TestAutomaticHeartbeat()
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

                        Log("sleeping to test heartbeat...");
                        await Task.Delay(8000).ForAwait();

                        var after = innerMuxer.OperationCount;
                        Assert.True(after >= before + 2, $"after: {after}, before: {before}");
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

        [Fact]
        public void EndpointIteratorIsReliableOverChanges()
        {
            var eps = new EndPointCollection
            {
                { IPAddress.Loopback, 7999 },
                { IPAddress.Loopback, 8000 },
            };

            using var iter = eps.GetEnumerator();
            Assert.True(iter.MoveNext());
            Assert.Equal(7999, ((IPEndPoint)iter.Current).Port);
            eps[1] = new IPEndPoint(IPAddress.Loopback, 8001); // boom
            Assert.True(iter.MoveNext());
            Assert.Equal(8001, ((IPEndPoint)iter.Current).Port);
            Assert.False(iter.MoveNext());
        }

        [Fact]
        public void ThreadPoolManagerIsDetected()
        {
            var config = new ConfigurationOptions
            {
                EndPoints = { { IPAddress.Loopback, 6379 } },
                SocketManager = SocketManager.ThreadPool
            };
            using var muxer = ConnectionMultiplexer.Connect(config);
            Assert.Same(PipeScheduler.ThreadPool, muxer.SocketManager.Scheduler);
        }

        [Fact]
        public void DefaultThreadPoolManagerIsDetected()
        {
            var config = new ConfigurationOptions
            {
                EndPoints = { { IPAddress.Loopback, 6379 } },
            };
            using var muxer = ConnectionMultiplexer.Connect(config);
            Assert.Same(SocketManager.Shared.Scheduler, muxer.SocketManager.Scheduler);
        }
    }
}
