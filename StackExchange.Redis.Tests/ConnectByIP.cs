using System.Net.Sockets;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectByIP : TestBase
    {
        public ConnectByIP(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void ParseEndpoints()
        {
            var eps = new EndPointCollection
            {
                { "127.0.0.1", 1000 },
                { "::1", 1001 },
                { "localhost", 1002 }
            };

            Assert.Equal(AddressFamily.InterNetwork, eps[0].AddressFamily);
            Assert.Equal(AddressFamily.InterNetworkV6, eps[1].AddressFamily);
            Assert.Equal(AddressFamily.Unspecified, eps[2].AddressFamily);

            Assert.Equal("127.0.0.1:1000", eps[0].ToString());
            Assert.Equal("[::1]:1001", eps[1].ToString());
            Assert.Equal("Unspecified/localhost:1002", eps[2].ToString());
        }

        [Fact]
        public void IPv4Connection()
        {
            var config = new ConfigurationOptions
            {
                EndPoints = { { TestConfig.Current.IPv4Server, TestConfig.Current.IPv4Port } }
            };
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var server = conn.GetServer(config.EndPoints[0]);
                Assert.Equal(AddressFamily.InterNetwork, server.EndPoint.AddressFamily);
                server.Ping();
            }
        }

        [Fact]
        public void IPv6Connection()
        {
            var config = new ConfigurationOptions
            {
                EndPoints = { { TestConfig.Current.IPv6Server, TestConfig.Current.IPv6Port } }
            };
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var server = conn.GetServer(config.EndPoints[0]);
                Assert.Equal(AddressFamily.InterNetworkV6, server.EndPoint.AddressFamily);
                server.Ping();
            }
        }
    }
}
