using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class MultiMaster : TestBase
    {
        protected override string GetConfiguration() =>
            TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort + "," + TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort + ",password=" + TestConfig.Current.SecurePassword;

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
                    var slave = servers.FirstOrDefault(x => x.IsSlave);
                    Assert.NotNull(slave); // Slave not found, ruh roh
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
                var primary = conn.GetServer(new IPEndPoint(IPAddress.Parse(TestConfig.Current.MasterServer), TestConfig.Current.MasterPort));
                var secondary = conn.GetServer(new IPEndPoint(IPAddress.Parse(TestConfig.Current.MasterServer), TestConfig.Current.SlavePort));

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

                    Assert.True(log.Contains("tie-break is unanimous at " + TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort), "unanimous");
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
                    Assert.StartsWith("No connection is available to service this operation: EXISTS DeslaveGoesToPrimary", ex.Message);
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
                    { TestConfig.Current.MasterServer, TestConfig.Current.MasterPort },
                    { TestConfig.Current.MasterServer, TestConfig.Current.SlavePort },
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

        public static IEnumerable<object[]> GetConnections()
        {
            yield return new object[] { TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort, TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort, TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort };
            yield return new object[] { TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort, TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort, TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort };
            yield return new object[] { TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort, TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort, null };
            yield return new object[] { TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort, TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort, null };

            yield return new object[] { null, TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort, TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort };
            yield return new object[] { TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort, null, TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort };
            yield return new object[] { null, TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort, TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort };
            yield return new object[] { TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort, null, TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort };
            yield return new object[] { null, null, null };
        }

        [Theory, MemberData(nameof(GetConnections))]
        public void TestMultiWithTiebreak(string a, string b, string elected)
        {
            const string TieBreak = "__tie__";
            // set the tie-breakers to the expected state
            using (var aConn = ConnectionMultiplexer.Connect(TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort))
            {
                aConn.GetDatabase().StringSet(TieBreak, a);
            }
            using (var aConn = ConnectionMultiplexer.Connect(TestConfig.Current.MasterServer + ":" + TestConfig.Current.SecurePort + ",password=" + TestConfig.Current.SecurePassword))
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