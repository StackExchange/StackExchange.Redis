using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class MultiMaster : TestBase
    {
        protected override string GetConfiguration() =>
            TestConfig.Current.MasterServerAndPort + "," + TestConfig.Current.SecureServerAndPort + ",password=" + TestConfig.Current.SecurePassword;

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
        public async Task DeslaveGoesToPrimary()
        {
            ConfigurationOptions config = GetMasterSlaveConfig();
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var primary = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                var secondary = conn.GetServer(TestConfig.Current.SlaveServerAndPort);

                primary.Ping();
                secondary.Ping();

                primary.MakeMaster(ReplicationChangeOptions.SetTiebreaker);
                secondary.MakeMaster(ReplicationChangeOptions.None);

                await Task.Delay(100).ConfigureAwait(false);

                primary.Ping();
                secondary.Ping();

                using (var writer = new StringWriter())
                {
                    conn.Configure(writer);
                    string log = writer.ToString();

                    Assert.True(log.Contains("tie-break is unanimous at " + TestConfig.Current.MasterServerAndPort), "unanimous");
                }
                // k, so we know everyone loves 6379; is that what we get?

                var db = conn.GetDatabase();
                RedisKey key = Me();

                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferSlave));

                var ex = Assert.Throws<RedisConnectionException>(() => db.IdentifyEndpoint(key, CommandFlags.DemandSlave));
                Assert.StartsWith("No connection is available to service this operation: EXISTS DeslaveGoesToPrimary", ex.Message);
                Writer.WriteLine("Invoking MakeMaster()...");
                primary.MakeMaster(ReplicationChangeOptions.Broadcast | ReplicationChangeOptions.EnslaveSubordinates | ReplicationChangeOptions.SetTiebreaker, Writer);
                Writer.WriteLine("Finished MakeMaster() call.");

                await Task.Delay(100).ConfigureAwait(false);

                Writer.WriteLine("Invoking Ping() (post-master)");
                primary.Ping();
                secondary.Ping();
                Writer.WriteLine("Finished Ping() (post-master)");

                Assert.True(primary.IsConnected, $"{primary.EndPoint} is not connected.");
                Assert.True(secondary.IsConnected, $"{secondary.EndPoint} is not connected.");

                Writer.WriteLine($"{primary.EndPoint}: {primary.ServerType}, Mode: {(primary.IsSlave ? "Slave" : "Master")}");
                Writer.WriteLine($"{secondary.EndPoint}: {secondary.ServerType}, Mode: {(secondary.IsSlave ? "Slave" : "Master")}");

                // Create a separate multiplexer with a valid view of the world to distinguish between failures of
                // server topology changes from failures to recognize those changes
                Writer.WriteLine("Connecting to secondary validation connection.");
                using (var conn2 = ConnectionMultiplexer.Connect(config))
                {
                    var primary2 = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                    var secondary2 = conn.GetServer(TestConfig.Current.SlaveServerAndPort);

                    Writer.WriteLine($"Check: {primary2.EndPoint}: {primary2.ServerType}, Mode: {(primary2.IsSlave ? "Slave" : "Master")}");
                    Writer.WriteLine($"Check: {secondary2.EndPoint}: {secondary2.ServerType}, Mode: {(secondary2.IsSlave ? "Slave" : "Master")}");

                    Assert.False(primary2.IsSlave, $"{primary2.EndPoint} should be a master (verification connection).");
                    Assert.True(secondary2.IsSlave, $"{secondary2.EndPoint} should be a slave (verification connection).");

                    var db2 = conn.GetDatabase();

                    Assert.Equal(primary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                    Assert.Equal(primary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                    Assert.Equal(secondary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.PreferSlave));
                    Assert.Equal(secondary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.DemandSlave));
                }

                Assert.False(primary.IsSlave, $"{primary.EndPoint} should be a master.");
                Assert.True(secondary.IsSlave, $"{secondary.EndPoint} should be a slave.");

                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                Assert.Equal(secondary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferSlave));
                Assert.Equal(secondary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandSlave));
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
                    { TestConfig.Current.SlaveServer, TestConfig.Current.SlavePort },
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
            yield return new object[] { TestConfig.Current.MasterServerAndPort, TestConfig.Current.MasterServerAndPort, TestConfig.Current.MasterServerAndPort };
            yield return new object[] { TestConfig.Current.SecureServerAndPort, TestConfig.Current.SecureServerAndPort, TestConfig.Current.SecureServerAndPort };
            yield return new object[] { TestConfig.Current.SecureServerAndPort, TestConfig.Current.MasterServerAndPort, null };
            yield return new object[] { TestConfig.Current.MasterServerAndPort, TestConfig.Current.SecureServerAndPort, null };

            yield return new object[] { null, TestConfig.Current.MasterServerAndPort, TestConfig.Current.MasterServerAndPort };
            yield return new object[] { TestConfig.Current.MasterServerAndPort, null, TestConfig.Current.MasterServerAndPort };
            yield return new object[] { null, TestConfig.Current.SecureServerAndPort, TestConfig.Current.SecureServerAndPort };
            yield return new object[] { TestConfig.Current.SecureServerAndPort, null, TestConfig.Current.SecureServerAndPort };
            yield return new object[] { null, null, null };
        }

        [Theory, MemberData(nameof(GetConnections))]
        public void TestMultiWithTiebreak(string a, string b, string elected)
        {
            const string TieBreak = "__tie__";
            // set the tie-breakers to the expected state
            using (var aConn = ConnectionMultiplexer.Connect(TestConfig.Current.MasterServerAndPort))
            {
                aConn.GetDatabase().StringSet(TieBreak, a);
            }
            using (var aConn = ConnectionMultiplexer.Connect(TestConfig.Current.SecureServerAndPort + ",password=" + TestConfig.Current.SecurePassword))
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
