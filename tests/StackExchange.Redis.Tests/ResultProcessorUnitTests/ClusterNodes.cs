using System.Linq;
using System.Net;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// <c>CLUSTER NODES</c>, as the new core reads it: the text via <see cref="RespHandlers.String"/>
/// (<c>Diagnostics.ClusterNodesRaw</c>), then <see cref="ClusterConfiguration"/> over it
/// (<c>RedisServer.ParseClusterNodes</c>).
/// </summary>
/// <remarks>
/// The shipped <c>ClusterNodesProcessor</c> could not be unit tested - it needed a live bridge to find the
/// server it then recorded the configuration against. The new core separates the parse from that
/// side-effect (<c>server.SetClusterConfiguration</c>, still in <c>RedisServer</c>), so the parse can be.
/// </remarks>
public class ClusterNodes(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    private static readonly EndPoint Origin = new IPEndPoint(IPAddress.Loopback, 7000);

    private static ClusterConfiguration Parse(string resp)
    {
        var nodes = Execute(resp, RespHandlers.String);
        Assert.False(string.IsNullOrWhiteSpace(nodes));
        return new ClusterConfiguration(new ServerSelectionStrategy(null), nodes!, Origin);
    }

    [Fact]
    public void PrimaryAndReplica_Success()
    {
        var text =
            "e7d1eecce10fd6bb5eb35b9f99a514335d9ba9ca 127.0.0.1:7000@17000 myself,master - 0 0 1 connected 0-8191\n" +
            "67ed2db8d677e59ec4a4cefb06858cf2a1a89fa1 127.0.0.1:7001@17001 master - 0 1426238316232 2 connected 8192-16383\n" +
            "292f8b365bb7edb5e285caf0b7e6ddc7265d2f4f 127.0.0.1:7002@17002 slave e7d1eecce10fd6bb5eb35b9f99a514335d9ba9ca 0 1426238317239 1 connected\n";
        var config = Parse($"${text.Length}\r\n{text}\r\n");

        Assert.Equal(3, config.Nodes.Count);
        Assert.Equal(Origin, config.Origin);

        var self = config.Nodes.Single(n => n.IsMyself);
        Assert.Equal("e7d1eecce10fd6bb5eb35b9f99a514335d9ba9ca", self.NodeId);
        Assert.False(self.IsReplica);
        Assert.Equal(new SlotRange(0, 8191), Assert.Single(self.Slots));

        var replica = config.Nodes.Single(n => n.IsReplica);
        Assert.Equal(self.NodeId, replica.ParentNodeId);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 7002), replica.EndPoint);

        Assert.Equal(self.EndPoint, config.GetBySlot(100)?.EndPoint);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 7001), config.GetBySlot(10000)?.EndPoint);
    }

    [Fact]
    public void VerbatimString_Success()
    {
        // RESP3 servers answer CLUSTER NODES as verbatim text
        var text = "e7d1eecce10fd6bb5eb35b9f99a514335d9ba9ca 127.0.0.1:7000@17000 myself,master - 0 0 1 connected 0-16383\n";
        var config = Parse($"={text.Length + 4}\r\ntxt:{text}\r\n");

        var self = Assert.Single(config.Nodes);
        Assert.True(self.IsMyself);
        Assert.Equal(new SlotRange(0, 16383), Assert.Single(self.Slots));
    }

    [Fact]
    public void NullBulkString_NoConfiguration()
    {
        // RedisServer.ParseClusterNodes answers null for no text, rather than an empty configuration
        Assert.Null(Execute("$-1\r\n", RespHandlers.String));
    }
}
