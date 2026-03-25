using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace StackExchange.Redis.Tests;

// context: https://github.com/StackExchange/StackExchange.Redis/pull/3043
public class ClusterHandshakeNodesUnitTests(ITestOutputHelper log)
{
    [Fact]
    public async Task ClusterHandshakeNodesAreIgnored()
    {
        using var server = new InProcessTestServer() { ServerType = ServerType.Cluster };
        var a = server.DefaultEndPoint;
        var b = server.AddEmptyNode();
        var c = server.AddEmptyNode(Server.RedisServer.NodeFlags.Handshake);
        using var conn = await server.ConnectAsync(defaultOnly: true); // defaultOnly: only connect to a initially

        log.WriteLine($"a: {Format.ToString(a)}, b: {Format.ToString(b)}, c: {Format.ToString(c)}");
        var ep = conn.GetEndPoints();
        log.WriteLine("Endpoints:");
        foreach (var e in ep)
        {
            log.WriteLine(Format.ToString(e));
        }
        Assert.Equal(2, ep.Length);
        Assert.Contains(a, ep);
        Assert.Contains(b, ep);
        Assert.DoesNotContain(c, ep);

        // check we can still *fetch* handshake nodes via the admin API
        var serverApi = conn.GetServer(a);
        var config = await serverApi.ClusterNodesAsync();
        Assert.NotNull(config);
        Assert.Equal(3, config.Nodes.Count);
        var eps = config.Nodes.Select(x => x.EndPoint).ToArray();
        Assert.Contains(a, eps);
        Assert.Contains(b, eps);
        Assert.Contains(c, eps);

        Assert.False(config[a]!.IsHandshake);
        Assert.False(config[b]!.IsHandshake);
        Assert.True(config[c]!.IsHandshake);
    }
}
