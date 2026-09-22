using System.Net;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// TLS target-host selection for SNI and certificate validation.
/// </summary>
public class TlsHostNameUnitTests
{
    [Fact]
    public void DnsEndPointUsesItsOwnHostEvenWhenSslHostIsSet()
    {
        var endpoint = new DnsEndPoint("host-2.redis.example.com", 443);
        Assert.Equal("host-2.redis.example.com", Format.GetTlsHostName(endpoint, "host-1.redis.example.com"));
    }

    [Fact]
    public void IpEndPointPrefersConfiguredSslHost()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6379);
        Assert.Equal("mycache.redis.example.com", Format.GetTlsHostName(endpoint, "mycache.redis.example.com"));
    }

    [Fact]
    public void IpEndPointFallsBackToAddressWhenSslHostMissing()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6379);
        Assert.Equal("10.0.0.1", Format.GetTlsHostName(endpoint, null));
        Assert.Equal("10.0.0.1", Format.GetTlsHostName(endpoint, ""));
    }

    [Fact]
    public void TlsOptionsResolveHostMatchesFormatHelper()
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { "host-1.redis.example.com:443" },
            Ssl = true,
            SslHost = "host-1.redis.example.com",
        };
        var tls = new Configuration.TlsOptions(options);

        Assert.Equal("host-2.redis.example.com", tls.ResolveHost(new DnsEndPoint("host-2.redis.example.com", 443)));
        Assert.Equal("host-1.redis.example.com", tls.ResolveHost(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 443)));
    }
}
