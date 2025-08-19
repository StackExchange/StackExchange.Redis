using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public abstract class GetServerTestsBase(ITestOutputHelper output, SharedConnectionFixture fixture)
    : TestBase(output, fixture)
{
    protected abstract bool IsCluster { get; }

    [Fact]
    public async Task GetServersMemoization()
    {
        await using var conn = Create();

        var servers0 = conn.GetServers();
        var servers1 = conn.GetServers();

        // different array, exact same contents
        Assert.NotSame(servers0, servers1);
        Assert.NotEmpty(servers0);
        Assert.NotNull(servers0);
        Assert.NotNull(servers1);
        Assert.Equal(servers0.Length, servers1.Length);
        for (int i = 0; i < servers0.Length; i++)
        {
            Assert.Same(servers0[i], servers1[i]);
        }
    }

    [Fact]
    public async Task GetServerByEndpointMemoization()
    {
        await using var conn = Create();
        var ep = conn.GetEndPoints().First();

        IServer x = conn.GetServer(ep), y = conn.GetServer(ep);
        Assert.Same(x, y);

        object asyncState = "whatever";
        x = conn.GetServer(ep, asyncState);
        y = conn.GetServer(ep, asyncState);
        Assert.NotSame(x, y);
    }

    [Fact]
    public async Task GetServerByKeyMemoization()
    {
        await using var conn = Create();
        RedisKey key = Me();
        string value = $"{key}:value";
        await conn.GetDatabase().StringSetAsync(key, value);

        IServer x = conn.GetServer(key), y = conn.GetServer(key);
        Assert.False(y.IsReplica, "IsReplica");
        Assert.Same(x, y);

        y = conn.GetServer(key, flags: CommandFlags.DemandMaster);
        Assert.Same(x, y);

        // async state demands separate instance
        y = conn.GetServer(key, "async state", flags: CommandFlags.DemandMaster);
        Assert.NotSame(x, y);

        // primary and replica should be different
        y = conn.GetServer(key, flags: CommandFlags.DemandReplica);
        Assert.NotSame(x, y);
        Assert.True(y.IsReplica, "IsReplica");

        // replica again: same
        var z = conn.GetServer(key, flags: CommandFlags.DemandReplica);
        Assert.Same(y, z);

        // check routed correctly
        var actual = (string?)await x.ExecuteAsync(null, "get", [key], CommandFlags.NoRedirect);
        Assert.Equal(value, actual); // check value against primary

        // for replica, don't check the value, because of replication delay - just: no error
        _ = y.ExecuteAsync(null, "get", [key], CommandFlags.NoRedirect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetServerWithDefaultKey(bool explicitNull)
    {
        await using var conn = Create();
        bool isCluster = conn.ServerSelectionStrategy.ServerType == ServerType.Cluster;
        Assert.Equal(IsCluster, isCluster); // check our assumptions!

        // we expect explicit null and default to act the same, but: check
        RedisKey key = explicitNull ? RedisKey.Null : default(RedisKey);

        IServer primary = conn.GetServer(key);
        Assert.False(primary.IsReplica);

        IServer replica = conn.GetServer(key, flags: CommandFlags.DemandReplica);
        Assert.True(replica.IsReplica);

        // check multiple calls
        HashSet<IServer> uniques = [];
        for (int i = 0; i < 100; i++)
        {
            uniques.Add(conn.GetServer(key));
        }

        if (isCluster)
        {
            Assert.True(uniques.Count > 1); // should be able to get arbitrary servers
        }
        else
        {
            Assert.Single(uniques);
        }

        uniques.Clear();
        for (int i = 0; i < 100; i++)
        {
            uniques.Add(conn.GetServer(key, flags: CommandFlags.DemandReplica));
        }

        if (isCluster)
        {
            Assert.True(uniques.Count > 1); // should be able to get arbitrary servers
        }
        else
        {
            Assert.Single(uniques);
        }
    }
}

[RunPerProtocol]
public class GetServerTestsCluster(ITestOutputHelper output, SharedConnectionFixture fixture) : GetServerTestsBase(output, fixture)
{
    protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts;

    protected override bool IsCluster => true;
}

[RunPerProtocol]
public class GetServerTestsStandalone(ITestOutputHelper output, SharedConnectionFixture fixture) : GetServerTestsBase(output, fixture)
{
    protected override string GetConfiguration() => // we want to test flags usage including replicas
        TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

    protected override bool IsCluster => false;
}
