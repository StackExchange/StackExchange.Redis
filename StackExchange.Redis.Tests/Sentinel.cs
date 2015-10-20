using System;
using System.Linq;
using System.Net;
using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture, Ignore("reason?")]
    public class Sentinel
    {
        // TODO fill in these constants before running tests
        private const string IP = "127.0.0.1";
        private const int Port = 26379;
        private const string ServiceName = "mymaster";

        private static readonly ConnectionMultiplexer Conn = GetConn();
        private static readonly IServer Server = Conn.GetServer(IP, Port);

        public static ConnectionMultiplexer GetConn()
        {
            // create a connection
            var options = new ConfigurationOptions()
            {
                CommandMap = CommandMap.Sentinel,
                EndPoints = { { IP, Port } },
                AllowAdmin = true,
                TieBreaker = "",
                ServiceName = ServiceName,
                SyncTimeout = 5000
            };
            var connection = ConnectionMultiplexer.Connect(options, Console.Out);
            Thread.Sleep(3000);
            Assert.IsTrue(connection.IsConnected);
            return connection;
        }

        [Test]
        public void PingTest()
        {
            var test = Server.Ping();
            Console.WriteLine("ping took {0} ms", test.TotalMilliseconds);
        }

        [Test]
        public void SentinelGetMasterAddressByNameTest()
        {
            var endpoint = Server.SentinelGetMasterAddressByName(ServiceName);
            Assert.IsNotNull(endpoint);
            var ipEndPoint = endpoint as IPEndPoint;
            Assert.IsNotNull(ipEndPoint);
            Console.WriteLine("{0}:{1}", ipEndPoint.Address, ipEndPoint.Port);
        }

        [Test]
        public void SentinelGetMasterAddressByNameNegativeTest() 
        {
            var endpoint = Server.SentinelGetMasterAddressByName("FakeServiceName");
            Assert.IsNull(endpoint);
        }

        [Test]
        public void SentinelMasterTest()
        {
            var dict = Server.SentinelMaster(ServiceName).ToDictionary();
            Assert.AreEqual(ServiceName, dict["name"]);
            foreach (var kvp in dict)
            {
                Console.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
            }
        }

        [Test]
        public void SentinelMastersTest()
        {
            var masterConfigs = Server.SentinelMasters();
            Assert.IsTrue(masterConfigs.First().ToDictionary().ContainsKey("name"));
            foreach (var config in masterConfigs)
            {
                foreach (var kvp in config)
                {
                    Console.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Test]
        public void SentinelSlavesTest() 
        {
            var slaveConfigs = Server.SentinelSlaves(ServiceName);
            if (slaveConfigs.Any()) 
            {
                Assert.IsTrue(slaveConfigs.First().ToDictionary().ContainsKey("name"));
            }
            foreach (var config in slaveConfigs) 
            {
                foreach (var kvp in config) {
                    Console.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
                }
            }
        }

        [Test, Ignore("reason?")]
        public void SentinelFailoverTest()
        {
            Server.SentinelFailover(ServiceName);
        }
    }
}
