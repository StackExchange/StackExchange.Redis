using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
#if !NETCOREAPP1_0
using System.Security.Authentication;
#endif

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class Config : BookSleeveTestBase
    {
        public Config(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void CanOpenUnsecuredConnection()
        {
            using (var conn = GetUnsecuredConnection(false))
            {
                var server = GetServer(conn);
                server.Ping();
            }
        }

        [Fact]
        public void CanOpenSecuredConnection()
        {
            using (var conn = GetSecuredConnection())
            {
                var server = GetServer(conn);
                server.Ping();
            }
        }

        [Fact]
        public void CanNotOpenNonsenseConnection_IP()
        {
            Assert.Throws<RedisConnectionException>(() =>
            {
                var log = new StringWriter();
                try
                {
                    using (var conn = ConnectionMultiplexer.Connect(TestConfig.Current.MasterServer + ":6500")) { }
                }
                finally
                {
                    Output.WriteLine(log.ToString());
                }
            });
        }

        [Fact]
        public async Task CanNotOpenNonsenseConnection_DNS()
        {
            var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () =>
            {
                var log = new StringWriter();
                try
                {
                    using (var conn = await ConnectionMultiplexer.ConnectAsync("doesnot.exist.ds.aasd981230d.com:6500", log).ForAwait())
                    {
                    }
                }
                finally
                {
                    Output.WriteLine(log.ToString());
                }
            }).ForAwait();
            Output.WriteLine(ex.ToString());
        }

        [Fact]
        public void CreateDisconnectedNonsenseConnection_IP()
        {
            var log = new StringWriter();
            try
            {
                using (var conn = ConnectionMultiplexer.Connect(TestConfig.Current.MasterServer + ":6500,abortConnect=false"))
                {
                    Assert.False(conn.GetServer(conn.GetEndPoints().Single()).IsConnected);
                    Assert.False(conn.GetDatabase().IsConnected(default(RedisKey)));
                }
            }
            finally
            {
                Output.WriteLine(log.ToString());
            }
        }

        [Fact]
        public void CreateDisconnectedNonsenseConnection_DNS()
        {
            var log = new StringWriter();
            try
            {
                using (var conn = ConnectionMultiplexer.Connect("doesnot.exist.ds.aasd981230d.com:6500,abortConnect=false", log))
                {
                    Assert.False(conn.GetServer(conn.GetEndPoints().Single()).IsConnected);
                    Assert.False(conn.GetDatabase().IsConnected(default(RedisKey)));
                }
            }
            finally
            {
                Output.WriteLine(log.ToString());
            }
        }

#if !NETCOREAPP1_0
        [Fact]
        public void SslProtocols_SingleValue()
        {
            var log = new StringWriter();
            var options = ConfigurationOptions.Parse("myhost,sslProtocols=Tls11");
            Assert.Equal(SslProtocols.Tls11, options.SslProtocols.Value);
        }

        [Fact]
        public void SslProtocols_MultipleValues()
        {
            var log = new StringWriter();
            var options = ConfigurationOptions.Parse("myhost,sslProtocols=Tls11|Tls12");
            Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, options.SslProtocols.Value);
        }

        [Fact]
        public void SslProtocols_UsingIntegerValue()
        {
            var log = new StringWriter();

            // The below scenario is for cases where the *targeted*
            // .NET framework version (e.g. .NET 4.0) doesn't define an enum value (e.g. Tls11)
            // but the OS has been patched with support
            const int integerValue = (int)(SslProtocols.Tls11 | SslProtocols.Tls12);
            var options = ConfigurationOptions.Parse("myhost,sslProtocols=" + integerValue);
            Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, options.SslProtocols.Value);
        }

        [Fact]
        public void SslProtocols_InvalidValue()
        {
            var log = new StringWriter();
            Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationOptions.Parse("myhost,sslProtocols=InvalidSslProtocol"));
        }
#endif

        [Fact]
        public void ConfigurationOptionsDefaultForAzure()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.windows.net");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsForAzureWhenSpecified()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.windows.net,abortConnect=true, version=2.1.1");
            Assert.True(options.DefaultVersion.Equals(new Version(2, 1, 1)));
            Assert.True(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureChina()
        {
            // added a few upper case chars to validate comparison
            var options = ConfigurationOptions.Parse("contoso.REDIS.CACHE.chinacloudapi.cn");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureGermany()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.cloudapi.de");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForAzureUSGov()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.usgovcloudapi.net");
            Assert.True(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.False(options.AbortOnConnectFail);
        }

        [Fact]
        public void ConfigurationOptionsDefaultForNonAzure()
        {
            var options = ConfigurationOptions.Parse("redis.contoso.com");
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
    }
}