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
    }
}
