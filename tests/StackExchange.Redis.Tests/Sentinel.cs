using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Sentinel : TestBase
    {
        private string ServiceName => TestConfig.Current.SentinelSeviceName;

        private ConnectionMultiplexer Conn { get; }
        private IServer Server { get; }
        protected StringWriter ConnectionLog { get; }

        public Sentinel(ITestOutputHelper output) : base(output)
        {
            ConnectionLog = new StringWriter();

            Skip.IfNoConfig(nameof(TestConfig.Config.SentinelServer), TestConfig.Current.SentinelServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.SentinelSeviceName), TestConfig.Current.SentinelSeviceName);

            var options = new ConfigurationOptions()
            {
                CommandMap = CommandMap.Sentinel,
                EndPoints = { { TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPort } },
                AllowAdmin = true,
                TieBreaker = "",
                ServiceName = TestConfig.Current.SentinelSeviceName,
                SyncTimeout = 5000
            };
            Conn = ConnectionMultiplexer.Connect(options, ConnectionLog);
            Thread.Sleep(3000);
            Assert.True(Conn.IsConnected);
            Server = Conn.GetServer(TestConfig.Current.SentinelServer, TestConfig.Current.SentinelPort);
        }

        [Fact]
        public void PingTest()
        {
            var test = Server.Ping();
            Log("ping took {0} ms", test.TotalMilliseconds);
        }

        [Fact]
        public void SentinelGetMasterAddressByNameTest()
        {
            var endpoint = Server.SentinelGetMasterAddressByName(ServiceName);
            Assert.NotNull(endpoint);
            var ipEndPoint = endpoint as IPEndPoint;
            Assert.NotNull(ipEndPoint);
            Log("{0}:{1}", ipEndPoint.Address, ipEndPoint.Port);
        }

        [Fact]
        public void SentinelGetMasterAddressByNameNegativeTest()
        {
            var endpoint = Server.SentinelGetMasterAddressByName("FakeServiceName");
            Assert.Null(endpoint);
        }

        [Fact]
        public async Task SentinelGetMasterAddressByNameAsyncNegativeTest()
        {
            var endpoint = await Server.SentinelGetMasterAddressByNameAsync("FakeServiceName").ForAwait();
            Assert.Null(endpoint);
        }

        [Fact]
        public void SentinelMasterTest()
        {
            var dict = Server.SentinelMaster(ServiceName).ToDictionary();
            Assert.Equal(ServiceName, dict["name"]);
            foreach (var kvp in dict)
            {
                Log("{0}:{1}", kvp.Key, kvp.Value);
            }
        }

        [Fact]
        public void SentinelSentinelsTest()
        {
            var sentinels = Server.SentinelSentinels("mymaster");
            Assert.True(sentinels[0].ToDictionary().ContainsKey("name"));
            foreach (var config in sentinels)
            {
                foreach (var kvp in config)
                {
                    Writer.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
                }
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
                    Log("{0}:{1}", kvp.Key, kvp.Value);
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
                    Log("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Fact]
        public void SentinelFailoverTest()
        {
            Server.SentinelFailover(ServiceName);
        }
    }
}
