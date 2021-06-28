﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class SentinelFailover : SentinelBase, IAsyncLifetime
    {
        public SentinelFailover(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task ManagedMasterConnectionEndToEndWithFailoverTest()
        {
            var connectionString = $"{TestConfig.Current.SentinelServer}:{TestConfig.Current.SentinelPortA},serviceName={ServiceOptions.ServiceName},allowAdmin=true";
            var conn = await ConnectionMultiplexer.ConnectAsync(connectionString);
            conn.ConfigurationChanged += (s, e) => {
                Log($"Configuration changed: {e.EndPoint}");
            };
            var sub = conn.GetSubscriber();
            sub.Subscribe("*", (channel, message) => {
                Log($"Sub: {channel}, message:{message}");
            });

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

            Log("Waiting for first replication check...");
            // force read from replica, replication has some lag
            await WaitForReplicationAsync(servers.First()).ForAwait();
            value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
            Assert.Equal(expected, value);
 
            Log("Waiting for ready pre-failover...");
            await WaitForReadyAsync();

            // capture current replica
            var replicas = SentinelServerA.SentinelGetReplicaAddresses(ServiceName);

            Log("Starting failover...");
            var sw = Stopwatch.StartNew();
            SentinelServerA.SentinelFailover(ServiceName);

            // There's no point in doing much for 10 seconds - this is a built-in delay of how Sentinel works.
            // The actual completion invoking the replication of the former master is handled via
            // https://github.com/redis/redis/blob/f233c4c59d24828c77eb1118f837eaee14695f7f/src/sentinel.c#L4799-L4808
            // ...which is invoked by INFO polls every 10 seconds (https://github.com/redis/redis/blob/f233c4c59d24828c77eb1118f837eaee14695f7f/src/sentinel.c#L81)
            // ...which is calling https://github.com/redis/redis/blob/f233c4c59d24828c77eb1118f837eaee14695f7f/src/sentinel.c#L2666
            // However, the quicker iteration on INFO during an o_down does not apply here: https://github.com/redis/redis/blob/f233c4c59d24828c77eb1118f837eaee14695f7f/src/sentinel.c#L3089-L3104
            // So...we're waiting 10 seconds, no matter what. Might as well just idle to be more stable.
            await Task.Delay(TimeSpan.FromSeconds(10));

            // wait until the replica becomes the master
            Log("Waiting for ready post-failover...");
            await WaitForReadyAsync(expectedMaster: replicas[0]);
            Log($"Time to failover: {sw.Elapsed}");

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

            Log("Waiting for second replication check...");
            // force read from replica, replication has some lag
            await WaitForReplicationAsync(newMaster).ForAwait();
            value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
            Assert.Equal(expected, value);
        }
    }
}
