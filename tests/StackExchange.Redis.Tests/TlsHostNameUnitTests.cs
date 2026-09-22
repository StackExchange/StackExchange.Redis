using System.Net;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// TLS target-host selection for SNI and certificate validation.
/// </summary>
public class TlsHostNameUnitTests
{
    [Fact]
    public void ExplicitSslHostOverridesDnsEndPoint()
    {
        var endpoint = new DnsEndPoint("host-2.redis.example.com", 443);
        var options = new ConfigurationOptions { SslHost = "host-1.redis.example.com" };
        Assert.Equal("host-1.redis.example.com", options.ResolveTlsHostName(endpoint));
    }

    [Fact]
    public void DnsEndPointUsesItsOwnHostInsteadOfInferredHost()
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { "host-1.redis.example.com:443" },
        };

        Assert.Null(options.SslHost);
        Assert.Equal("host-2.redis.example.com", options.ResolveTlsHostName(new DnsEndPoint("host-2.redis.example.com", 443)));
    }

    [Fact]
    public void IpEndPointPrefersExplicitSslHost()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6379);
        var options = new ConfigurationOptions
        {
            EndPoints = { "host-1.redis.example.com:443" },
            SslHost = "mycache.redis.example.com",
        };
        Assert.Equal("mycache.redis.example.com", options.ResolveTlsHostName(endpoint));
    }

    [Fact]
    public void IpEndPointUsesInferredHost()
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { "host-1.redis.example.com:443" },
        };
        Assert.Equal("host-1.redis.example.com", options.ResolveTlsHostName(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6379)));
    }

    [Fact]
    public void IpEndPointFallsBackToAddressWhenHostsAreMissing()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6379);
        Assert.Equal("10.0.0.1", new ConfigurationOptions().ResolveTlsHostName(endpoint));
    }

    [Fact]
    public void TlsOptionsResolveHostHonorsExplicitOverride()
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { "host-1.redis.example.com:443" },
            Ssl = true,
            SslHost = "override.redis.example.com",
        };
        var tls = new Configuration.TlsOptions(options);

        Assert.Equal("override.redis.example.com", tls.ResolveHost(new DnsEndPoint("host-2.redis.example.com", 443)));
        Assert.Equal("override.redis.example.com", tls.ResolveHost(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 443)));
    }

    [Fact]
    public void TlsOptionsResolveHostUsesEndpointBeforeInferredHost()
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { "host-1.redis.example.com:443" },
            Ssl = true,
        };
        var tls = new Configuration.TlsOptions(options);

        Assert.Equal("host-2.redis.example.com", tls.ResolveHost(new DnsEndPoint("host-2.redis.example.com", 443)));
        Assert.Equal("host-1.redis.example.com", tls.ResolveHost(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 443)));
    }
}
