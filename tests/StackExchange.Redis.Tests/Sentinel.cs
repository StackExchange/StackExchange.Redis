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
        private ConfigurationOptions ServiceOptions => new ConfigurationOptions { ServiceName = ServiceName, AllowAdmin = true };

        private ConnectionMultiplexer Conn { get; }
        private IServer SentinelServerA { get; }
        private IServer SentinelServerB { get; }
        private IServer SentinelServerC { get; }
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
                    { TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA },
                    { TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortB },
                    { TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortC }
                },
                AllowAdmin = true,
                TieBreaker = "",
                ServiceName = TestConfig.Current.SentinelSeviceName,
                SyncTimeout = 5000
            };
            Conn = ConnectionMultiplexer.Connect(options, ConnectionLog);
            for (var i = 0; i < 150; i++)
            {
                Thread.Sleep(20);
                if (Conn.IsConnected && Conn.GetSentinelMasterConnection(ServiceOptions).IsConnected)
                {
                    break;
                }
            }
            Assert.True(Conn.IsConnected);
            SentinelServerA = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA);
            SentinelServerB = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortB);
            SentinelServerC = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortC);
            SentinelsServers = new IServer[] { SentinelServerA, SentinelServerB, SentinelServerC };
        }

        [Fact]
        public void PingTest()
        {
            var test = SentinelServerA.Ping();
            Log("ping to sentinel {0}:{1} took {2} ms", TestConfig.Current.SentinelServer,
                TestConfig.Current.SentinelPortA, test.TotalMilliseconds);
            test = SentinelServerB.Ping();
            Log("ping to sentinel {0}:{1} took {1} ms", TestConfig.Current.SentinelServer,
                TestConfig.Current.SentinelPortB, test.TotalMilliseconds);
            test = SentinelServerC.Ping();
            Log("ping to sentinel {0}:{1} took {1} ms", TestConfig.Current.SentinelServer,
                TestConfig.Current.SentinelPortC, test.TotalMilliseconds);
        }

        [Fact]
        public void SentinelGetMasterAddressByNameTest()
        {
            foreach (var server in SentinelsServers)
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
                Assert.StartsWith("master", dict["flags"]);
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
                Assert.StartsWith("master", results.ToDictionary()["flags"]);
                foreach (var kvp in results)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        // Sometimes it's global, sometimes it's local
        // Depends what mood Redis is in but they're equal and not the point of our tests
        private static readonly IpComparer _ipComparer = new IpComparer();
        private class IpComparer : IEqualityComparer<string>
        {
            public bool Equals(string x, string y) => x == y || x?.Replace("0.0.0.0", "127.0.0.1") == y?.Replace("0.0.0.0", "127.0.0.1");
            public int GetHashCode(string obj) => obj.GetHashCode();
        }

        [Fact]
        public void SentinelSentinelsTest()
        {
            var sentinels = SentinelServerA.SentinelSentinels(ServiceName);
            var Server26380Info = SentinelServerB.Info();

            var expected = new List<string> {
                SentinelServerB.EndPoint.ToString(),
                SentinelServerC.EndPoint.ToString()
            };

            var actual = new List<string>();
            foreach (var kv in sentinels)
            {
                var data = kv.ToDictionary();
                actual.Add(data["ip"] + ":" + data["port"]);
            }

            Assert.All(expected, ep => Assert.NotEqual(ep, SentinelServerA.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual, _ipComparer));

            sentinels = SentinelServerB.SentinelSentinels(ServiceName);
            foreach (var kv in sentinels)
            {
                var data = kv.ToDictionary();
                actual.Add(data["ip"] + ":" + data["port"]);
            }
            expected = new List<string> {
                SentinelServerA.EndPoint.ToString(),
                SentinelServerC.EndPoint.ToString()
            };

            Assert.All(expected, ep => Assert.NotEqual(ep, SentinelServerB.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual, _ipComparer));

            sentinels = SentinelServerC.SentinelSentinels(ServiceName);
            foreach (var kv in sentinels)
            {
                var data = kv.ToDictionary();
                actual.Add(data["ip"] + ":" + data["port"]);
            }
            expected = new List<string> {
                SentinelServerA.EndPoint.ToString(),
                SentinelServerB.EndPoint.ToString()
            };

            Assert.All(expected, ep => Assert.NotEqual(ep, SentinelServerC.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual, _ipComparer));
        }

        [Fact]
        public async Task SentinelSentinelsAsyncTest()
        {
            var sentinels = await SentinelServerA.SentinelSentinelsAsync(ServiceName).ForAwait();
            var expected = new List<string> {
                SentinelServerB.EndPoint.ToString(),
                SentinelServerC.EndPoint.ToString()
            };

            var actual = new List<string>();
            foreach (var kv in sentinels)
            {
                var data = kv.ToDictionary();
                actual.Add(data["ip"] + ":" + data["port"]);
            }
            Assert.All(expected, ep => Assert.NotEqual(ep, SentinelServerA.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual, _ipComparer));

            sentinels = await SentinelServerB.SentinelSentinelsAsync(ServiceName).ForAwait();

            expected = new List<string> {
                SentinelServerA.EndPoint.ToString(),
                SentinelServerC.EndPoint.ToString()
            };

            actual = new List<string>();
            foreach (var kv in sentinels)
            {
                var data = kv.ToDictionary();
                actual.Add(data["ip"] + ":" + data["port"]);
            }
            Assert.All(expected, ep => Assert.NotEqual(ep, SentinelServerB.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual, _ipComparer));

            sentinels = await SentinelServerC.SentinelSentinelsAsync(ServiceName).ForAwait();
            expected = new List<string> {
                SentinelServerA.EndPoint.ToString(),
                SentinelServerB.EndPoint.ToString()
            };
            actual = new List<string>();
            foreach (var kv in sentinels)
            {
                var data = kv.ToDictionary();
                actual.Add(data["ip"] + ":" + data["port"]);
            }
            Assert.All(expected, ep => Assert.NotEqual(ep, SentinelServerC.EndPoint.ToString()));
            Assert.True(sentinels.Length == 2);
            Assert.All(expected, ep => Assert.Contains(ep, actual, _ipComparer));
        }

        [Fact]
        public void SentinelMastersTest()
        {
            var masterConfigs = SentinelServerA.SentinelMasters();
            Assert.Single(masterConfigs);
            Assert.True(masterConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.Equal(ServiceName, masterConfigs[0].ToDictionary()["name"]);
            Assert.StartsWith("master", masterConfigs[0].ToDictionary()["flags"]);
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
            var masterConfigs = await SentinelServerA.SentinelMastersAsync().ForAwait();
            Assert.Single(masterConfigs);
            Assert.True(masterConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.Equal(ServiceName, masterConfigs[0].ToDictionary()["name"]);
            Assert.StartsWith("master", masterConfigs[0].ToDictionary()["flags"]);
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
            var slaveConfigs = SentinelServerA.SentinelSlaves(ServiceName);
            Assert.True(slaveConfigs.Length > 0);
            Assert.True(slaveConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.StartsWith("slave", slaveConfigs[0].ToDictionary()["flags"]);

            foreach (var config in slaveConfigs)
            {
                foreach (var kvp in config)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public async Task SentinelSlavesAsyncTest()
        {
            var slaveConfigs = await SentinelServerA.SentinelSlavesAsync(ServiceName).ForAwait();
            Assert.True(slaveConfigs.Length > 0);
            Assert.True(slaveConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.StartsWith("slave", slaveConfigs[0].ToDictionary()["flags"]);
            foreach (var config in slaveConfigs)
            {
                foreach (var kvp in config)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public async Task SentinelFailoverTest()
        {
            var i = 0;
            foreach (var server in SentinelsServers)
            {
                Log("Failover: " + i++);
                var master = server.SentinelGetMasterAddressByName(ServiceName);
                var slaves = server.SentinelSlaves(ServiceName);

                await Task.Delay(1000).ForAwait();
                try
                {
                    Log("Failover attempted initiated");
                    server.SentinelFailover(ServiceName);
                    Log("  Success!");
                }
                catch (RedisServerException ex) when (ex.Message.Contains("NOGOODSLAVE"))
                {
                    // Retry once
                    Log("  Retry initiated");
                    await Task.Delay(1000).ForAwait();
                    server.SentinelFailover(ServiceName);
                    Log("  Retry complete");
                }
                await Task.Delay(2000).ForAwait();

                var newMaster = server.SentinelGetMasterAddressByName(ServiceName);
                var newSlave = server.SentinelSlaves(ServiceName);

                Assert.Equal(slaves[0].ToDictionary()["name"], newMaster.ToString());
                Assert.Equal(master.ToString(), newSlave[0].ToDictionary()["name"]);
            }
        }

        [Fact]
        public async Task SentinelFailoverAsyncTest()
        {
            var i = 0;
            foreach (var server in SentinelsServers)
            {
                Log("Failover: " + i++);
                var master = server.SentinelGetMasterAddressByName(ServiceName);
                var slaves = server.SentinelSlaves(ServiceName);

                await Task.Delay(1000).ForAwait();
                try
                {
                    Log("Failover attempted initiated");
                    await server.SentinelFailoverAsync(ServiceName).ForAwait();
                    Log("  Success!");
                }
                catch (RedisServerException ex) when (ex.Message.Contains("NOGOODSLAVE"))
                {
                    // Retry once
                    Log("  Retry initiated");
                    await Task.Delay(1000).ForAwait();
                    await server.SentinelFailoverAsync(ServiceName).ForAwait();
                    Log("  Retry complete");
                }
                await Task.Delay(2000).ForAwait();

                var newMaster = server.SentinelGetMasterAddressByName(ServiceName);
                var newSlave = server.SentinelSlaves(ServiceName);

                Assert.Equal(slaves[0].ToDictionary()["name"], newMaster.ToString());
                Assert.Equal(master.ToString(), newSlave[0].ToDictionary()["name"]);
            }
        }

        [Fact]
        public async Task GetSentinelMasterConnectionFailoverTest()
        {
            var conn = Conn.GetSentinelMasterConnection(ServiceOptions);
            var endpoint = conn.currentSentinelMasterEndPoint.ToString();

            try
            {
                Log("Failover attempted initiated");
                SentinelServerA.SentinelFailover(ServiceName);
                Log("  Success!");
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGOODSLAVE"))
            {
                // Retry once
                Log("  Retry initiated");
                await Task.Delay(1000).ForAwait();
                SentinelServerA.SentinelFailover(ServiceName);
                Log("  Retry complete");
            }
            await Task.Delay(2000).ForAwait();

            // Try and complete ASAP
            await UntilCondition(TimeSpan.FromSeconds(10), () => {
                var checkConn = Conn.GetSentinelMasterConnection(ServiceOptions);
                return endpoint != checkConn.currentSentinelMasterEndPoint.ToString();
            });

            // Post-check for validity
            var conn1 = Conn.GetSentinelMasterConnection(ServiceOptions);
            Assert.NotEqual(endpoint, conn1.currentSentinelMasterEndPoint.ToString());
        }

        [Fact]
        public async Task GetSentinelMasterConnectionFailoverAsyncTest()
        {
            var conn = Conn.GetSentinelMasterConnection(ServiceOptions);
            var endpoint = conn.currentSentinelMasterEndPoint.ToString();

            try
            {
                Log("Failover attempted initiated");
                await SentinelServerA.SentinelFailoverAsync(ServiceName).ForAwait();
                Log("  Success!");
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGOODSLAVE"))
            {
                // Retry once
                Log("  Retry initiated");
                await Task.Delay(1000).ForAwait();
                await SentinelServerA.SentinelFailoverAsync(ServiceName).ForAwait();
                Log("  Retry complete");
            }

            // Try and complete ASAP
            await UntilCondition(TimeSpan.FromSeconds(10), () => {
                var checkConn = Conn.GetSentinelMasterConnection(ServiceOptions);
                return endpoint != checkConn.currentSentinelMasterEndPoint.ToString();
            });

            // Post-check for validity
            var conn1 = Conn.GetSentinelMasterConnection(ServiceOptions);
            Assert.NotEqual(endpoint, conn1.currentSentinelMasterEndPoint.ToString());
        }

        [Fact]
        public async Task GetSentinelMasterConnectionWriteReadFailover()
        {
            Log("Conn:");
            foreach (var server in Conn.GetServerSnapshot().ToArray())
            {
                Log("  Endpoint: " + server.EndPoint);
            }
            Log("Conn Slaves:");
            foreach (var slaves in SentinelServerA.SentinelSlaves(ServiceName))
            {
                foreach(var pair in slaves)
                {
                    Log("  {0}: {1}", pair.Key, pair.Value);
                }
            }

            var conn = Conn.GetSentinelMasterConnection(ServiceOptions);
            var s = conn.currentSentinelMasterEndPoint.ToString();
            Log("Sentinel Master Endpoint: " + s);
            foreach (var server in conn.GetServerSnapshot().ToArray())
            {
                Log("  Server: " + server.EndPoint);
                Log("    Master Endpoint: " + server.MasterEndPoint);
                Log("    IsSlave: " + server.IsSlave);
                Log("    SlaveReadOnly: " + server.SlaveReadOnly);
                var info = conn.GetServer(server.EndPoint).Info("Replication");
                foreach (var section in info)
                {
                    Log("    Section: " + section.Key);
                    foreach (var pair in section)
                    {
                        Log("        " + pair.Key +": " + pair.Value);
                    }
                }
            }

            IDatabase db = conn.GetDatabase();
            var expected = DateTime.Now.Ticks.ToString();
            Log("Tick Key: " + expected);
            var key = Me();
            db.KeyDelete(key, CommandFlags.FireAndForget);
            db.StringSet(key, expected);

            await UntilCondition(TimeSpan.FromSeconds(10),
                () => SentinelServerA.SentinelMaster(ServiceName).ToDictionary()["num-slaves"] != "0"
            );
            Log("Conditions met");

            try
            {
                Log("Failover attempted initiated");
                SentinelServerA.SentinelFailover(ServiceName);
                Log("  Success!");
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGOODSLAVE"))
            {
                // Retry once
                Log("  Retry initiated");
                await Task.Delay(1000).ForAwait();
                SentinelServerA.SentinelFailover(ServiceName);
                Log("  Retry complete");
            }
            Log("Delaying for failover conditions...");
            await Task.Delay(2000).ForAwait();
            Log("Conditons check...");
            // Spin until complete (with a timeout) - since this can vary
            await UntilCondition(TimeSpan.FromSeconds(20), () =>
            {
                var checkConn = Conn.GetSentinelMasterConnection(ServiceOptions);
                return s != checkConn.currentSentinelMasterEndPoint.ToString()
                    && expected == checkConn.GetDatabase().StringGet(key);
            });
            Log("  Conditions met.");

            var conn1 = Conn.GetSentinelMasterConnection(ServiceOptions);
            var s1 = conn1.currentSentinelMasterEndPoint.ToString();
            Log("New master endpoint: " + s1);

            var actual = conn1.GetDatabase().StringGet(key);
            Log("Fetched tick key: " + actual);

            Assert.NotNull(s);
            Assert.NotNull(s1);
            Assert.NotEmpty(s);
            Assert.NotEmpty(s1);
            Assert.NotEqual(s, s1);
            // TODO: Track this down on the test race
            //Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task SentinelGetSentinelAddressesTest()
        {
            var addresses = await SentinelServerA.SentinelGetSentinelAddresses(ServiceName).ForAwait();
            Assert.Contains(SentinelServerB.EndPoint, addresses);
            Assert.Contains(SentinelServerC.EndPoint, addresses);

            addresses = await SentinelServerB.SentinelGetSentinelAddresses(ServiceName).ForAwait();
            Assert.Contains(SentinelServerA.EndPoint, addresses);
            Assert.Contains(SentinelServerC.EndPoint, addresses);

            addresses = await SentinelServerC.SentinelGetSentinelAddresses(ServiceName).ForAwait();
            Assert.Contains(SentinelServerA.EndPoint, addresses);
            Assert.Contains(SentinelServerB.EndPoint, addresses);
        }

        [Fact]
        public async Task ReadOnlyConnectionSlavesTest()
        {
            var slaves = SentinelServerA.SentinelSlaves(ServiceName);
            var config = new ConfigurationOptions
            {
                TieBreaker = "",
                ServiceName = TestConfig.Current.SentinelSeviceName,
            };

            foreach (var kv in slaves)
            {
                Assert.Equal("slave", kv.ToDictionary()["flags"]);
                config.EndPoints.Add(kv.ToDictionary()["name"]);
            }

            var readonlyConn = ConnectionMultiplexer.Connect(config);

            await UntilCondition(TimeSpan.FromSeconds(2), () => readonlyConn.IsConnected);
            Assert.True(readonlyConn.IsConnected);
            var db = readonlyConn.GetDatabase();
            var s = db.StringGet("test");
            Assert.True(s.IsNullOrEmpty);
            //var ex = Assert.Throws<RedisConnectionException>(() => db.StringSet("test", "try write to read only instance"));
            //Assert.StartsWith("No connection is available to service this operation", ex.Message);

        }
    }
}
