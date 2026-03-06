using System;
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
    public async Task SelectByWeight()
    {
        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(endpoint: germany);
        using var server1 = new InProcessTestServer(endpoint: canada);
        using var server2 = new InProcessTestServer(endpoint: tokyo);

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()) { Weight = 2 },
            new(server1.GetClientConfig()) { Weight = 9 },
            new(server2.GetClientConfig()) { Weight = 3 },
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
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

        WriteLatency(conn);
    }

    private void WriteLatency(IConnectionGroup conn)
    {
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        foreach (var member in conn.GetMembers())
        {
            log.WriteLine($"{member.Name}: {member.Latency.TotalMilliseconds}us");
        }
        log.WriteLine($"Active: {typed.Active}");
    }

    [Fact]
    public async Task SelectByLatency()
    {
        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(endpoint: germany);
        using var server1 = new InProcessTestServer(endpoint: canada);
        using var server2 = new InProcessTestServer(endpoint: tokyo);

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()),
            new(server1.GetClientConfig()),
            new(server2.GetClientConfig()),
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
        conn.ConnectionChanged += (_, args) => log.WriteLine($"Connection changed: {args.Type}, from {args.PreviousGroup?.Name ?? "(nil)"} to {args.Group.Name}");

        Assert.True(conn.IsConnected);
        server0.SetLatency(TimeSpan.FromMilliseconds(10));
        server0.SetLatency(TimeSpan.Zero);
        server2.SetLatency(TimeSpan.FromMilliseconds(15));
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        typed.OnHeartbeat(); // update latencies
        await Task.Delay(100); // allow time to settle
        typed.SelectPreferredGroup();
        WriteLatency(typed);

        // (R.4.1) If multiple member databases are configured, then I want to failover to the one with the highest weight.
        var db = conn.GetDatabase();
        var ep = await db.IdentifyEndpointAsync();
        Assert.Equal(canada, ep);

        // change latency and update
        server0.SetLatency(TimeSpan.FromMilliseconds(10));
        server1.SetLatency(TimeSpan.FromMilliseconds(10));
        server2.SetLatency(TimeSpan.Zero);
        typed.OnHeartbeat(); // update latencies
        await Task.Delay(100); // allow time to settle
        typed.SelectPreferredGroup();
        ep = await db.IdentifyEndpointAsync();
        WriteLatency(typed);
        Assert.Equal(tokyo, ep);
    }
}
