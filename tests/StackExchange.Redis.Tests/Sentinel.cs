using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

            var options = ServiceOptions.Clone();
            options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA);
            options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortB);
            options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortC);

            Conn = ConnectionMultiplexer.SentinelConnect(options, ConnectionLog);
            for (var i = 0; i < 150; i++)
            {
                Thread.Sleep(20);
                if (Conn.IsConnected && Conn.GetSentinelMasterConnection(options).IsConnected)
                {
                    break;
                }
            }
            Assert.True(Conn.IsConnected);
            SentinelServerA = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA);
            SentinelServerB = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortB);
            SentinelServerC = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortC);
            SentinelsServers = new[] { SentinelServerA, SentinelServerB, SentinelServerC };

            // wait until we are in a state of a single master and replica
            WaitForReady();
        }

        [Fact]
        public void MasterConnectTest()
        {
            var connectionString = $"{TestConfig.Current.SentinelServer},serviceName={ServiceOptions.ServiceName},allowAdmin=true";
            var conn = ConnectionMultiplexer.Connect(connectionString);

            var db = conn.GetDatabase();
            db.Ping();

            var endpoints = conn.GetEndPoints();
            Assert.Equal(2, endpoints.Length);

            var servers = endpoints.Select(e => conn.GetServer(e)).ToArray();
            Assert.Equal(2, servers.Length);

            var master = servers.FirstOrDefault(s => !s.IsReplica);
            Assert.NotNull(master);
            var replica = servers.FirstOrDefault(s => s.IsReplica);
            Assert.NotNull(replica);
            Assert.NotEqual(master.EndPoint.ToString(), replica.EndPoint.ToString());

            var expected = DateTime.Now.Ticks.ToString();
            Log("Tick Key: " + expected);
            var key = Me();
            db.KeyDelete(key, CommandFlags.FireAndForget);
            db.StringSet(key, expected);

            var value = db.StringGet(key);
            Assert.Equal(expected, value);

            // force read from replica, replication has some lag
            WaitForReplication(servers.First());
            value = db.StringGet(key, CommandFlags.DemandReplica);
            Assert.Equal(expected, value);
        }

        [Fact]
        public async Task MasterConnectAsyncTest()
        {
            var connectionString = $"{TestConfig.Current.SentinelServer},serviceName={ServiceOptions.ServiceName},allowAdmin=true";
            var conn = await ConnectionMultiplexer.ConnectAsync(connectionString);

            var db = conn.GetDatabase();
            await db.PingAsync();

            var endpoints = conn.GetEndPoints();
            Assert.Equal(2, endpoints.Length);

            var servers = endpoints.Select(e => conn.GetServer(e)).ToArray();
            Assert.Equal(2, servers.Length);

            var master = servers.FirstOrDefault(s => !s.IsReplica);
            Assert.NotNull(master);
            var replica = servers.FirstOrDefault(s => s.IsReplica);
            Assert.NotNull(replica);
            Assert.NotEqual(master.EndPoint.ToString(), replica.EndPoint.ToString());

            var expected = DateTime.Now.Ticks.ToString();
            Log("Tick Key: " + expected);
            var key = Me();
            await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
            await db.StringSetAsync(key, expected);

            var value = await db.StringGetAsync(key);
            Assert.Equal(expected, value);

            // force read from replica, replication has some lag
            WaitForReplication(servers.First());
            value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
            Assert.Equal(expected, value);
        }

        [Fact]
        public async Task ManagedMasterConnectionEndToEndWithFailoverTest()
        {
            var connectionString = $"{TestConfig.Current.SentinelServer}:{TestConfig.Current.SentinelPortA},serviceName={ServiceOptions.ServiceName},allowAdmin=true";
            var conn = await ConnectionMultiplexer.ConnectAsync(connectionString);
            conn.ConfigurationChanged += (s, e) => {
                Log($"Configuration changed: {e.EndPoint}");
            };

            var db = conn.GetDatabase();
            await db.PingAsync();

            var endpoints = conn.GetEndPoints();
            Assert.Equal(2, endpoints.Length);

            var servers = endpoints.Select(e => conn.GetServer(e)).ToArray();
            Assert.Equal(2, servers.Length);

            var master = servers.FirstOrDefault(s => !s.IsReplica);
            Assert.NotNull(master);
            var replica = servers.FirstOrDefault(s => s.IsReplica);
            Assert.NotNull(replica);
            Assert.NotEqual(master.EndPoint.ToString(), replica.EndPoint.ToString());

            // set string value on current master
            var expected = DateTime.Now.Ticks.ToString();
            Log("Tick Key: " + expected);
            var key = Me();
            await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
            await db.StringSetAsync(key, expected);

            var value = await db.StringGetAsync(key);
            Assert.Equal(expected, value);

            // force read from replica, replication has some lag
            WaitForReplication(servers.First());
            value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
            Assert.Equal(expected, value);

            // forces and verifies failover
            DoFailover();

            endpoints = conn.GetEndPoints();
            Assert.Equal(2, endpoints.Length);

            servers = endpoints.Select(e => conn.GetServer(e)).ToArray();
            Assert.Equal(2, servers.Length);

            var newMaster = servers.FirstOrDefault(s => !s.IsReplica);
            Assert.NotNull(newMaster);
            Assert.Equal(replica.EndPoint.ToString(), newMaster.EndPoint.ToString());
            var newReplica = servers.FirstOrDefault(s => s.IsReplica);
            Assert.NotNull(newReplica);
            Assert.Equal(master.EndPoint.ToString(), newReplica.EndPoint.ToString());
            Assert.NotEqual(master.EndPoint.ToString(), replica.EndPoint.ToString());

            value = await db.StringGetAsync(key);
            Assert.Equal(expected, value);

            // force read from replica, replication has some lag
            WaitForReplication(newMaster);
            value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
            Assert.Equal(expected, value);
        }

        [Fact]
        public void SentinelConnectTest()
        {
            var options = ServiceOptions.Clone();
            options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA);

            var conn = ConnectionMultiplexer.SentinelConnect(options);
            var db = conn.GetDatabase();

            var test = db.Ping();
            Log("ping to sentinel {0}:{1} took {2} ms", TestConfig.Current.SentinelServer,
                TestConfig.Current.SentinelPortA, test.TotalMilliseconds);
        }

        [Fact]
        public async Task SentinelConnectAsyncTest()
        {
            var options = ServiceOptions.Clone();
            options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA);

            var conn = await ConnectionMultiplexer.SentinelConnectAsync(options);
            var db = conn.GetDatabase();

            var test = await db.PingAsync();
            Log("ping to sentinel {0}:{1} took {2} ms", TestConfig.Current.SentinelServer,
                TestConfig.Current.SentinelPortA, test.TotalMilliseconds);
        }

        [Fact]
        public void SentinelRole()
        {
            foreach (var server in SentinelsServers)
            {
                var role = server.Role();
                Assert.Equal(role.Value, RedisLiterals.sentinel);
                var sentinel = role as Role.Sentinel;
                Assert.NotNull(sentinel);
            }
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
        public void SentinelReplicasTest()
        {
            var replicaConfigs = SentinelServerA.SentinelReplicas(ServiceName);
            Assert.True(replicaConfigs.Length > 0);
            Assert.True(replicaConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.StartsWith("slave", replicaConfigs[0].ToDictionary()["flags"]);

            foreach (var config in replicaConfigs)
            {
                foreach (var kvp in config)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public async Task SentinelReplicasAsyncTest()
        {
            var replicaConfigs = await SentinelServerA.SentinelReplicasAsync(ServiceName).ForAwait();
            Assert.True(replicaConfigs.Length > 0);
            Assert.True(replicaConfigs[0].ToDictionary().ContainsKey("name"));
            Assert.StartsWith("slave", replicaConfigs[0].ToDictionary()["flags"]);
            foreach (var config in replicaConfigs)
            {
                foreach (var kvp in config)
                {
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public async Task SentinelGetSentinelAddressesTest()
        {
            var addresses = await SentinelServerA.SentinelGetSentinelAddressesAsync(ServiceName).ForAwait();
            Assert.Contains(SentinelServerB.EndPoint, addresses);
            Assert.Contains(SentinelServerC.EndPoint, addresses);

            addresses = await SentinelServerB.SentinelGetSentinelAddressesAsync(ServiceName).ForAwait();
            Assert.Contains(SentinelServerA.EndPoint, addresses);
            Assert.Contains(SentinelServerC.EndPoint, addresses);

            addresses = await SentinelServerC.SentinelGetSentinelAddressesAsync(ServiceName).ForAwait();
            Assert.Contains(SentinelServerA.EndPoint, addresses);
            Assert.Contains(SentinelServerB.EndPoint, addresses);
        }

        [Fact]
        public async Task ReadOnlyConnectionReplicasTest()
        {
            var replicas = SentinelServerA.SentinelGetReplicaAddresses(ServiceName);
            var config = new ConfigurationOptions();

            foreach (var replica in replicas)
            {
                config.EndPoints.Add(replica);
            }

            var readonlyConn = await ConnectionMultiplexer.ConnectAsync(config);

            await UntilCondition(TimeSpan.FromSeconds(2), () => readonlyConn.IsConnected);
            Assert.True(readonlyConn.IsConnected);
            var db = readonlyConn.GetDatabase();
            var s = db.StringGet("test");
            Assert.True(s.IsNullOrEmpty);
            //var ex = Assert.Throws<RedisConnectionException>(() => db.StringSet("test", "try write to read only instance"));
            //Assert.StartsWith("No connection is available to service this operation", ex.Message);
        }

        private void DoFailover()
        {
            WaitForReady();

            // capture current replica
            var replicas = SentinelServerA.SentinelGetReplicaAddresses(ServiceName);

            Log("Starting failover...");
            var sw = Stopwatch.StartNew();
            SentinelServerA.SentinelFailover(ServiceName);

            // wait until the replica becomes the master
            WaitForReady(expectedMaster: replicas[0]);
            Log($"Time to failover: {sw.Elapsed}");
        }

        private void WaitForReady(EndPoint expectedMaster = null, bool waitForReplication = false, TimeSpan? duration = null)
        {
            duration ??= TimeSpan.FromSeconds(30);

            var sw = Stopwatch.StartNew();

            // wait until we have 1 master and 1 replica and have verified their roles
            var master = SentinelServerA.SentinelGetMasterAddressByName(ServiceName);
            if (expectedMaster != null && expectedMaster.ToString() != master.ToString())
            {
                while (sw.Elapsed < duration.Value)
                {
                    Thread.Sleep(1000);
                    try
                    {
                        master = SentinelServerA.SentinelGetMasterAddressByName(ServiceName);
                        if (expectedMaster.ToString() == master.ToString())
                            break;
                    }
                    catch (Exception)
                    {
                        // ignore
                    }
                }
            }
            if (expectedMaster != null && expectedMaster.ToString() != master.ToString())
                throw new RedisException($"Master was expected to be {expectedMaster}");
            Log($"Master is {master}");

            var replicas = SentinelServerA.SentinelGetReplicaAddresses(ServiceName);
            var checkConn = Conn.GetSentinelMasterConnection(ServiceOptions);

            WaitForRole(checkConn.GetServer(master), "master", duration.Value.Subtract(sw.Elapsed));
            if (replicas.Length > 0)
            {
                WaitForRole(checkConn.GetServer(replicas[0]), "slave", duration.Value.Subtract(sw.Elapsed));
            }

            if (waitForReplication)
            {
                WaitForReplication(checkConn.GetServer(master), duration.Value.Subtract(sw.Elapsed));
            }
        }

        private void WaitForRole(IServer server, string role, TimeSpan? duration = null)
        {
            duration ??= TimeSpan.FromSeconds(30);

            Log($"Waiting for server ({server.EndPoint}) role to be \"{role}\"...");
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < duration.Value)
            {
                try
                {
                    if (server.Role().Value == role)
                    {
                        Log($"Done waiting for server ({server.EndPoint}) role to be \"{role}\"");
                        return;
                    }
                }
                catch (Exception)
                {
                    // ignore
                }

                Thread.Sleep(1000);
            }

            throw new RedisException("Timeout waiting for server to have expected role assigned");
        }

        private void WaitForReplication(IServer master, TimeSpan? duration = null)
        {
            duration ??= TimeSpan.FromSeconds(10);

            Log("Waiting for master/replica replication to be in sync...");
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < duration.Value)
            {
                var info = master.Info("replication");
                var replicationInfo = info.FirstOrDefault(f => f.Key == "Replication")?.ToArray().ToDictionary();
                var replicaInfo = replicationInfo?.FirstOrDefault(i => i.Key.StartsWith("slave")).Value?.Split(',').ToDictionary(i => i.Split('=').First(), i => i.Split('=').Last());
                var replicaOffset = replicaInfo?["offset"];
                var masterOffset = replicationInfo?["master_repl_offset"];

                if (replicaOffset == masterOffset)
                {
                    Log($"Done waiting for master ({masterOffset}) / replica ({replicaOffset}) replication to be in sync");
                    return;
                }

                Log($"Waiting for master ({masterOffset}) / replica ({replicaOffset}) replication to be in sync...");

                Thread.Sleep(250);
            }

            throw new RedisException("Timeout waiting for test servers master/replica replication to be in sync.");
        }
    }
}
