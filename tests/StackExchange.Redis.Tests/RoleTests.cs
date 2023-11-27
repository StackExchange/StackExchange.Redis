using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class Roles : TestBase
{
    public Roles(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrimaryRole(bool allowAdmin) // should work with or without admin now
    {
        using var conn = Create(allowAdmin: allowAdmin);
        var servers = conn.GetServers();
        Log("Server list:");
        foreach (var s in servers)
        {
            Log($"  Server: {s.EndPoint} (isConnected: {s.IsConnected}, isReplica: {s.IsReplica})");
        }
        var server = servers.First(conn => !conn.IsReplica);
        var role = server.Role();
        Log($"Chosen primary: {server.EndPoint} (role: {role})");
        if (allowAdmin)
        {
            Log($"Info (Replication) dump for {server.EndPoint}:");
            Log(server.InfoRaw("Replication"));
            Log("");

            foreach (var s in servers)
            {
                if (s.IsReplica)
                {
                    Log($"Info (Replication) dump for {s.EndPoint}:");
                    Log(s.InfoRaw("Replication"));
                    Log("");
                }
            }
        }
        Assert.NotNull(role);
        Assert.Equal(role.Value, RedisLiterals.master);
        var primary = role as Role.Master;
        Assert.NotNull(primary);
        Assert.NotNull(primary.Replicas);

        // Only do this check for Redis > 4 (to exclude Redis 3.x on Windows).
        // Unrelated to this test, the replica isn't connecting and we'll revisit swapping the server out.
        // TODO: MemuraiDeveloper check
        if (server.Version > RedisFeatures.v4_0_0)
        {
            Log($"Searching for: {TestConfig.Current.ReplicaServer}:{TestConfig.Current.ReplicaPort}");
            Log($"Replica count: {primary.Replicas.Count}");

            Assert.NotEmpty(primary.Replicas);
            foreach (var replica in primary.Replicas)
            {
                Log($"  Replica: {replica.Ip}:{replica.Port} (offset: {replica.ReplicationOffset})");
                Log(replica.ToString());
            }
            Assert.Contains(primary.Replicas, r =>
                r.Ip == TestConfig.Current.ReplicaServer &&
                r.Port == TestConfig.Current.ReplicaPort);
        }
    }

    [Fact]
    public void ReplicaRole()
    {
        using var conn = ConnectionMultiplexer.Connect($"{TestConfig.Current.ReplicaServerAndPort},allowAdmin=true");
        var server = conn.GetServers().First(conn => conn.IsReplica);

        var role = server.Role();
        Assert.NotNull(role);
        var replica = role as Role.Replica;
        Assert.NotNull(replica);
        Assert.Equal(replica.MasterIp, TestConfig.Current.PrimaryServer);
        Assert.Equal(replica.MasterPort, TestConfig.Current.PrimaryPort);
    }
}
