using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class ValkeyUnitTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(ServerType.Standalone)]
    [InlineData(ServerType.Cluster)]
    public async Task IdentifyValkeyCluster(ServerType type)
    {
        using InProcessValkeyLikeServer server = new(log) { ServerType = type };
        await using var client = await server.ConnectAsync();
        var serverApi = client.GetServer(server.DefaultEndPoint);
        Assert.Equal(type, serverApi.ServerType);
        Assert.Equal(ProductVariant.Valkey, serverApi.GetProductVariant(out var version));
        Assert.Equal("8.1", version);
        Assert.Equal(server.RedisVersion, serverApi.Version); // not the valkey version from HELLO (#3278)
    }

    private sealed class InProcessValkeyLikeServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        // see https://github.com/StackExchange/StackExchange.Redis/pull/3050
        protected override string ServerModeKey => "server_mode";

        // Valkey reports its own version as HELLO "version" (see #3278); the name match is case-insensitive
        protected override string HelloServerName => "Valkey";
        protected override string HelloVersionString => "8.1";

        protected override void Info(StringBuilder sb, string section, Server.RedisClient? client = null)
        {
            base.Info(sb, section, client);
            if (section is "Server")
            {
                sb.AppendLine("valkey_version:8.1")
                    .AppendLine("server_name:valkey");
            }
        }
    }
}
