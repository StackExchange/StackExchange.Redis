using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SentinelConfigTests
{
    [Fact]
    public void Parse_SentinelCredentials_FromConnectionString()
    {
        var cs = "localhost:26379,serviceName=myprimary,sentinelUser=su,sentinelPassword=sp";
        var options = ConfigurationOptions.Parse(cs);

        Assert.Equal("su", options.SentinelUser);
        Assert.Equal("sp", options.SentinelPassword);
        Assert.Equal("myprimary", options.ServiceName);
    }

    [Fact]
    public void ToString_Masks_SentinelPassword_WhenExcluded()
    {
        var options = new ConfigurationOptions();
        options.EndPoints.Add("localhost", 26379);
        options.ServiceName = "myprimary";
        options.SentinelUser = "su";
        options.SentinelPassword = "secret";

        var repr = options.ToString(includePassword: false);

        Assert.Contains("sentinelUser=su", repr);
        Assert.Contains("sentinelPassword=*****", repr);
        Assert.DoesNotContain("secret", repr);
    }

    [Fact]
    public void Clone_Preserves_SentinelCredentials()
    {
        var options = new ConfigurationOptions();
        options.SentinelUser = "su";
        options.SentinelPassword = "sp";

        var clone = options.Clone();

        Assert.Equal(options.SentinelUser, clone.SentinelUser);
        Assert.Equal(options.SentinelPassword, clone.SentinelPassword);
    }

    [Fact]
    public void Connect_UnreachableSentinel_AbortDisabled_ReturnsDisconnectedMultiplexer()
    {
        var options = GetUnreachableSentinelOptions(abortOnConnectFail: false);

        using var connection = ConnectionMultiplexer.Connect(options);

        Assert.False(connection.IsConnected);
        var exception = Assert.IsType<RedisConnectionException>(connection.LastException);
        Assert.Equal(ConnectionFailureType.UnableToConnect, exception.FailureType);
        Assert.Equal("Sentinel: Failed connecting to configured primary for service: unreachable-primary", exception.Message);
        Assert.NotNull(connection.sentinelConnection);
    }

    [Fact]
    public async Task ConnectAsync_UnreachableSentinel_AbortDisabled_ReturnsDisconnectedMultiplexer()
    {
        var options = GetUnreachableSentinelOptions(abortOnConnectFail: false);

        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);

        Assert.False(connection.IsConnected);
        var exception = Assert.IsType<RedisConnectionException>(connection.LastException);
        Assert.Equal(ConnectionFailureType.UnableToConnect, exception.FailureType);
        Assert.Equal("Sentinel: Failed connecting to configured primary for service: unreachable-primary", exception.Message);
        Assert.NotNull(connection.sentinelConnection);
    }

    [Fact]
    public void SentinelConnect_UnreachableSentinel_AbortDisabled_ReturnsDisconnectedMultiplexer()
    {
        var options = GetUnreachableSentinelOptions(abortOnConnectFail: false);

        using var connection = ConnectionMultiplexer.SentinelConnect(options);

        Assert.False(connection.IsConnected);
    }

    [Fact]
    public void Connect_UnreachableSentinel_AbortEnabled_Throws()
    {
        var options = GetUnreachableSentinelOptions(abortOnConnectFail: true);

        Assert.Throws<RedisConnectionException>(() => ConnectionMultiplexer.Connect(options));
    }

    private static ConfigurationOptions GetUnreachableSentinelOptions(bool abortOnConnectFail)
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = abortOnConnectFail,
            ConnectRetry = 0,
            ConnectTimeout = 100,
            ServiceName = "unreachable-primary",
        };
        options.EndPoints.Add(IPAddress.Loopback, 1);
        return options;
    }
}
