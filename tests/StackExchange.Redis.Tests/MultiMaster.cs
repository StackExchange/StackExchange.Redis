using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class MultiMaster : TestBase
    {
        protected override string GetConfiguration() =>
            TestConfig.Current.MasterServerAndPort + "," + TestConfig.Current.SecureServerAndPort + ",password=" + TestConfig.Current.SecurePassword;
        public MultiMaster(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void CannotFlushReplica()
        {
            var ex = Assert.Throws<RedisCommandException>(() =>
            {
                using (var conn = ConnectionMultiplexer.Connect(TestConfig.Current.ReplicaServerAndPort + ",allowAdmin=true"))
                {
                    var servers = conn.GetEndPoints().Select(e => conn.GetServer(e));
                    var replica = servers.FirstOrDefault(x => x.IsReplica);
                    Assert.NotNull(replica); // replica not found, ruh roh
                    replica.FlushDatabase();
                }
            });
            Assert.Equal("Command cannot be issued to a replica: FLUSHDB", ex.Message);
        }

        [Fact]
        public void TestMultiNoTieBreak()
        {
            using (var log = new StringWriter())
            using (Create(log: log, tieBreaker: ""))
            {
                Log(log.ToString());
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
            using (Create(log: log, tieBreaker: TieBreak))
            {
                string text = log.ToString();
                Log(text);
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
