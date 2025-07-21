using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ConnectCustomConfigTests(ITestOutputHelper output) : TestBase(output)
{
    // So we're triggering tiebreakers here
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

    [Theory]
    [InlineData("config")]
    [InlineData("info")]
    [InlineData("get")]
    [InlineData("config,get")]
    [InlineData("info,get")]
    [InlineData("config,info,get")]
    public async Task DisabledCommandsStillConnect(string disabledCommands)
    {
        await using var conn = Create(allowAdmin: true, disabledCommands: disabledCommands.Split(','), log: Writer);

        var db = conn.GetDatabase();
        await db.PingAsync();
        Assert.True(db.IsConnected(default(RedisKey)));
    }

    [Theory]
    [InlineData("config")]
    [InlineData("info")]
    [InlineData("get")]
    [InlineData("cluster")]
    [InlineData("config,get")]
    [InlineData("info,get")]
    [InlineData("config,info,get")]
    [InlineData("config,info,get,cluster")]
    public async Task DisabledCommandsStillConnectCluster(string disabledCommands)
    {
        await using var conn = Create(allowAdmin: true, configuration: TestConfig.Current.ClusterServersAndPorts, disabledCommands: disabledCommands.Split(','), log: Writer);

        var db = conn.GetDatabase();
        await db.PingAsync();
        Assert.True(db.IsConnected(default(RedisKey)));
    }

    [Fact]
    public async Task TieBreakerIntact()
    {
        await using var conn = Create(allowAdmin: true, log: Writer);

        var tiebreaker = conn.GetDatabase().StringGet(conn.RawConfig.TieBreaker);
        Log($"Tiebreaker: {tiebreaker}");

        foreach (var server in conn.GetServerSnapshot())
        {
            Assert.Equal(tiebreaker, server.TieBreakerResult);
        }
    }

    [Fact]
    public async Task TieBreakerSkips()
    {
        await using var conn = Create(allowAdmin: true, disabledCommands: ["get"], log: Writer);
        Assert.Throws<RedisCommandException>(() => conn.GetDatabase().StringGet(conn.RawConfig.TieBreaker));

        foreach (var server in conn.GetServerSnapshot())
        {
            Assert.True(server.IsConnected);
            Assert.Null(server.TieBreakerResult);
        }
    }

    [Fact]
    public async Task TiebreakerIncorrectType()
    {
        var tiebreakerKey = Me();
        await using var fubarConn = Create(allowAdmin: true, log: Writer);
        // Store something nonsensical in the tiebreaker key:
        fubarConn.GetDatabase().HashSet(tiebreakerKey, "foo", "bar");

        // Ensure the next connection getting an invalid type still connects
        await using var conn = Create(allowAdmin: true, tieBreaker: tiebreakerKey, log: Writer);

        var db = conn.GetDatabase();
        await db.PingAsync();
        Assert.True(db.IsConnected(default(RedisKey)));

        var ex = Assert.Throws<RedisServerException>(() => db.StringGet(tiebreakerKey));
        Assert.Contains("WRONGTYPE", ex.Message);
    }

    [Theory]
    [InlineData(true, 2, 15)]
    [InlineData(false, 0, 0)]
    public async Task HeartbeatConsistencyCheckPingsAsync(bool enableConsistencyChecks, int minExpected, int maxExpected)
    {
        var options = new ConfigurationOptions()
        {
            HeartbeatConsistencyChecks = enableConsistencyChecks,
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
        };
        options.EndPoints.Add(TestConfig.Current.PrimaryServerAndPort);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);

        var db = conn.GetDatabase();
        await db.PingAsync();
        Assert.True(db.IsConnected(default));

        var preCount = conn.OperationCount;
        Log("OperationCount (pre-delay): " + preCount);

        // Allow several heartbeats to happen, but don't need to be strict here
        // e.g. allow thread pool starvation flex with the test suite's load (just check for a few)
        await Task.Delay(TimeSpan.FromSeconds(1));

        var postCount = conn.OperationCount;
        Log("OperationCount (post-delay): " + postCount);

        var opCount = postCount - preCount;
        Log("OperationCount (diff): " + opCount);

        Assert.True(minExpected <= opCount && opCount >= minExpected, $"Expected opcount ({opCount}) between {minExpected}-{maxExpected}");
    }
}
