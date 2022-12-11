using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class MultiPrimaryTests : TestBase
{
    protected override string GetConfiguration() =>
        TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.SecureServerAndPort + ",password=" + TestConfig.Current.SecurePassword;
    public MultiPrimaryTests(ITestOutputHelper output) : base (output) { }

    [Fact]
    public void CannotFlushReplica()
    {
        var ex = Assert.Throws<RedisCommandException>(() =>
        {
            using var conn = ConnectionMultiplexer.Connect(TestConfig.Current.ReplicaServerAndPort + ",allowAdmin=true");

            var servers = conn.GetEndPoints().Select(e => conn.GetServer(e));
            var replica = servers.FirstOrDefault(x => x.IsReplica);
            Assert.NotNull(replica); // replica not found, ruh roh
            replica.FlushDatabase();
        });
        Assert.Equal("Command cannot be issued to a replica: FLUSHDB", ex.Message);
    }

    [Fact]
    public void TestMultiNoTieBreak()
    {
        var log = new StringBuilder();
        Writer.EchoTo(log);
        using (Create(log: Writer, tieBreaker: ""))
        {
            Assert.Contains("Choosing primary arbitrarily", log.ToString());
        }
    }

    public static IEnumerable<object?[]> GetConnections()
    {
        yield return new object[] { TestConfig.Current.PrimaryServerAndPort, TestConfig.Current.PrimaryServerAndPort, TestConfig.Current.PrimaryServerAndPort };
        yield return new object[] { TestConfig.Current.SecureServerAndPort, TestConfig.Current.SecureServerAndPort, TestConfig.Current.SecureServerAndPort };
        yield return new object?[] { TestConfig.Current.SecureServerAndPort, TestConfig.Current.PrimaryServerAndPort, null };
        yield return new object?[] { TestConfig.Current.PrimaryServerAndPort, TestConfig.Current.SecureServerAndPort, null };

        yield return new object?[] { null, TestConfig.Current.PrimaryServerAndPort, TestConfig.Current.PrimaryServerAndPort };
        yield return new object?[] { TestConfig.Current.PrimaryServerAndPort, null, TestConfig.Current.PrimaryServerAndPort };
        yield return new object?[] { null, TestConfig.Current.SecureServerAndPort, TestConfig.Current.SecureServerAndPort };
        yield return new object?[] { TestConfig.Current.SecureServerAndPort, null, TestConfig.Current.SecureServerAndPort };
        yield return new object?[] { null, null, null };
    }

    [Theory, MemberData(nameof(GetConnections))]
    public void TestMultiWithTiebreak(string a, string b, string elected)
    {
        const string TieBreak = "__tie__";
        // set the tie-breakers to the expected state
        using (var aConn = ConnectionMultiplexer.Connect(TestConfig.Current.PrimaryServerAndPort))
        {
            aConn.GetDatabase().StringSet(TieBreak, a);
        }
        using (var aConn = ConnectionMultiplexer.Connect(TestConfig.Current.SecureServerAndPort + ",password=" + TestConfig.Current.SecurePassword))
        {
            aConn.GetDatabase().StringSet(TieBreak, b);
        }

        // see what happens
        var log = new StringBuilder();
        Writer.EchoTo(log);

        using (Create(log: Writer, tieBreaker: TieBreak))
        {
            string text = log.ToString();
            Assert.False(text.Contains("failed to nominate"), "failed to nominate");
            if (elected != null)
            {
                Assert.True(text.Contains("Elected: " + elected), "elected");
            }
            int nullCount = (a == null ? 1 : 0) + (b == null ? 1 : 0);
            if ((a == b && nullCount == 0) || nullCount == 1)
            {
                Assert.True(text.Contains("Election: Tie-breaker unanimous"), "unanimous");
                Assert.False(text.Contains("Election: Choosing primary arbitrarily"), "arbitrarily");
            }
            else
            {
                Assert.False(text.Contains("Election: Tie-breaker unanimous"), "unanimous");
                Assert.True(text.Contains("Election: Choosing primary arbitrarily"), "arbitrarily");
            }
        }
    }
}
