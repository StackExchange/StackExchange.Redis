using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ServerEndPointClusterProbeUnitTests
{
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
        var keyBytes = (byte[]?)key;
        var prefixBytes = (byte[]?)ServerSelectionStrategy.GetHashTagPrefix(targetSlot);
        Assert.NotNull(keyBytes);
        Assert.NotNull(prefixBytes);
        Assert.True(keyBytes.Take(prefixBytes.Length).SequenceEqual(prefixBytes));
        Assert.True(keyBytes.Skip(keyBytes.Length - connection.UniqueId.Length).SequenceEqual(connection.UniqueId));
    }

    [Theory]
    [InlineData(ServerType.Standalone)]
    [InlineData(ServerType.Cluster)]
    public async Task ExistsTracerUsesPlainKeyWithoutKnownOwnedSlots(ServerType serverType)
    {
        using var server = new InProcessTestServer();
        var config = server.GetClientConfig(defaultOnly: true);
        var commands = server.GetCommands();
        commands.Remove(nameof(RedisCommand.ECHO));
        commands.Remove(nameof(RedisCommand.PING));
        commands.Remove(nameof(RedisCommand.TIME));
        config.CommandMap = CommandMap.Create(commands);

        await using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var endpoint = new ServerEndPoint(connection, new IPEndPoint(IPAddress.Loopback, 12345))
        {
            ServerType = serverType,
        };

        var message = endpoint.GetTracerMessage(checkResponse: true);
        var clusterStrategy = new ServerSelectionStrategy(null) { ServerType = ServerType.Cluster };

        Assert.Equal(RedisCommand.EXISTS, message.Command);
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)connection.UniqueId), message.GetHashSlot(clusterStrategy));
        Assert.Equal(connection.UniqueId, (byte[]?)endpoint.GetTracerKey());
    }
}
