using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Sentinel : TestBase
    {
        private string ServiceName => TestConfig.Current.SentinelSeviceName;

        private ConnectionMultiplexer Conn { get; }
        private IServer Server26379 { get; }
        private IServer Server26380 { get; }
        private IServer Server26381 { get; }
        public IServer[] SentinelsServers { get; }
        protected StringWriter ConnectionLog { get; }

        public Sentinel(ITestOutputHelper output) : base(output)
        {
            ConnectionLog = new StringWriter();

            Skip.IfNoConfig(nameof(TestConfig.Config.SentinelServer), TestConfig.Current.SentinelServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.SentinelSeviceName), TestConfig.Current.SentinelSeviceName);

            var options = new ConfigurationOptions()
            {
                CommandMap = CommandMap.Sentinel,
                EndPoints = {
                    { TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPort },
                    { TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPort1 },
                    { TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPort2 }
                },
                AllowAdmin = true,
                TieBreaker = "",
                ServiceName = TestConfig.Current.SentinelSeviceName,
                SyncTimeout = 5000
            };
            Conn = ConnectionMultiplexer.Connect(options, ConnectionLog);
            Thread.Sleep(3000);
            Assert.True(Conn.IsConnected);
            Server26379 = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPort);
            Server26380 = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPort1);
            Server26381 = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPort2);
            SentinelsServers = new IServer[] { Server26379, Server26380, Server26381};
        }

        [Fact]
        public void PingTest()
        {
            var test = Server26379.Ping();
            Log("ping to sentinel {0}:{1} took {2} ms", TestConfig.Current.SentinelServer,
                TestConfig.Current.SentinelPort, test.TotalMilliseconds);
            test = Server26380.Ping();
            Log("ping to sentinel {0}:{1} took {1} ms", TestConfig.Current.SentinelServer,
                TestConfig.Current.SentinelPort1, test.TotalMilliseconds);
            test = Server26381.Ping();
            Log("ping to sentinel {0}:{1} took {1} ms", TestConfig.Current.SentinelServer,
                TestConfig.Current.SentinelPort2, test.TotalMilliseconds);
        }

        [Fact]
        public void SentinelGetMasterAddressByNameTest()
        {
            foreach(var server in SentinelsServers)
            {
                var master = server.SentinelMaster(ServiceName);
                var endpoint = server.SentinelGetMasterAddressByName(ServiceName);
                Assert.NotNull(endpoint);
                var ipEndPoint = endpoint as IPEndPoint;
                Assert.NotNull(ipEndPoint);
                Assert.Equal(master.ToDictionary()["ip"], ipEndPoint.Address.ToString());
                Assert.Equal(master.ToDictionary()["port"], ipEndPoint.Port.ToString());
                Log("{0}:{1}", ipEndPoint.Address, ipEndPoint.Port);
            }
        }

        [Fact]
        public async Task SentinelGetMasterAddressByNameAsyncTest()
        {
            foreach (var server in SentinelsServers)
            {
                var master = server.SentinelMaster(ServiceName);
                var endpoint = await server.SentinelGetMasterAddressByNameAsync(ServiceName).ForAwait();
                Assert.NotNull(endpoint);
                var ipEndPoint = endpoint as IPEndPoint;
                Assert.NotNull(ipEndPoint);
                Assert.Equal(master.ToDictionary()["ip"], ipEndPoint.Address.ToString());
                Assert.Equal(master.ToDictionary()["port"], ipEndPoint.Port.ToString());
                Log("{0}:{1}", ipEndPoint.Address, ipEndPoint.Port);
            }
        }
        [Fact]
        public void SentinelGetMasterAddressByNameNegativeTest()
        {
            foreach (var server in SentinelsServers)
            {
                var endpoint = server.SentinelGetMasterAddressByName("FakeServiceName");
                Assert.Null(endpoint);
            }
        }

        [Fact]
        public async Task SentinelGetMasterAddressByNameAsyncNegativeTest()
        {
            foreach (var server in SentinelsServers)
            {
                var endpoint = await server.SentinelGetMasterAddressByNameAsync("FakeServiceName").ForAwait();
                Assert.Null(endpoint);
            }
        }

        [Fact]
        public void SentinelMasterTest()
        {
            foreach (var server in SentinelsServers)
            {
                var dict = server.SentinelMaster(ServiceName).ToDictionary();
                Assert.Equal(ServiceName, dict["name"]);
                Assert.Equal("master", dict["flags"]);
                foreach (var kvp in dict)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public async Task SentinelMasterAsyncTest()
        {
            foreach (var server in SentinelsServers)
            {
                var results = await server.SentinelMasterAsync(ServiceName).ForAwait();
                Assert.Equal(ServiceName, results.ToDictionary()["name"]);
                Assert.Equal("master", results.ToDictionary()["flags"]);
                foreach (var kvp in results)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public void SentinelSentinelsTest()
        {
            var sentinels = Server26379.SentinelSentinels(ServiceName);
            var expected = new List<string> {
                Server26380.EndPoint.ToString(),
                Server26381.EndPoint.ToString()
            };

            var actual = new List<string>();
            foreach(var kv in sentinels)
            {
                actual.Add(kv.ToDictionary()["name"]);
            }
            

            Assert.All(expected, ep => Assert.NotEqual(ep, Server26379.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual));

            sentinels = Server26380.SentinelSentinels(ServiceName);
            foreach (var kv in sentinels)
            {
                actual.Add(kv.ToDictionary()["name"]);
            }
            expected = new List<string> {
                Server26379.EndPoint.ToString(),
                Server26381.EndPoint.ToString()
            };

            Assert.All(expected, ep => Assert.NotEqual(ep, Server26380.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual));
            
            sentinels = Server26381.SentinelSentinels(ServiceName);
            foreach (var kv in sentinels)
            {
                actual.Add(kv.ToDictionary()["name"]);
            }
            expected = new List<string> {
                Server26379.EndPoint.ToString(),
                Server26380.EndPoint.ToString()
            };

            Assert.All(expected, ep => Assert.NotEqual(ep, Server26381.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual));
        }

        [Fact]
        public async Task SentinelSentinelsAsyncTest()
        {
            var sentinels = await Server26379.SentinelSentinelsAsync(ServiceName).ForAwait();
            var expected = new List<string> {
                Server26380.EndPoint.ToString(),
                Server26381.EndPoint.ToString()
            };

            var actual = new List<string>();
            foreach (var kv in sentinels)
            {
                actual.Add(kv.ToDictionary()["name"]);
            }
            Assert.All(expected, ep => Assert.NotEqual(ep, Server26379.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual));
            

            sentinels = await Server26380.SentinelSentinelsAsync(ServiceName).ForAwait();

            expected = new List<string> {
                Server26379.EndPoint.ToString(),
                Server26381.EndPoint.ToString()
            };

            actual = new List<string>();
            foreach (var kv in sentinels)
            {
                actual.Add(kv.ToDictionary()["name"]);
            }
            Assert.All(expected, ep => Assert.NotEqual(ep, Server26380.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual));

            sentinels = await Server26381.SentinelSentinelsAsync(ServiceName).ForAwait();
            expected = new List<string> {
                Server26379.EndPoint.ToString(),
                Server26380.EndPoint.ToString()
            };
            actual = new List<string>();
            foreach (var kv in sentinels)
            {
                actual.Add(kv.ToDictionary()["name"]);
            }
            Assert.All(expected, ep => Assert.NotEqual(ep, Server26381.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual));
        }

        [Fact]
        public void SentinelMastersTest()
        {            
            var masterConfigs = Server26379.SentinelMasters();
            Assert.Single(masterConfigs);
            Assert.True(masterConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.Equal(ServiceName, masterConfigs[0].ToDictionary()["name"]);
            Assert.Equal("master", masterConfigs[0].ToDictionary()["flags"]);
            foreach (var config in masterConfigs)
            {
                foreach (var kvp in config)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public async Task SentinelMastersAsyncTest()
        {
            var masterConfigs = await Server26379.SentinelMastersAsync().ForAwait();
            Assert.Single(masterConfigs);
            Assert.True(masterConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.Equal(ServiceName, masterConfigs[0].ToDictionary()["name"]);
            Assert.Equal("master", masterConfigs[0].ToDictionary()["flags"]);
            foreach (var config in masterConfigs)
            {
                foreach (var kvp in config)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public void SentinelSlavesTest()
        {
            var slaveConfigs = Server26379.SentinelSlaves(ServiceName);
            Assert.True(slaveConfigs.Length > 0);            
            Assert.True(slaveConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.Equal("slave", slaveConfigs[0].ToDictionary()["flags"]);
            
            foreach (var config in slaveConfigs)
            {
                foreach (var kvp in config) {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public async Task SentinelSlavesAsyncTest()
        {
            var slaveConfigs = await Server26379.SentinelSlavesAsync(ServiceName).ForAwait();
            Assert.True(slaveConfigs.Length > 0);
            Assert.True(slaveConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.Equal("slave", slaveConfigs[0].ToDictionary()["flags"]);            
            foreach (var config in slaveConfigs)
            {
                foreach (var kvp in config)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public void SentinelFailoverTest()
        {
            foreach(var server in SentinelsServers)
            {
                var master = server.SentinelGetMasterAddressByName(ServiceName);
                var slaves = server.SentinelSlaves(ServiceName);

                server.SentinelFailover(ServiceName);
                Thread.Sleep(3000);

                var newMaster = server.SentinelGetMasterAddressByName(ServiceName);
                var newSlave = server.SentinelSlaves(ServiceName);

                Assert.Equal(slaves[0].ToDictionary()["name"], newMaster.ToString());
                Assert.Equal(master.ToString(), newSlave[0].ToDictionary()["name"]);
            }
        }

        [Fact]
        public async Task SentinelFailoverAsyncTest()
        {
            foreach (var server in SentinelsServers)
            {
                var master = server.SentinelGetMasterAddressByName(ServiceName);
                var slaves = server.SentinelSlaves(ServiceName);

                await server.SentinelFailoverAsync(ServiceName);
                Thread.Sleep(3000);

                var newMaster = server.SentinelGetMasterAddressByName(ServiceName);
                var newSlave = server.SentinelSlaves(ServiceName);

                Assert.Equal(slaves[0].ToDictionary()["name"], newMaster.ToString());
                Assert.Equal(master.ToString(), newSlave[0].ToDictionary()["name"]);
            }
        }

        [Fact]
        public void GetSentinelMasterConnectionFailoverTest()
        {
            var conn = Conn.GetSentinelMasterConnection(new ConfigurationOptions { ServiceName = ServiceName });
            var endpoint = conn.currentSentinelMasterEndPoint.ToString();

            Server26379.SentinelFailover(ServiceName);
            Thread.Sleep(3000);

            var conn1 = Conn.GetSentinelMasterConnection(new ConfigurationOptions { ServiceName = ServiceName });
            var endpoint1 = conn1.currentSentinelMasterEndPoint.ToString();

            Assert.NotEqual(endpoint, endpoint1);
        }

        [Fact]
        public async Task GetSentinelMasterConnectionFailoverAsyncTest()
        {
            var conn = Conn.GetSentinelMasterConnection(new ConfigurationOptions { ServiceName = ServiceName });
            var endpoint = conn.currentSentinelMasterEndPoint.ToString();

            await Server26379.SentinelFailoverAsync(ServiceName).ForAwait();
            Thread.Sleep(5000);
            var conn1 = Conn.GetSentinelMasterConnection(new ConfigurationOptions { ServiceName = ServiceName });
            var endpoint1 = conn1.currentSentinelMasterEndPoint.ToString();

            Assert.NotEqual(endpoint, endpoint1);
        }

        [Fact]
        public void GetSentinelMasterConnectionWriteReadFailover()
        {
            var conn = Conn.GetSentinelMasterConnection(new ConfigurationOptions { ServiceName = ServiceName });
            var s = conn.currentSentinelMasterEndPoint.ToString();
            IDatabase db = conn.GetDatabase();
            var expected = DateTime.Now.Ticks.ToString();
            db.StringSet("beforeFailOverValue", expected);

            Server26379.SentinelFailover(ServiceName);
            Thread.Sleep(3000);

            var conn1 = Conn.GetSentinelMasterConnection(new ConfigurationOptions { ServiceName = ServiceName });
            var s1 = conn1.currentSentinelMasterEndPoint.ToString();

            var db1 = conn1.GetDatabase();
            var actual = db1.StringGet("beforeFailOverValue");
            Assert.NotNull(s);
            Assert.NotNull(s1);
            Assert.NotEmpty(s);
            Assert.NotEmpty(s1);
            Assert.NotEqual(s, s1);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task SentinelGetSentinelAddressesTest()
        {
            var addresses = await Server26379.SentinelGetSentinelAddresses(ServiceName).ForAwait();            
            Assert.Contains(Server26380.EndPoint, addresses);
            Assert.Contains(Server26381.EndPoint, addresses);

            addresses = await Server26380.SentinelGetSentinelAddresses(ServiceName).ForAwait();
            Assert.Contains(Server26379.EndPoint, addresses);
            Assert.Contains(Server26381.EndPoint, addresses);

            addresses = await Server26381.SentinelGetSentinelAddresses(ServiceName).ForAwait();
            Assert.Contains(Server26379.EndPoint, addresses);
            Assert.Contains(Server26380.EndPoint, addresses);
        }

        [Fact]
        public void ReadOnlyConnectionSlavesTest()
        {
            var slaves = Server26379.SentinelSlaves(ServiceName);
            var config = new ConfigurationOptions
            {                 
                TieBreaker = "",
                ServiceName = TestConfig.Current.SentinelSeviceName,
                SyncTimeout = 5000
            };

            foreach(var kv in slaves)
            {
                config.EndPoints.Add(kv.ToDictionary()["name"]);
            }

            var readonlyConn = ConnectionMultiplexer.Connect(config);
            Thread.Sleep(3000);
            Assert.True(readonlyConn.IsConnected);
            var db = readonlyConn.GetDatabase();
            var s = db.StringGet("test");
            Assert.True(s.IsNullOrEmpty);
            var ex = Assert.Throws<RedisConnectionException>(()=> db.StringSet("test", "try write to read only instance"));
            Assert.StartsWith("No connection is available to service this operation", ex.Message);
        }
    }
}
