using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class ConnectCustomConfigTests : TestBase
{
    public ConnectCustomConfigTests(ITestOutputHelper output) : base (output) { }

    // So we're triggering tiebreakers here
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

    [Theory]
    [InlineData("config")]
    [InlineData("info")]
    [InlineData("get")]
    [InlineData("config,get")]
    [InlineData("info,get")]
    [InlineData("config,info,get")]
    public void DisabledCommandsStillConnect(string disabledCommands)
    {
        using var conn = Create(allowAdmin: true, disabledCommands: disabledCommands.Split(','), log: Writer);

        var db = conn.GetDatabase();
        db.Ping();
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
    public void DisabledCommandsStillConnectCluster(string disabledCommands)
    {
        using var conn = Create(allowAdmin: true, configuration: TestConfig.Current.ClusterServersAndPorts, disabledCommands: disabledCommands.Split(','), log: Writer);

        var db = conn.GetDatabase();
        db.Ping();
        Assert.True(db.IsConnected(default(RedisKey)));
    }

    [Fact]
    public void TieBreakerIntact()
    {
        using var conn = Create(allowAdmin: true, log: Writer);

        var tiebreaker = conn.GetDatabase().StringGet(conn.RawConfig.TieBreaker);
        Log($"Tiebreaker: {tiebreaker}");

        foreach (var server in conn.GetServerSnapshot())
        {
            Assert.Equal(tiebreaker, server.TieBreakerResult);
        }
    }

    [Fact]
    public void TieBreakerSkips()
    {
        using var conn = Create(allowAdmin: true, disabledCommands: new[] { "get" }, log: Writer);
        Assert.Throws<RedisCommandException>(() => conn.GetDatabase().StringGet(conn.RawConfig.TieBreaker));

        foreach (var server in conn.GetServerSnapshot())
        {
            Assert.True(server.IsConnected);
            Assert.Null(server.TieBreakerResult);
        }
    }

    [Fact]
    public void TiebreakerIncorrectType()
    {
        var tiebreakerKey = Me();
        using var fubarConn = Create(allowAdmin: true, log: Writer);
        // Store something nonsensical in the tiebreaker key:
        fubarConn.GetDatabase().HashSet(tiebreakerKey, "foo", "bar");

        // Ensure the next connection getting an invalid type still connects
        using var conn = Create(allowAdmin: true, tieBreaker: tiebreakerKey, log: Writer);

        var db = conn.GetDatabase();
        db.Ping();
        Assert.True(db.IsConnected(default(RedisKey)));

        var ex = Assert.Throws<RedisServerException>(() => db.StringGet(tiebreakerKey));
        Assert.Contains("WRONGTYPE", ex.Message);
    }
}
