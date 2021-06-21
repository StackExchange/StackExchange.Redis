using System;
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
            await WaitForReplicationAsync(servers.First()).ForAwait();
            value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
            Assert.Equal(expected, value);

            // forces and verifies failover
            await DoFailoverAsync();

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
            await WaitForReplicationAsync(newMaster).ForAwait();
            value = await db.StringGetAsync(key, CommandFlags.DemandReplica);
            Assert.Equal(expected, value);
        }
    }
}
