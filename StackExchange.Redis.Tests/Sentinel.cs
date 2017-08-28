using System;
using System.Net;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Sentinel
    {
        private string ServerName => TestConfig.Current.SentinelServer;
        private int Port => TestConfig.Current.SentinelPort;
        private string ServiceName => TestConfig.Current.SentinelSeviceName;

        private ConnectionMultiplexer Conn { get; }
        private IServer Server { get; }

        public ITestOutputHelper Output { get; }
        public Sentinel(ITestOutputHelper output)
        {
            Output = output;
            var options = new ConfigurationOptions()
            {
                CommandMap = CommandMap.Sentinel,
                EndPoints = { { ServerName, Port } },
                AllowAdmin = true,
                TieBreaker = "",
                ServiceName = ServiceName,
                SyncTimeout = 5000
            };
            Conn = ConnectionMultiplexer.Connect(options, Console.Out);
            Thread.Sleep(3000);
            Assert.True(Conn.IsConnected);
            Server = Conn.GetServer(ServerName, Port);
        }

        [Fact]
        public void PingTest()
        {
            var test = Server.Ping();
            Output.WriteLine("ping took {0} ms", test.TotalMilliseconds);
        }

        [Fact]
        public void SentinelGetMasterAddressByNameTest()
        {
            var endpoint = Server.SentinelGetMasterAddressByName(ServiceName);
            Assert.NotNull(endpoint);
            var ipEndPoint = endpoint as IPEndPoint;
            Assert.NotNull(ipEndPoint);
            Output.WriteLine("{0}:{1}", ipEndPoint.Address, ipEndPoint.Port);
        }

        [Fact]
        public void SentinelGetMasterAddressByNameNegativeTest()
        {
            var endpoint = Server.SentinelGetMasterAddressByName("FakeServiceName");
            Assert.Null(endpoint);
        }

        [Fact]
        public void SentinelMasterTest()
        {
            var dict = Server.SentinelMaster(ServiceName).ToDictionary();
            Assert.Equal(ServiceName, dict["name"]);
            foreach (var kvp in dict)
            {
                Output.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
            }
        }

        [Fact]
        public void SentinelMastersTest()
        {
            var masterConfigs = Server.SentinelMasters();
            Assert.True(masterConfigs[0].ToDictionary().ContainsKey("name"));
            foreach (var config in masterConfigs)
            {
                foreach (var kvp in config)
                {
                    Output.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public void SentinelSlavesTest()
        {
            var slaveConfigs = Server.SentinelSlaves(ServiceName);
            if (slaveConfigs.Length > 0)
            {
                Assert.True(slaveConfigs[0].ToDictionary().ContainsKey("name"));
            }
            foreach (var config in slaveConfigs)
            {
                foreach (var kvp in config) {
                    Output.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact(Skip = "Isolated Test")]
        public void SentinelFailoverTest()
        {
            Server.SentinelFailover(ServiceName);
        }
    }
}
