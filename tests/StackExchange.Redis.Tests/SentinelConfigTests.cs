using System;
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
}
