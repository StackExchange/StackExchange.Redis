using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class SentinelBase : TestBase, IAsyncLifetime
{
    protected static string ServiceName => TestConfig.Current.SentinelSeviceName;
    protected static ConfigurationOptions ServiceOptions => new ConfigurationOptions { ServiceName = ServiceName, AllowAdmin = true };

    protected ConnectionMultiplexer Conn { get; set; }
    protected IServer SentinelServerA { get; set; }
    protected IServer SentinelServerB { get; set; }
    protected IServer SentinelServerC { get; set; }
    public IServer[] SentinelsServers { get; set; }

#nullable disable
    public SentinelBase(ITestOutputHelper output) : base(output)
    {
        Skip.IfNoConfig(nameof(TestConfig.Config.SentinelServer), TestConfig.Current.SentinelServer);
        Skip.IfNoConfig(nameof(TestConfig.Config.SentinelSeviceName), TestConfig.Current.SentinelSeviceName);
    }
#nullable enable

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        var options = ServiceOptions.Clone();
        options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA);
        options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortB);
        options.EndPoints.Add(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortC);
        Conn = ConnectionMultiplexer.SentinelConnect(options, Writer);

        for (var i = 0; i < 150; i++)
        {
            await Task.Delay(100).ForAwait();
            if (Conn.IsConnected)
            {
                using var checkConn = Conn.GetSentinelMasterConnection(options, Writer);
                if (checkConn.IsConnected)
                {
                    break;
                }
            }
        }
        Assert.True(Conn.IsConnected);
        SentinelServerA = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortA)!;
        SentinelServerB = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortB)!;
        SentinelServerC = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPortC)!;
        SentinelsServers = new[] { SentinelServerA, SentinelServerB, SentinelServerC };

        SentinelServerA.AllowReplicaWrites = true;
        // Wait until we are in a state of a single primary and replica
        await WaitForReadyAsync();
    }

    // Sometimes it's global, sometimes it's local
    // Depends what mood Redis is in but they're equal and not the point of our tests
    protected static readonly IpComparer _ipComparer = new IpComparer();
    protected class IpComparer : IEqualityComparer<string?>
    {
        public bool Equals(string? x, string? y) => x == y || x?.Replace("0.0.0.0", "127.0.0.1") == y?.Replace("0.0.0.0", "127.0.0.1");
        public int GetHashCode(string? obj) => obj?.GetHashCode() ?? 0;
    }

    protected async Task WaitForReadyAsync(EndPoint? expectedPrimary = null, bool waitForReplication = false, TimeSpan? duration = null)
    {
        duration ??= TimeSpan.FromSeconds(30);

        var sw = Stopwatch.StartNew();

        // wait until we have 1 primary and 1 replica and have verified their roles
        var primary = SentinelServerA.SentinelGetMasterAddressByName(ServiceName);
        if (expectedPrimary != null && expectedPrimary.ToString() != primary?.ToString())
        {
            while (sw.Elapsed < duration.Value)
            {
                await Task.Delay(1000).ForAwait();
                try
                {
                    primary = SentinelServerA.SentinelGetMasterAddressByName(ServiceName);
                    if (expectedPrimary.ToString() == primary?.ToString())
                        break;
                }
                catch (Exception)
                {
                    // ignore
                }
            }
        }
        if (expectedPrimary != null && expectedPrimary.ToString() != primary?.ToString())
            throw new RedisException($"Primary was expected to be {expectedPrimary}");
        Log($"Primary is {primary}");

        using var checkConn = Conn.GetSentinelMasterConnection(ServiceOptions);

        await WaitForRoleAsync(checkConn.GetServer(primary), "master", duration.Value.Subtract(sw.Elapsed)).ForAwait();

        var replicas = SentinelServerA.SentinelGetReplicaAddresses(ServiceName);
        if (replicas?.Length > 0)
        {
            await Task.Delay(1000).ForAwait();
            replicas = SentinelServerA.SentinelGetReplicaAddresses(ServiceName);
            await WaitForRoleAsync(checkConn.GetServer(replicas[0]), "slave", duration.Value.Subtract(sw.Elapsed)).ForAwait();
        }

        if (waitForReplication)
        {
            await WaitForReplicationAsync(checkConn.GetServer(primary), duration.Value.Subtract(sw.Elapsed)).ForAwait();
        }
    }

    protected async Task WaitForRoleAsync(IServer server, string role, TimeSpan? duration = null)
    {
        duration ??= TimeSpan.FromSeconds(30);

        Log($"Waiting for server ({server.EndPoint}) role to be \"{role}\"...");
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration.Value)
        {
            try
            {
                if (server.Role()?.Value == role)
                {
                    Log($"Done waiting for server ({server.EndPoint}) role to be \"{role}\"");
                    return;
                }
            }
            catch (Exception)
            {
                // ignore
            }

            await Task.Delay(500).ForAwait();
        }

        throw new RedisException($"Timeout waiting for server ({server.EndPoint}) to have expected role (\"{role}\") assigned");
    }

    protected async Task WaitForReplicationAsync(IServer primary, TimeSpan? duration = null)
    {
        duration ??= TimeSpan.FromSeconds(10);

        static void LogEndpoints(IServer primary, Action<string> log)
        {
            if (primary.Multiplexer is ConnectionMultiplexer muxer)
            {
                var serverEndpoints = muxer.GetServerSnapshot();
                log("Endpoints:");
                foreach (var serverEndpoint in serverEndpoints)
                {
                    log($"  {serverEndpoint}:");
                    var server = primary.Multiplexer.GetServer(serverEndpoint.EndPoint);
                    log($"     Server: (Connected={server.IsConnected}, Type={server.ServerType}, IsReplica={server.IsReplica}, Unselectable={serverEndpoint.GetUnselectableFlags()})");
                }
            }
        }

        Log("Waiting for primary/replica replication to be in sync...");
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration.Value)
        {
            var info = primary.Info("replication");
            var replicationInfo = info.FirstOrDefault(f => f.Key == "Replication")?.ToArray().ToDictionary();
            var replicaInfo = replicationInfo?.FirstOrDefault(i => i.Key.StartsWith("slave")).Value?.Split(',').ToDictionary(i => i.Split('=').First(), i => i.Split('=').Last());
            var replicaOffset = replicaInfo?["offset"];
            var primaryOffset = replicationInfo?["master_repl_offset"];

            if (replicaOffset == primaryOffset)
            {
                Log($"Done waiting for primary ({primaryOffset}) / replica ({replicaOffset}) replication to be in sync");
                LogEndpoints(primary, m => Log(m));
                return;
            }

            Log($"Waiting for primary ({primaryOffset}) / replica ({replicaOffset}) replication to be in sync...");

            await Task.Delay(250).ForAwait();
        }

        throw new RedisException("Timeout waiting for test servers primary/replica replication to be in sync.");
    }
}
