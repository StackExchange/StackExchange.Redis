using System.Net;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class SentinelGetSentinelAddresses(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // SentinelCommands.AddressesHandler - shared by SENTINEL SENTINELS and SENTINEL REPLICAS
    private static IRespHandler<System.Net.EndPoint[]> Addresses
        => PrivateHandlers.Get<System.Net.EndPoint[]>(typeof(SentinelCommands), "AddressesHandler");

    [Fact]
    public void SingleSentinel_Success()
    {
        var resp = "*1\r\n*4\r\n$2\r\nip\r\n$9\r\n127.0.0.1\r\n$4\r\nport\r\n$5\r\n26379\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Single(result);
        var endpoint = Assert.IsType<IPEndPoint>(result[0]);
        Assert.Equal("127.0.0.1", endpoint.Address.ToString());
        Assert.Equal(26379, endpoint.Port);
    }

    [Fact]
    public void MultipleSentinels_Success()
    {
        var resp = "*2\r\n*4\r\n$2\r\nip\r\n$9\r\n127.0.0.1\r\n$4\r\nport\r\n$5\r\n26379\r\n*4\r\n$2\r\nip\r\n$9\r\n127.0.0.2\r\n$4\r\nport\r\n$5\r\n26380\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);

        var endpoint1 = Assert.IsType<IPEndPoint>(result[0]);
        Assert.Equal("127.0.0.1", endpoint1.Address.ToString());
        Assert.Equal(26379, endpoint1.Port);

        var endpoint2 = Assert.IsType<IPEndPoint>(result[1]);
        Assert.Equal("127.0.0.2", endpoint2.Address.ToString());
        Assert.Equal(26380, endpoint2.Port);
    }

    [Fact]
    public void DnsEndpoint_Success()
    {
        var resp = "*1\r\n*4\r\n$2\r\nip\r\n$20\r\nsentinel.example.com\r\n$4\r\nport\r\n$5\r\n26379\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Single(result);
        var endpoint = Assert.IsType<DnsEndPoint>(result[0]);
        Assert.Equal("sentinel.example.com", endpoint.Host);
        Assert.Equal(26379, endpoint.Port);
    }

    [Fact]
    public void ReversedOrder_Success()
    {
        var resp = "*1\r\n*4\r\n$4\r\nport\r\n$5\r\n26379\r\n$2\r\nip\r\n$9\r\n127.0.0.1\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Single(result);
        var endpoint = Assert.IsType<IPEndPoint>(result[0]);
        Assert.Equal("127.0.0.1", endpoint.Address.ToString());
        Assert.Equal(26379, endpoint.Port);
    }

    [Fact]
    public void EmptyArray_Success()
    {
        var resp = "*0\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void NullBulkString_Success()
    {
        // BEHAVIOUR CHANGE: the shipped SentinelGetSentinelAddressesProcessor only accepted an aggregate;
        // AddressesHandler checks IsNull first, so any nil - array or bulk string - reads as "no sentinels"
        var resp = "$-1\r\n";
        var result = Execute(resp, Addresses);

        Assert.Empty(result);
    }

    [Fact]
    public void NotArray_Failure()
    {
        var resp = "+OK\r\n";
        ExecuteUnexpected(resp, Addresses);
    }
}
