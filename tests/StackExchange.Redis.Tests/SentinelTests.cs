using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class SentinelTests : SentinelBase
{
    public SentinelTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task PrimaryConnectTest()
    {
        var connectionString = $"{TestConfig.Current.SentinelServer},serviceName={ServiceOptions.ServiceName},allowAdmin=true";

        var conn = ConnectionMultiplexer.Connect(connectionString);

        var db = conn.GetDatabase();
        db.Ping();

        var endpoints = conn.GetEndPoints();
        Assert.Equal(2, endpoints.Length);

        var servers = endpoints.Select(e => conn.GetServer(e)).ToArray();
        Assert.Equal(2, servers.Length);

        var primary = servers.FirstOrDefault(s => !s.IsReplica);
        Assert.NotNull(primary);
        var replica = servers.FirstOrDefault(s => s.IsReplica);
        Assert.NotNull(replica);
        Assert.NotEqual(primary.EndPoint.ToString(), replica.EndPoint.ToString());

        var expected = DateTime.Now.Ticks.ToString();
        Log("Tick Key: " + expected);
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, expected);

        var value = db.StringGet(key);
        Assert.Equal(expected, value);

        // force read from replica, replication has some lag
        await WaitForReplicationAsync(servers[0], TimeSpan.FromSeconds(10)).ForAwait();
        value = db.StringGet(key, CommandFlags.DemandReplica);
        Assert.Equal(expected, value);
    }

    [Fact]
    public async Task PrimaryConnectAsyncTest()
    {
        var connectionString = $"{TestConfig.Current.SentinelServer},serviceName={ServiceOptions.ServiceName},allowAdmin=true";
        var conn = await ConnectionMultiplexer.ConnectAsync(connectionString);

        var db = conn.GetDatabase();
        await db.PingAsync();

        var endpoints = conn.GetEndPoints();
        Assert.Equal(2, endpoints.Length);

        var servers = endpoints.Select(e => conn.GetServer(e)).ToArray();
        Assert.Equal(2, servers.Length);

        var primary = servers.FirstOrDefault(s => !s.IsReplica);
        Assert.NotNull(primary);
        var replica = servers.FirstOrDefault(s => s.IsReplica);
        Assert.NotNull(replica);
        Assert.NotEqual(primary.EndPoint.ToString(), replica.EndPoint.ToString());

        var expected = DateTime.Now.Ticks.ToString();
        Log("Tick Key: " + expected);
        var key = Me();
        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
        await db.StringSetAsync(key, expected);

        var value = await db.StringGetAsync(key);
        Assert.Equal(expected, value);

        // force read from replica, replication has some lag
        await WaitForReplicationAsync(servers[0], TimeSpan.FromSeconds(10)).ForAwait();
        value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
        Assert.Equal(expected, value);
    }

    [Fact]
    [RunPerProtocol]
    public void SentinelConnectTest()
    {
        var options = ServiceOptions.Clone();
        options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA);
        using var conn = ConnectionMultiplexer.SentinelConnect(options);

        var db = conn.GetDatabase();
        var test = db.Ping();
        Log("ping to sentinel {0}:{1} took {2} ms", TestConfig.Current.SentinelServer,
            TestConfig.Current.SentinelPortA, test.TotalMilliseconds);
    }

    [Fact]
    public void SentinelRepeatConnectTest()
    {
        var options = ConfigurationOptions.Parse($"{TestConfig.Current.SentinelServer}:{TestConfig.Current.SentinelPortA}");
        options.ServiceName = ServiceName;
        options.AllowAdmin = true;

        Log("Service Name: " + options.ServiceName);
        foreach (var ep in options.EndPoints)
        {
            Log("  Endpoint: " + ep);
        }

        using var conn = ConnectionMultiplexer.Connect(options);

        var db = conn.GetDatabase();
        var test = db.Ping();
        Log("ping to 1st sentinel {0}:{1} took {2} ms", TestConfig.Current.SentinelServer,
            TestConfig.Current.SentinelPortA, test.TotalMilliseconds);

        Log("Service Name: " + options.ServiceName);
        foreach (var ep in options.EndPoints)
        {
            Log("  Endpoint: " + ep);
        }

        using var conn2 = ConnectionMultiplexer.Connect(options);

        var db2 = conn2.GetDatabase();
        var test2 = db2.Ping();
        Log("ping to 2nd sentinel {0}:{1} took {2} ms", TestConfig.Current.SentinelServer,
            TestConfig.Current.SentinelPortA, test2.TotalMilliseconds);
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
            Assert.NotNull(role);
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
    public void SentinelGetPrimaryAddressByNameTest()
    {
        foreach (var server in SentinelsServers)
        {
            var primary = server.SentinelMaster(ServiceName);
            var endpoint = server.SentinelGetMasterAddressByName(ServiceName);
            Assert.NotNull(endpoint);
            var ipEndPoint = endpoint as IPEndPoint;
            Assert.NotNull(ipEndPoint);
            Assert.Equal(primary.ToDictionary()["ip"], ipEndPoint.Address.ToString());
            Assert.Equal(primary.ToDictionary()["port"], ipEndPoint.Port.ToString());
            Log("{0}:{1}", ipEndPoint.Address, ipEndPoint.Port);
        }
    }

    [Fact]
    public async Task SentinelGetPrimaryAddressByNameAsyncTest()
    {
        foreach (var server in SentinelsServers)
        {
            var primary = server.SentinelMaster(ServiceName);
            var endpoint = await server.SentinelGetMasterAddressByNameAsync(ServiceName).ForAwait();
            Assert.NotNull(endpoint);
            var ipEndPoint = endpoint as IPEndPoint;
            Assert.NotNull(ipEndPoint);
            Assert.Equal(primary.ToDictionary()["ip"], ipEndPoint.Address.ToString());
            Assert.Equal(primary.ToDictionary()["port"], ipEndPoint.Port.ToString());
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
    public void SentinelPrimaryTest()
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
    public async Task SentinelPrimaryAsyncTest()
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

    [Fact]
    public void SentinelSentinelsTest()
    {
        var sentinels = SentinelServerA.SentinelSentinels(ServiceName);

        var expected = new List<string?> {
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
        expected = new List<string?> {
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
        expected = new List<string?> {
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
        var expected = new List<string?> {
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

        expected = new List<string?> {
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
        expected = new List<string?> {
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
    public void SentinelPrimariesTest()
    {
        var primaryConfigs = SentinelServerA.SentinelMasters();
        Assert.Single(primaryConfigs);
        Assert.True(primaryConfigs[0].ToDictionary().ContainsKey("name"), "replicaConfigs contains 'name'");
        Assert.Equal(ServiceName, primaryConfigs[0].ToDictionary()["name"]);
        Assert.StartsWith("master", primaryConfigs[0].ToDictionary()["flags"]);
        foreach (var config in primaryConfigs)
        {
            foreach (var kvp in config)
            {
                Log("{0}:{1}", kvp.Key, kvp.Value);
            }
        }
    }

    [Fact]
    public async Task SentinelPrimariesAsyncTest()
    {
        var primaryConfigs = await SentinelServerA.SentinelMastersAsync().ForAwait();
        Assert.Single(primaryConfigs);
        Assert.True(primaryConfigs[0].ToDictionary().ContainsKey("name"), "replicaConfigs contains 'name'");
        Assert.Equal(ServiceName, primaryConfigs[0].ToDictionary()["name"]);
        Assert.StartsWith("master", primaryConfigs[0].ToDictionary()["flags"]);
        foreach (var config in primaryConfigs)
        {
            foreach (var kvp in config)
            {
                Log("{0}:{1}", kvp.Key, kvp.Value);
            }
        }
    }

    [Fact]
    public async Task SentinelReplicasTest()
    {
        // Give previous test run a moment to reset when multi-framework failover is in play.
        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => SentinelServerA.SentinelReplicas(ServiceName).Length > 0);

        var replicaConfigs = SentinelServerA.SentinelReplicas(ServiceName);
        Assert.True(replicaConfigs.Length > 0, "Has replicaConfigs");
        Assert.True(replicaConfigs[0].ToDictionary().ContainsKey("name"), "replicaConfigs contains 'name'");
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
        // Give previous test run a moment to reset when multi-framework failover is in play.
        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => SentinelServerA.SentinelReplicas(ServiceName).Length > 0);

        var replicaConfigs = await SentinelServerA.SentinelReplicasAsync(ServiceName).ForAwait();
        Assert.True(replicaConfigs.Length > 0, "Has replicaConfigs");
        Assert.True(replicaConfigs[0].ToDictionary().ContainsKey("name"), "replicaConfigs contains 'name'");
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
        if (replicas.Length == 0)
        {
            Skip.Inconclusive("Sentinel race: 0 replicas to test against.");
        }

        var config = new ConfigurationOptions();
        foreach (var replica in replicas)
        {
            config.EndPoints.Add(replica);
        }

        var readonlyConn = await ConnectionMultiplexer.ConnectAsync(config);

        await UntilConditionAsync(TimeSpan.FromSeconds(2), () => readonlyConn.IsConnected);
        Assert.True(readonlyConn.IsConnected);
        var db = readonlyConn.GetDatabase();
        var s = db.StringGet("test");
        Assert.True(s.IsNullOrEmpty);
        //var ex = Assert.Throws<RedisConnectionException>(() => db.StringSet("test", "try write to read only instance"));
        //Assert.StartsWith("No connection is available to service this operation", ex.Message);
    }
}
