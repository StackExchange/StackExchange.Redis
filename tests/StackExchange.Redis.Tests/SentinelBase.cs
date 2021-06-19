﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class SentinelBase : TestBase, IAsyncLifetime
    {
        protected string ServiceName => TestConfig.Current.SentinelSeviceName;
        protected ConfigurationOptions ServiceOptions => new ConfigurationOptions { ServiceName = ServiceName, AllowAdmin = true };

        protected ConnectionMultiplexer Conn { get; set; }
        protected IServer SentinelServerA { get; set; }
        protected IServer SentinelServerB { get; set; }
        protected IServer SentinelServerC { get; set; }
        public IServer[] SentinelsServers { get; set; }

        public SentinelBase(ITestOutputHelper output) : base(output)
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.SentinelServer), TestConfig.Current.SentinelServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.SentinelSeviceName), TestConfig.Current.SentinelSeviceName);
        }

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
                await Task.Delay(20).ForAwait();
                if (Conn.IsConnected && Conn.GetSentinelMasterConnection(options, Writer).IsConnected)
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
            await WaitForReadyAsync();
        }

        // Sometimes it's global, sometimes it's local
        // Depends what mood Redis is in but they're equal and not the point of our tests
        protected static readonly IpComparer _ipComparer = new IpComparer();
        protected class IpComparer : IEqualityComparer<string>
        {
            public bool Equals(string x, string y) => x == y || x?.Replace("0.0.0.0", "127.0.0.1") == y?.Replace("0.0.0.0", "127.0.0.1");
            public int GetHashCode(string obj) => obj.GetHashCode();
        }

        protected async Task DoFailoverAsync()
        {
            await WaitForReadyAsync();

            // capture current replica
            var replicas = SentinelServerA.SentinelGetReplicaAddresses(ServiceName);

            Log("Starting failover...");
            var sw = Stopwatch.StartNew();
            SentinelServerA.SentinelFailover(ServiceName);

            // wait until the replica becomes the master
            await WaitForReadyAsync(expectedMaster: replicas[0]);
            Log($"Time to failover: {sw.Elapsed}");
        }

        protected async Task WaitForReadyAsync(EndPoint expectedMaster = null, bool waitForReplication = false, TimeSpan? duration = null)
        {
            duration ??= TimeSpan.FromSeconds(30);

            var sw = Stopwatch.StartNew();

            // wait until we have 1 master and 1 replica and have verified their roles
            var master = SentinelServerA.SentinelGetMasterAddressByName(ServiceName);
            if (expectedMaster != null && expectedMaster.ToString() != master.ToString())
            {
                while (sw.Elapsed < duration.Value)
                {
                    await Task.Delay(1000).ForAwait();
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

            await WaitForRoleAsync(checkConn.GetServer(master), "master", duration.Value.Subtract(sw.Elapsed)).ForAwait();
            if (replicas.Length > 0)
            {
                await WaitForRoleAsync(checkConn.GetServer(replicas[0]), "slave", duration.Value.Subtract(sw.Elapsed)).ForAwait();
            }

            if (waitForReplication)
            {
                await WaitForReplicationAsync(checkConn.GetServer(master), duration.Value.Subtract(sw.Elapsed)).ForAwait();
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

                await Task.Delay(1000).ForAwait();
            }

            throw new RedisException($"Timeout waiting for server ({server.EndPoint}) to have expected role (\"{role}\") assigned");
        }

        protected async Task WaitForReplicationAsync(IServer master, TimeSpan? duration = null)
        {
            duration ??= TimeSpan.FromSeconds(10);

            static void LogEndpoints(IServer master, Action<string> log)
            {
                var serverEndpoints = (master.Multiplexer as ConnectionMultiplexer).GetServerSnapshot();
                log("Endpoints:");
                foreach (var serverEndpoint in serverEndpoints)
                {
                    log($"  {serverEndpoint}:");
                    var server = master.Multiplexer.GetServer(serverEndpoint.EndPoint);
                    log($"     Server: (Connected={server.IsConnected}, Type={server.ServerType}, IsReplica={server.IsReplica}, Unselectable={serverEndpoint.GetUnselectableFlags()})");
                }
            }

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
                    LogEndpoints(master, Log);
                    return;
                }

                Log($"Waiting for master ({masterOffset}) / replica ({replicaOffset}) replication to be in sync...");

                await Task.Delay(250).ForAwait();
            }

            throw new RedisException("Timeout waiting for test servers master/replica replication to be in sync.");
        }
    }
}
