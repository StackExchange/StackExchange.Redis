using System.Net;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class SentinelGetReplicaAddresses(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // SentinelCommands.AddressesHandler - shared by SENTINEL SENTINELS and SENTINEL REPLICAS
    private static IRespHandler<System.Net.EndPoint[]> Addresses
        => NewCoreHandlers.Get<System.Net.EndPoint[]>(typeof(SentinelCommands), "AddressesHandler");

    [Fact]
    public void SingleReplica_Success()
    {
        var resp = "*1\r\n*4\r\n$2\r\nip\r\n$9\r\n127.0.0.1\r\n$4\r\nport\r\n$4\r\n6380\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Single(result);

        var endpoint = Assert.IsType<System.Net.IPEndPoint>(result[0]);
        Assert.Equal("127.0.0.1", endpoint.Address.ToString());
        Assert.Equal(6380, endpoint.Port);
    }

    [Fact]
    public void MultipleReplicas_Success()
    {
        var resp = "*2\r\n*4\r\n$2\r\nip\r\n$9\r\n127.0.0.1\r\n$4\r\nport\r\n$4\r\n6380\r\n*4\r\n$2\r\nip\r\n$9\r\n127.0.0.2\r\n$4\r\nport\r\n$4\r\n6381\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);

        var endpoint1 = Assert.IsType<System.Net.IPEndPoint>(result[0]);
        Assert.Equal("127.0.0.1", endpoint1.Address.ToString());
        Assert.Equal(6380, endpoint1.Port);

        var endpoint2 = Assert.IsType<System.Net.IPEndPoint>(result[1]);
        Assert.Equal("127.0.0.2", endpoint2.Address.ToString());
        Assert.Equal(6381, endpoint2.Port);
    }

    [Fact]
    public void DnsEndpoint_Success()
    {
        var resp = "*1\r\n*4\r\n$2\r\nip\r\n$17\r\nredis.example.com\r\n$4\r\nport\r\n$4\r\n6380\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Single(result);

        var endpoint = Assert.IsType<System.Net.DnsEndPoint>(result[0]);
        Assert.Equal("redis.example.com", endpoint.Host);
        Assert.Equal(6380, endpoint.Port);
    }

    [Fact]
    public void ReversedOrder_Success()
    {
        // Test that order doesn't matter - port before ip
        var resp = "*1\r\n*4\r\n$4\r\nport\r\n$4\r\n6380\r\n$2\r\nip\r\n$9\r\n127.0.0.1\r\n";
        var result = Execute(resp, Addresses);

        Assert.NotNull(result);
        Assert.Single(result);
        var endpoint = Assert.IsType<IPEndPoint>(result[0]);
        Assert.Equal("127.0.0.1", endpoint.Address.ToString());
        Assert.Equal(6380, endpoint.Port);
    }

    [Fact]
    public void EmptyArray_Success()
    {
        // BEHAVIOUR CHANGE: the shipped SentinelGetReplicaAddressesProcessor rejected an empty list as an
        // unexpected response; the new core's AddressesHandler (shared with SENTINEL SENTINELS) reads it as
        // "no replicas", which is what a primary with no replicas actually reports.
        var resp = "*0\r\n";
        var result = Execute(resp, Addresses);
        Assert.Empty(result);
    }

    [Fact]
    public void NullArray_Success()
    {
        // BEHAVIOUR CHANGE: rejected by the shipped processor; a nil reply is "no replicas" to AddressesHandler
        var resp = "*-1\r\n";
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
