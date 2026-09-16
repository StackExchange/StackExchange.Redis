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
        var endpoint = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
        Assert.NotNull(await connection.GetServer(server.DefaultEndPoint).ClusterNodesAsync());
        Assert.Equal(ServerType.Cluster, endpoint.ServerType);
        Assert.NotNull(endpoint.ClusterConfiguration?[endpoint.EndPoint]);
        endpoint.RoleKnownFromHello = false;
        server.ClearRecorded();

        await endpoint.AutoConfigureAsync(null);
        await connection.GetDatabase().PingAsync();

        Assert.DoesNotContain(server.Recorded("GET"), args => args.Contains(tieBreakerKey, StringComparer.Ordinal));

        // the wire spelling, not the C# identifier: RedisLiterals.replica_read_only carries
        // "replica-read-only", so matching on the underscored name never matched anything and this
        // assertion passed whatever the client sent
        Assert.DoesNotContain(server.Recorded("SET"), args => args.Contains(ReplicaReadOnly, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The value <see cref="RedisLiterals"/> actually puts on the wire for the role probe.</summary>
    private const string ReplicaReadOnly = "replica-read-only";

    [Fact]
    public async Task FirstHandshakeProbesBeforeClusterModeIsKnown()
    {
        // Pins the residue of #2970 rather than endorsing it. The guards added for that issue read the
        // per-endpoint ServerType, which is seeded Standalone and only becomes Cluster once the CLUSTER NODES
        // reply has been processed - and these probes are composed into the same pipeline batch that carries
        // that command, so they are already on the wire by then. The result is exactly one unslotted round
        // per new ServerEndPoint; every autoconfigure after it is clean, which is what
        // AutoConfigureSkipsKeyProbesWhenClusterTopologyKnown covers.
        //
        // If handshake ordering is ever changed so the topology lands first, this test should begin failing.
        // Invert it rather than deleting it: the point is that the behaviour is a deliberate, known state.
        const string tieBreakerKey = "cluster-tie-breaker";
        using var server = new RecordingServer { ServerType = ServerType.Cluster };
        var config = server.GetClientConfig(defaultOnly: true);
        var commands = server.GetCommands();
        commands.Remove(nameof(RedisCommand.INFO));   // so the SET role probe is the fallback
        commands.Remove(nameof(RedisCommand.CONFIG)); // ...and CONFIG GET does not answer first
        commands.Remove(nameof(RedisCommand.HELLO));  // ...and the role is not known from HELLO
        config.CommandMap = CommandMap.Create(commands);
        config.Protocol = RedisProtocol.Resp2;
        config.TieBreaker = tieBreakerKey;

        // deliberately not cleared: this is about what the *first* handshake puts on the wire
        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.Contains(server.Recorded("SET"), args => args.Contains(ReplicaReadOnly, StringComparer.OrdinalIgnoreCase));
        Assert.Contains(server.Recorded("GET"), args => args.Contains(tieBreakerKey, StringComparer.Ordinal));
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
        var endpoint = connection.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.Configured);
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
        using var endpoint = new ServerEndPoint(connection, new IPEndPoint(IPAddress.Loopback, 12345), ServerProvenance.Configured)
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
        using var endpoint = new ServerEndPoint(connection, new IPEndPoint(IPAddress.Loopback, 12345), ServerProvenance.Configured)
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
        using var endpoint = new ServerEndPoint(connection, replicaEndPoint, ServerProvenance.Configured)
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
