using System;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConfigUri : TestBase
    {
        public ConfigUri(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void SslProtocols_SingleValue()
        {
            var options = ConfigurationOptions.Parse("redis://myhost?sslProtocols=Tls11");
            Assert.Equal(SslProtocols.Tls11, options.SslProtocols.GetValueOrDefault());
        }

        [Fact]
        public void SslProtocols_MultipleValues()
        {
            var options = ConfigurationOptions.Parse("redis://myhost?sslProtocols=Tls11&sslProtocols=Tls12");
            Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, options.SslProtocols.GetValueOrDefault());
        }

        [Theory]
        [InlineData("checkCertificateRevocation=false", false)]
        [InlineData("checkCertificateRevocation=true", true)]
        [InlineData("", true)]
        public void ConfigurationOption_CheckCertificateRevocation(string conString, bool expectedValue)
        {
            var options = ConfigurationOptions.Parse($"redis://host?{conString}");
            Assert.Equal(expectedValue, options.CheckCertificateRevocation);
            var toString = options.ToString();
            Assert.True(toString.IndexOf(conString, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        [Fact]
        public void SslProtocols_UsingIntegerValue()
        {
            // The below scenario is for cases where the *targeted*
            // .NET framework version (e.g. .NET 4.0) doesn't define an enum value (e.g. Tls11)
            // but the OS has been patched with support
            const int integerValue = (int)(SslProtocols.Tls11 | SslProtocols.Tls12);
            var options = ConfigurationOptions.Parse("redis://myhost?sslProtocols=" + integerValue);
            Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, options.SslProtocols.GetValueOrDefault());
        }

        [Fact]
        public void SslProtocols_InvalidValue()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationOptions.Parse("redis://myhost?sslProtocols=InvalidSslProtocol"));
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzure()
        {
            var options = ConfigurationOptions.Parse("redis://contoso.redis.cache.windows.net");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsForAzureWhenSpecified()
        {
            var options = ConfigurationOptions.Parse("redis://contoso.redis.cache.windows.net?abortConnect=true&version=2.1.1");
            Assert.True(options.DefaultVersion.Equals(new Version(2, 1, 1)));
            Assert.True(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureChina()
        {
            // added a few upper case chars to validate comparison
            var options = ConfigurationOptions.Parse("redis://contoso.REDIS.CACHE.chinacloudapi.cn");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureGermany()
        {
            var options = ConfigurationOptions.Parse("redis://contoso.redis.cache.cloudapi.de");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureUSGov()
        {
            var options = ConfigurationOptions.Parse("redis://contoso.redis.cache.usgovcloudapi.net");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForNonAzure()
        {
            var options = ConfigurationOptions.Parse("redis://redis.contoso.com");
            Assert.True(options.DefaultVersion.Equals(new Version(2, 0, 0)));
            Assert.True(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultWhenNoEndpointsSpecifiedYet()
        {
            var options = new ConfigurationOptions();
            Assert.True(options.DefaultVersion.Equals(new Version(2, 0, 0)));
            Assert.True(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsSyncTimeout()
        {
            // Default check
            var options = new ConfigurationOptions();
            Assert.Equal(5000, options.SyncTimeout);

            options = ConfigurationOptions.Parse("redis://host?syncTimeout=20");
            Assert.Equal(20, options.SyncTimeout);
        }

        [Theory]
        [InlineData("redis://127.1:6379", AddressFamily.InterNetwork, "127.0.0.1", 6379)]
        [InlineData("redis://127.0.0.1:6379", AddressFamily.InterNetwork, "127.0.0.1", 6379)]
        // https://www.ietf.org/rfc/rfc2732.txt
        // To use a literal IPv6 address in a URL, the literal address should be enclosed in "[" and "]" characters.
        [InlineData("redis://[2a01:9820:1:24::1:1:6379]", AddressFamily.InterNetworkV6, "2a01:9820:1:24:0:1:1:6379", 0)]
        [InlineData("redis://[fedc:ba98:7654:3210:fedc:ba98:7654:3210]:6379", AddressFamily.InterNetworkV6, "fedc:ba98:7654:3210:fedc:ba98:7654:3210", 6379)]
        public void ConfigurationOptionsIPv6Parsing(string configString, AddressFamily family, string address, int port)
        {
            var options = ConfigurationOptions.Parse(configString);
            Assert.Single(options.EndPoints);
            var ep = Assert.IsType<IPEndPoint>(options.EndPoints[0]);
            Assert.Equal(family, ep.AddressFamily);
            Assert.Equal(address, ep.Address.ToString());
            Assert.Equal(port, ep.Port);
        }

        [Theory]
        [InlineData("redis://2a01:9820:1:24::1:1:6379")]
        [InlineData("redis://fedc:ba98:7654:3210:fedc:ba98:7654:3210:6379")]
        [InlineData("redis://fedc:ba98:7654:3210:fedc:ba98:7654:3210")]
        public void ConfigurationOptionsInvalidIPv6Parsing(string configString)
        {
            var ex = Record.Exception(() => ConfigurationOptions.Parse(configString));
            Assert.IsType<ArgumentException>(ex);
        }

        [Theory]
        [InlineData("redis://myDNS:1234?password=myPassword&connectRetry=3&connectTimeout=15000&syncTimeout=15000&defaultDatabase=0&abortConnect=false&ssl=true&sslProtocols=Tls12", SslProtocols.Tls12)]
        [InlineData("redis://myDNS:1234?password=myPassword&abortConnect=false&ssl=true&sslProtocols=Tls12", SslProtocols.Tls12)]
#pragma warning disable CS0618 // obsolete
        [InlineData("redis://myDNS:1234?password=myPassword&abortConnect=false&ssl=true&sslProtocols=Ssl3", SslProtocols.Ssl3)]
#pragma warning restore CS0618 // obsolete
        [InlineData("redis://myDNS:1234?password=myPassword&abortConnect=false&ssl=true&sslProtocols=Tls12 ", SslProtocols.Tls12)]
        public void ParseTlsWithoutTrailingComma(string configString, SslProtocols expected)
        {
            var config = ConfigurationOptions.Parse(configString);
            Assert.Equal(expected, config.SslProtocols);
        }

        [Theory]
        [InlineData("redis://foo?sslProtocols=NotAThing", "Keyword 'sslProtocols' requires an SslProtocol value (multiple values separated by '|'); the value 'NotAThing' is not recognised.", "sslProtocols")]
        [InlineData("redis://foo?SyncTimeout=ten", "Keyword 'SyncTimeout' requires an integer value; the value 'ten' is not recognised.", "SyncTimeout")]
        [InlineData("redis://foo?syncTimeout=-42", "Keyword 'syncTimeout' has a minimum value of '1'; the value '-42' is not permitted.", "syncTimeout")]
        [InlineData("redis://foo?AllowAdmin=maybe", "Keyword 'AllowAdmin' requires a boolean value; the value 'maybe' is not recognised.", "AllowAdmin")]
        [InlineData("redis://foo?Version=current", "Keyword 'Version' requires a version value; the value 'current' is not recognised.", "Version")]
        [InlineData("redis://foo?proxy=epoxy", "Keyword 'proxy' requires a proxy value; the value 'epoxy' is not recognised.", "proxy")]
        public void ConfigStringErrorsGiveMeaningfulMessages(string configString, string expected, string paramName)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationOptions.Parse(configString));
            Assert.StartsWith(expected, ex.Message); // param name gets concatenated sometimes
            Assert.Equal(paramName, ex.ParamName); // param name gets concatenated sometimes
        }

        [Fact]
        public void ConfigStringInvalidOptionErrorGiveMeaningfulMessages()
        {
            var ex = Assert.Throws<ArgumentException>(() => ConfigurationOptions.Parse("redis://foo?flibble=value"));
            Assert.StartsWith("Keyword 'flibble' is not supported.", ex.Message); // param name gets concatenated sometimes
            Assert.Equal("flibble", ex.ParamName);
        }
    }
}
