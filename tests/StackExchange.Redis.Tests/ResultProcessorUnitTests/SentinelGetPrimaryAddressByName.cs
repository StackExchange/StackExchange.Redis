using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class SentinelGetPrimaryAddressByName(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void ValidHostAndPort_Success()
    {
        // Array with 2 elements: host (bulk string) and port (integer)
        var resp = "*2\r\n$9\r\n127.0.0.1\r\n:6379\r\n";
        var result = Execute(resp, ResultProcessor.SentinelPrimaryEndpoint);

        Assert.NotNull(result);
        var ipEndpoint = Assert.IsType<System.Net.IPEndPoint>(result);
        Assert.Equal("127.0.0.1", ipEndpoint.Address.ToString());
        Assert.Equal(6379, ipEndpoint.Port);
    }

    [Fact]
    public void DomainNameAndPort_Success()
    {
        // Array with 2 elements: domain name (bulk string) and port (integer)
        var resp = "*2\r\n$17\r\nredis.example.com\r\n:6380\r\n";
        var result = Execute(resp, ResultProcessor.SentinelPrimaryEndpoint);

        Assert.NotNull(result);
        var dnsEndpoint = Assert.IsType<System.Net.DnsEndPoint>(result);
        Assert.Equal("redis.example.com", dnsEndpoint.Host);
        Assert.Equal(6380, dnsEndpoint.Port);
    }

    [Fact]
    public void NullArray_Success()
    {
        // Null array - primary doesn't exist
        var resp = "*-1\r\n";
        var result = Execute(resp, ResultProcessor.SentinelPrimaryEndpoint);

        Assert.Null(result);
    }

    [Fact]
    public void EmptyArray_Success()
    {
        // Empty array - primary doesn't exist
        var resp = "*0\r\n";
        var result = Execute(resp, ResultProcessor.SentinelPrimaryEndpoint);

        Assert.Null(result);
    }

    [Fact]
    public void NotArray_Failure()
    {
        // Simple string instead of array
        var resp = "+OK\r\n";
        ExecuteUnexpected(resp, ResultProcessor.SentinelPrimaryEndpoint);
    }

    [Fact]
    public void ArrayWithOneElement_Failure()
    {
        // Array with only 1 element (missing port)
        var resp = "*1\r\n$9\r\n127.0.0.1\r\n";
        ExecuteUnexpected(resp, ResultProcessor.SentinelPrimaryEndpoint);
    }

    [Fact]
    public void ArrayWithThreeElements_Failure()
    {
        // Array with 3 elements (too many)
        var resp = "*3\r\n$9\r\n127.0.0.1\r\n:6379\r\n$5\r\nextra\r\n";
        ExecuteUnexpected(resp, ResultProcessor.SentinelPrimaryEndpoint);
    }

    [Fact]
    public void ArrayWithNonIntegerPort_Failure()
    {
        // Array with 2 elements but port is not an integer
        var resp = "*2\r\n$9\r\n127.0.0.1\r\n$4\r\nport\r\n";
        ExecuteUnexpected(resp, ResultProcessor.SentinelPrimaryEndpoint);
    }
}
