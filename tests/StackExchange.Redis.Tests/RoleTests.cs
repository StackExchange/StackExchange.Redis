using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class Roles : TestBase
{
    public Roles(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrimaryRole(bool allowAdmin) // should work with or without admin now
    {
        using var conn = Create(allowAdmin: allowAdmin);
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);

        var role = server.Role();
        Assert.NotNull(role);
        Assert.Equal(role.Value, RedisLiterals.master);
        var primary = role as Role.Master;
        Assert.NotNull(primary);
        Assert.NotNull(primary.Replicas);
        Assert.NotEmpty(primary.Replicas);
        foreach (var replica in primary.Replicas)
        {
            Log(replica.ToString());
        }
        Assert.Contains(primary.Replicas, r =>
            r.Ip == TestConfig.Current.ReplicaServer &&
            r.Port == TestConfig.Current.ReplicaPort);
    }

    [Fact]
    public void ReplicaRole()
    {
        using var conn = ConnectionMultiplexer.Connect($"{TestConfig.Current.ReplicaServerAndPort},allowAdmin=true");
        var server = conn.GetServer(TestConfig.Current.ReplicaServerAndPort);

        var role = server.Role();
        Assert.NotNull(role);
        var replica = role as Role.Replica;
        Assert.NotNull(replica);
        Assert.Equal(replica.MasterIp, TestConfig.Current.PrimaryServer);
        Assert.Equal(replica.MasterPort, TestConfig.Current.PrimaryPort);
    }
}
