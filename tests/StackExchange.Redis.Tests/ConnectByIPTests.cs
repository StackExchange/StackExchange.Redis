using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ConnectByIPTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public void ParseEndpoints()
    {
        var eps = new EndPointCollection
        {
            { "127.0.0.1", 1000 },
            { "::1", 1001 },
            { "localhost", 1002 },
        };

        Assert.Equal(AddressFamily.InterNetwork, eps[0].AddressFamily);
        Assert.Equal(AddressFamily.InterNetworkV6, eps[1].AddressFamily);
        Assert.Equal(AddressFamily.Unspecified, eps[2].AddressFamily);

        Assert.Equal("127.0.0.1:1000", eps[0].ToString());
        Assert.Equal("[::1]:1001", eps[1].ToString());
        Assert.Equal("Unspecified/localhost:1002", eps[2].ToString());
    }

    [Fact]
    public async Task IPv4Connection()
    {
        var config = new ConfigurationOptions
        {
            EndPoints = { { TestConfig.Current.IPv4Server, TestConfig.Current.IPv4Port } },
        };
        await using var conn = ConnectionMultiplexer.Connect(config);

        var server = conn.GetServer(config.EndPoints[0]);
        Assert.Equal(AddressFamily.InterNetwork, server.EndPoint.AddressFamily);
        await server.PingAsync();
    }

    [Fact]
    public async Task IPv6Connection()
    {
        var config = new ConfigurationOptions
        {
            EndPoints = { { TestConfig.Current.IPv6Server, TestConfig.Current.IPv6Port } },
        };
        await using var conn = ConnectionMultiplexer.Connect(config);

        var server = conn.GetServer(config.EndPoints[0]);
        Assert.Equal(AddressFamily.InterNetworkV6, server.EndPoint.AddressFamily);
        await server.PingAsync();
    }

    [Theory]
    [MemberData(nameof(ConnectByVariousEndpointsData))]
    public async Task ConnectByVariousEndpoints(EndPoint ep, AddressFamily expectedFamily)
    {
        Assert.Equal(expectedFamily, ep.AddressFamily);
        var config = new ConfigurationOptions
        {
            EndPoints = { ep },
        };
        if (ep.AddressFamily != AddressFamily.InterNetworkV6) // I don't have IPv6 servers
        {
            await using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var actual = conn.GetEndPoints().Single();
                var server = conn.GetServer(actual);
                await server.PingAsync();
            }
        }
    }

    public static IEnumerable<object[]> ConnectByVariousEndpointsData()
    {
        yield return new object[] { new IPEndPoint(IPAddress.Loopback, 6379), AddressFamily.InterNetwork };

        yield return new object[] { new IPEndPoint(IPAddress.IPv6Loopback, 6379), AddressFamily.InterNetworkV6 };

        yield return new object[] { new DnsEndPoint("localhost", 6379), AddressFamily.Unspecified };

        yield return new object[] { new DnsEndPoint("localhost", 6379, AddressFamily.InterNetwork), AddressFamily.InterNetwork };

        yield return new object[] { new DnsEndPoint("localhost", 6379, AddressFamily.InterNetworkV6), AddressFamily.InterNetworkV6 };

        yield return new object[] { ConfigurationOptions.Parse("localhost:6379").EndPoints.Single(), AddressFamily.Unspecified };

        yield return new object[] { ConfigurationOptions.Parse("localhost").EndPoints.Single(), AddressFamily.Unspecified };

        yield return new object[] { ConfigurationOptions.Parse("127.0.0.1:6379").EndPoints.Single(), AddressFamily.InterNetwork };

        yield return new object[] { ConfigurationOptions.Parse("127.0.0.1").EndPoints.Single(), AddressFamily.InterNetwork };

        yield return new object[] { ConfigurationOptions.Parse("[::1]").EndPoints.Single(), AddressFamily.InterNetworkV6 };

        yield return new object[] { ConfigurationOptions.Parse("[::1]:6379").EndPoints.Single(), AddressFamily.InterNetworkV6 };
    }
}
