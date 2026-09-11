using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ServerEndPointClusterProbeUnitTests
{
    [Fact]
    public async Task AutoConfigureSkipsKeyProbesWhenClusterTopologyKnown()
    {
        const string tieBreakerKey = "cluster-tie-breaker";
        using var server = new RecordingServer { ServerType = ServerType.Cluster };
        var config = server.GetClientConfig(defaultOnly: true);
        var commands = server.GetCommands();
        commands.Remove(nameof(RedisCommand.INFO));
        config.CommandMap = CommandMap.Create(commands);
        config.Protocol = RedisProtocol.Resp2;
        config.TieBreaker = tieBreakerKey;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var endpoint = connection.GetServerEndPoint(server.DefaultEndPoint);
        Assert.NotNull(await connection.GetServer(server.DefaultEndPoint).ClusterNodesAsync());
        Assert.Equal(ServerType.Cluster, endpoint.ServerType);
        Assert.NotNull(endpoint.ClusterConfiguration?[endpoint.EndPoint]);
        endpoint.RoleKnownFromHello = false;
        server.ClearRecorded();

        await endpoint.AutoConfigureAsync(null);
        await connection.GetDatabase().PingAsync();

        Assert.DoesNotContain(server.Recorded("GET"), args => args.Contains(tieBreakerKey, StringComparer.Ordinal));
        Assert.DoesNotContain(server.Recorded("SET"), args => args.Contains("replica_read_only", StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExistsTracerUsesOwnedClusterSlot()
    {
        using var server = new InProcessTestServer { ServerType = ServerType.Cluster };
        var config = server.GetClientConfig(defaultOnly: true);
        var commands = server.GetCommands();
        commands.Remove(nameof(RedisCommand.ECHO));
        commands.Remove(nameof(RedisCommand.PING));
        commands.Remove(nameof(RedisCommand.TIME));
        config.CommandMap = CommandMap.Create(commands);

        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var endpoint = connection.GetServerEndPoint(server.DefaultEndPoint);
        var node = endpoint.ClusterConfiguration?.Nodes.Single(x => x.EndPoint?.Equals(endpoint.EndPoint) == true);
        Assert.NotNull(node);
        var targetSlot = node.Slots[0].From;

        var message = endpoint.GetTracerMessage(checkResponse: true);

        Assert.Equal(RedisCommand.EXISTS, message.Command);
        Assert.Equal(targetSlot, message.GetHashSlot(connection.ServerSelectionStrategy));
        var key = endpoint.GetTracerKey();
        Assert.Equal(ServerSelectionStrategy.CreateKeyForSlot(targetSlot, connection.UniqueId), key);
    }

    [Fact]
    public async Task ExistsTracerUsesPlainKeyWithoutKnownOwnedSlots()
    {
        using var server = new InProcessTestServer();
        var config = server.GetClientConfig(defaultOnly: true);
        var commands = server.GetCommands();
        commands.Remove(nameof(RedisCommand.ECHO));
        commands.Remove(nameof(RedisCommand.PING));
        commands.Remove(nameof(RedisCommand.TIME));
        config.CommandMap = CommandMap.Create(commands);

        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        using var endpoint = new ServerEndPoint(connection, new IPEndPoint(IPAddress.Loopback, 12345))
        {
            ServerType = ServerType.Standalone,
        };

        var message = endpoint.GetTracerMessage(checkResponse: true);
        var clusterStrategy = new ServerSelectionStrategy(null) { ServerType = ServerType.Cluster };

        Assert.Equal(RedisCommand.EXISTS, message.Command);
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)connection.UniqueId), message.GetHashSlot(clusterStrategy));
        Assert.Equal(connection.UniqueId, (byte[]?)endpoint.GetTracerKey());
    }

    [Fact]
    public async Task ExistsTracerUsesPlainKeyWhileClusterTopologyIsNotKnown()
    {
        using var server = new InProcessTestServer();
        var config = server.GetClientConfig(defaultOnly: true);
        var commands = server.GetCommands();
        commands.Remove(nameof(RedisCommand.ECHO));
        commands.Remove(nameof(RedisCommand.PING));
        commands.Remove(nameof(RedisCommand.TIME));
        config.CommandMap = CommandMap.Create(commands);

        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        using var endpoint = new ServerEndPoint(connection, new IPEndPoint(IPAddress.Loopback, 12345))
        {
            ServerType = ServerType.Cluster,
        };

        // Cluster mode can be known before the first CLUSTER NODES reply supplies this endpoint's topology.
        Assert.Null(endpoint.ClusterConfiguration);
        var message = endpoint.GetTracerMessage(checkResponse: true);
        var clusterStrategy = new ServerSelectionStrategy(null) { ServerType = ServerType.Cluster };

        Assert.Equal(RedisCommand.EXISTS, message.Command);
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)connection.UniqueId), message.GetHashSlot(clusterStrategy));
        Assert.Equal(connection.UniqueId, (byte[]?)endpoint.GetTracerKey());
    }

    [Fact]
    public async Task ExistsTracerOnClusterReplicaUsesPrimarySlot()
    {
        const int targetSlot = 1234;
        using var server = new InProcessTestServer();
        var config = server.GetClientConfig(defaultOnly: true);
        var commands = server.GetCommands();
        commands.Remove(nameof(RedisCommand.ECHO));
        commands.Remove(nameof(RedisCommand.PING));
        commands.Remove(nameof(RedisCommand.TIME));
        config.CommandMap = CommandMap.Create(commands);

        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var primaryEndPoint = new IPEndPoint(IPAddress.Loopback, 12344);
        var replicaEndPoint = new IPEndPoint(IPAddress.Loopback, 12345);
        var clusterConfiguration = new ClusterConfiguration(
            connection.ServerSelectionStrategy,
            $"primary-id {primaryEndPoint} master - 0 0 1 connected {targetSlot}-{targetSlot}{Environment.NewLine}" +
            $"replica-id {replicaEndPoint} replica primary-id 0 0 2 connected",
            replicaEndPoint);
        using var endpoint = new ServerEndPoint(connection, replicaEndPoint)
        {
            ServerType = ServerType.Cluster,
            IsReplica = true,
        };
        endpoint.SetClusterConfiguration(clusterConfiguration);
        var replicaNode = clusterConfiguration[replicaEndPoint];

        Assert.NotNull(replicaNode);
        Assert.Empty(replicaNode.Slots);
        Assert.Equal(targetSlot, replicaNode.Parent?.Slots[0].From);
        Assert.Equal(targetSlot, endpoint.GetServableSlot());

        var message = endpoint.GetTracerMessage(checkResponse: true);
        var clusterStrategy = new ServerSelectionStrategy(null) { ServerType = ServerType.Cluster };

        Assert.Equal(RedisCommand.EXISTS, message.Command);
        Assert.Equal(targetSlot, message.GetHashSlot(clusterStrategy));
        Assert.Equal(ServerSelectionStrategy.CreateKeyForSlot(targetSlot, connection.UniqueId), endpoint.GetTracerKey());
    }

    private sealed class RecordingServer : InProcessTestServer
    {
        private readonly ConcurrentQueue<(string Command, string[] Args)> _commands = new();

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            var args = new string[Math.Max(request.Count - 1, 0)];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = request.GetString(i + 1);
            }
            _commands.Enqueue((request.GetString(0).ToUpperInvariant(), args));
            return base.Execute(client, in request);
        }

        public void ClearRecorded()
        {
            while (_commands.TryDequeue(out _)) { }
        }

        public string[][] Recorded(string command)
            => _commands.Where(x => x.Command == command).Select(x => x.Args).ToArray();
    }
}
