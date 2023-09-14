using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Profiling;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class SentinelTwoReplicasTest : SentinelBase
{
#nullable disable
    public SentinelTwoReplicasTest(ITestOutputHelper output) : base(output, TestConfig.Current.SentinelTwoReplicasPortA, TestConfig.Current.SentinelTwoReplicasPortB, TestConfig.Current.SentinelTwoReplicasPortC)
    {
    }
#nullable enable

    [Fact]
    public async Task PrimaryConnectTest()
    {
        var connectionString = $"{TestConfig.Current.SentinelServer}:{TestConfig.Current.SentinelTwoReplicasPortA},serviceName={ServiceOptions.ServiceName},allowAdmin=true";
        using var conn = await ConnectionMultiplexer.ConnectAsync(connectionString);

        conn.ConfigurationChanged += (s, e) => Log($"Configuration changed: {e.EndPoint}");

        var db = conn.GetDatabase();
        await db.PingAsync();

        var endpoints = conn.GetEndPoints();
        Assert.Equal(3, endpoints.Length);

        var servers = endpoints.Select(e => conn.GetServer(e)).ToArray();
        Assert.Equal(3, servers.Length);

        var primary = servers.FirstOrDefault(s => !s.IsReplica);
        Assert.NotNull(primary);
        var replica = servers.FirstOrDefault(s => s.IsReplica);
        Assert.NotNull(replica);
        Assert.NotEqual(primary.EndPoint.ToString(), replica.EndPoint.ToString());

        // Set string value on current primary
        var expected = DateTime.Now.Ticks.ToString();
        Log("Tick Key: " + expected);
        var key = Me();
        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
        await db.StringSetAsync(key, expected);

        var value = await db.StringGetAsync(key);
        Assert.Equal(expected, value);

        Log("Waiting for first replication check...");
        // force read from replica, replication has some lag
        await WaitForReplicationAsync(servers[0]).ForAwait();
        value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
        
        Assert.Equal(expected, value);
    }

    [Fact]
    public async Task ReplicasRoundRobinServerSelectionTest()
    {
        var connectionString = $"{TestConfig.Current.SentinelServer}:{TestConfig.Current.SentinelTwoReplicasPortA},serviceName={ServiceOptions.ServiceName},allowAdmin=true";
        using var conn = await ConnectionMultiplexer.ConnectAsync(connectionString);

        conn.ConfigurationChanged += (s, e) => Log($"Configuration changed: {e.EndPoint}");

        var db = conn.GetDatabase();
        await db.PingAsync();

        var endpoints = conn.GetEndPoints();
        Assert.Equal(3, endpoints.Length);

        var servers = endpoints.Select(e => conn.GetServer(e)).ToArray();
        Assert.Equal(3, servers.Length);

        var primary = servers.FirstOrDefault(s => !s.IsReplica);
        Assert.NotNull(primary);
        var replicasCount = servers.Count(s => s.IsReplica);
        Assert.Equal(2, replicasCount);

        // Set string value on current primary
        var expected = DateTime.Now.Ticks.ToString();
        Log("Tick Key: " + expected);
        var key = Me();
        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
        await db.StringSetAsync(key, expected);

        var value = await db.StringGetAsync(key);
        Assert.Equal(expected, value);

        Log("Waiting for first replication check...");
        // force read from replica, replication has some lag
        await WaitForReplicationAsync(servers[0]).ForAwait();

        var profilingSession = new ProfilingSession();
        conn.RegisterProfiler(() => profilingSession);
        for (var i = 0; i < 100; i++)
        {
            await db.StringGetAsync(key, CommandFlags.DemandReplica);
        }

        var commands = profilingSession.FinishProfiling();
        var counters = new Dictionary<string, int>();

        foreach (var endpoint in commands.Select(command => command.EndPoint.ToString()))
        {
            Assert.NotNull(endpoint);
            if (counters.TryGetValue(endpoint, out var c))
                c++;
            else
            {
                c++;
            }
            counters[endpoint] = c;
        }

        foreach (var counter in counters)
        {
            Log($"Endpoint {counter.Key} {counter.Value} commands");
        }

        var pairs = counters.ToArray();
        Assert.Equal(2, pairs.Length);
        Assert.Equal(pairs[0].Value, pairs[1].Value);
    }

}
