using System;
using System.IO;
using System.Linq;
using System.Net;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class MultiMaster : TestBase
    {
        protected override string GetConfiguration() =>
            PrimaryServer + ":" + SecurePort + "," + PrimaryServer + ":" + PrimaryPort + ",password=" + SecurePassword;

        public MultiMaster(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void CannotFlushSlave()
        {
            var ex = Assert.Throws<RedisCommandException>(() =>
            {
                ConfigurationOptions config = GetMasterSlaveConfig();
                using (var conn = ConnectionMultiplexer.Connect(config))
                {
                    var servers = conn.GetEndPoints().Select(e => conn.GetServer(e));
                    var slave = servers.First(x => x.IsSlave);
                    slave.FlushDatabase();
                }
            });
            Assert.Equal("Command cannot be issued to a slave: FLUSHDB", ex.Message);
        }

        [Fact]
        public void DeslaveGoesToPrimary()
        {
            ConfigurationOptions config = GetMasterSlaveConfig();
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var primary = conn.GetServer(new IPEndPoint(IPAddress.Parse(PrimaryServer), PrimaryPort));
                var secondary = conn.GetServer(new IPEndPoint(IPAddress.Parse(PrimaryServer), SlavePort));

                primary.Ping();
                secondary.Ping();

                primary.MakeMaster(ReplicationChangeOptions.SetTiebreaker);
                secondary.MakeMaster(ReplicationChangeOptions.None);

                primary.Ping();
                secondary.Ping();

                using (var writer = new StringWriter())
                {
                    conn.Configure(writer);
                    string log = writer.ToString();

                    Assert.True(log.Contains("tie-break is unanimous at " + PrimaryServer + ":" + PrimaryPort), "unanimous");
                }
                // k, so we know everyone loves 6379; is that what we get?

                var db = conn.GetDatabase();
                RedisKey key = Me();

                EndPoint demandMaster, preferMaster, preferSlave, demandSlave;
                preferMaster = db.IdentifyEndpoint(key, CommandFlags.PreferMaster);
                demandMaster = db.IdentifyEndpoint(key, CommandFlags.DemandMaster);
                preferSlave = db.IdentifyEndpoint(key, CommandFlags.PreferSlave);

                Assert.Equal(primary.EndPoint, demandMaster);
                Assert.Equal(primary.EndPoint, preferMaster);
                Assert.Equal(primary.EndPoint, preferSlave);

                try
                {
                    demandSlave = db.IdentifyEndpoint(key, CommandFlags.DemandSlave);
                    Assert.True(false, "this should not have worked");
                }
                catch (RedisConnectionException ex)
                {
                    Assert.Equal("No connection is available to service this operation: EXISTS DeslaveGoesToPrimary", ex.Message);
                }

                primary.MakeMaster(ReplicationChangeOptions.Broadcast | ReplicationChangeOptions.EnslaveSubordinates | ReplicationChangeOptions.SetTiebreaker);

                primary.Ping();
                secondary.Ping();

                preferMaster = db.IdentifyEndpoint(key, CommandFlags.PreferMaster);
                demandMaster = db.IdentifyEndpoint(key, CommandFlags.DemandMaster);
                preferSlave = db.IdentifyEndpoint(key, CommandFlags.PreferSlave);
                demandSlave = db.IdentifyEndpoint(key, CommandFlags.DemandSlave);

                Assert.Equal(primary.EndPoint, demandMaster);
                Assert.Equal(primary.EndPoint, preferMaster);
                Assert.Equal(secondary.EndPoint, preferSlave);
                Assert.Equal(secondary.EndPoint, preferSlave);
            }
        }

        private static ConfigurationOptions GetMasterSlaveConfig()
        {
            return new ConfigurationOptions
            {
                AllowAdmin = true,
                SyncTimeout = 100000,
                EndPoints =
                {
                    { PrimaryServer, PrimaryPort },
                    { PrimaryServer, SlavePort },
                }
            };
        }

        [Fact]
        public void TestMultiNoTieBreak()
        {
            using (var log = new StringWriter())
            using (var conn = Create(log: log, tieBreaker: ""))
            {
                Output.WriteLine(log.ToString());
                Assert.Contains("Choosing master arbitrarily", log.ToString());
            }
        }

        [Theory]
        [InlineData(PrimaryServer + ":" + PrimaryPortString, PrimaryServer + ":" + PrimaryPortString, PrimaryServer + ":" + PrimaryPortString)]
        [InlineData(PrimaryServer + ":" + SecurePortString, PrimaryServer + ":" + SecurePortString, PrimaryServer + ":" + SecurePortString)]
        [InlineData(PrimaryServer + ":" + SecurePortString, PrimaryServer + ":" + PrimaryPortString, null)]
        [InlineData(PrimaryServer + ":" + PrimaryPortString, PrimaryServer + ":" + SecurePortString, null)]

        [InlineData(null, PrimaryServer + ":" + PrimaryPortString, PrimaryServer + ":" + PrimaryPortString)]
        [InlineData(PrimaryServer + ":" + PrimaryPortString, null, PrimaryServer + ":" + PrimaryPortString)]
        [InlineData(null, PrimaryServer + ":" + SecurePortString, PrimaryServer + ":" + SecurePortString)]
        [InlineData(PrimaryServer + ":" + SecurePortString, null, PrimaryServer + ":" + SecurePortString)]
        [InlineData(null, null, null)]

        public void TestMultiWithTiebreak(string a, string b, string elected)
        {
            const string TieBreak = "__tie__";
            // set the tie-breakers to the expected state
            using (var aConn = ConnectionMultiplexer.Connect(PrimaryServer + ":" + PrimaryPort))
            {
                aConn.GetDatabase().StringSet(TieBreak, a);
            }
            using (var aConn = ConnectionMultiplexer.Connect(PrimaryServer + ":" + SecurePort + ",password=" + SecurePassword))
            {
                aConn.GetDatabase().StringSet(TieBreak, b);
            }

            // see what happens
            using (var log = new StringWriter())
            using (var conn = Create(log: log, tieBreaker: TieBreak))
            {
                string text = log.ToString();
                Output.WriteLine(text);
                Assert.False(text.Contains("failed to nominate"), "failed to nominate");
                if (elected != null)
                {
                    Assert.True(text.Contains("Elected: " + elected), "elected");
                }
                int nullCount = (a == null ? 1 : 0) + (b == null ? 1 : 0);
                if ((a == b && nullCount == 0) || nullCount == 1)
                {
                    Assert.True(text.Contains("tie-break is unanimous"), "unanimous");
                    Assert.False(text.Contains("Choosing master arbitrarily"), "arbitrarily");
                }
                else
                {
                    Assert.False(text.Contains("tie-break is unanimous"), "unanimous");
                    Assert.True(text.Contains("Choosing master arbitrarily"), "arbitrarily");
                }
            }
        }
    }
}