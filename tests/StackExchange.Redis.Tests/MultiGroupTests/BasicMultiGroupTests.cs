using System.IO;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

public class BasicMultiGroupTests(ITestOutputHelper log)
{
    protected TextWriter Log { get; } = new TextWriterOutputHelper(log);

    [Fact]
    public async Task BasicSmokeTest()
    {
        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(log, germany);
        using var server1 = new InProcessTestServer(log, canada);
        using var server2 = new InProcessTestServer(log, tokyo);

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()) { Weight = 2 },
            new(server1.GetClientConfig()) { Weight = 9 },
            new(server2.GetClientConfig()) { Weight = 3 },
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, Log);
        Assert.True(conn.IsConnected);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);

        // (R.4.1) If multiple member databases are configured, then I want to failover to the one with the highest weight.
        var db = conn.GetDatabase();
        var ep = await db.IdentifyEndpointAsync();
        Assert.Equal(canada, ep);

        // change weight and update
        members[1].Weight = 1;
        typed.SelectPreferredGroup();
        ep = await db.IdentifyEndpointAsync();
        Assert.Equal(tokyo, ep);
    }
}
