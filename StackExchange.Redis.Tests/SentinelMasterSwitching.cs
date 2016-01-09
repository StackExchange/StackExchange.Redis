using System.Linq;
using System.Net;
using NUnit.Framework;
using System;
using System.Threading;
using System.Security.Cryptography;
using System.Diagnostics;

namespace StackExchange.Redis.Tests
{
    [TestFixture, Ignore]
    public class SentinelMasterSwitching
    {
        // TODO fill in these constants before running tests
        private const string IP = "127.0.0.1";
        private const int Sentinel1Port = 26379;
        private const int Sentinel2Port = 26380;
        private const string ServiceName = "mymaster";

        private static bool masterKnown;
        private static readonly ConnectionMultiplexer SentinelConn = GetSentinelConn();
        private static readonly ConnectionMultiplexer RedisConn = GetRedisConn();
        //private static readonly IServer Server = Conn.GetServer(IP, Port);

        public static ConnectionMultiplexer GetSentinelConn()
        {
            // create a connection
            var options = new ConfigurationOptions()
            {
                CommandMap = CommandMap.Sentinel,
                EndPoints = { { IP, Sentinel1Port }, { IP, Sentinel2Port } },
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

        public static ConnectionMultiplexer GetRedisConn()
        {
            // create a connection
            var options = new ConfigurationOptions()
            {
                AllowAdmin = true, //To allow shutdown
                SentinelConnection = SentinelConn,
                SyncTimeout = 5000
            };
            var connection = ConnectionMultiplexer.Connect(options, Console.Out);
            Thread.Sleep(3000);
            Assert.IsTrue(connection.IsConnected);
            return connection;
        }

        [Test]
        public void MasterSwitch()
        {
            masterKnown = true;
            SentinelConn.GetSubscriber().Subscribe("+switch-master", (channel, message) =>
            {
                masterKnown = true;
            });
            IDatabase db = RedisConn.GetDatabase(0);
            db.StringSet("TestValue0", "Value0");
            db.KeyDelete("TestValue0");
            RedisConn.GetServer(SentinelConn.GetConfiguredMasterForService()).Shutdown(ShutdownMode.Always);
            Stopwatch sw = new Stopwatch();
            sw.Start();
            masterKnown = false;
            Assert.Throws<RedisConnectionException>(() => { db.StringSet("TestValue1", "Value1"); db.KeyDelete("TestValue1"); }, "Can access master immediately after shutdown!");
            while (!masterKnown) {
                if (sw.ElapsedMilliseconds > 180 * 1000) throw new TimeoutException("Test timed out waiting for sentinel to switch master!");
                Thread.Sleep(100); 
            }
            Assert.True(masterKnown, "Master still unknown after timeout!");
            Thread.Sleep(2000); // Wait for reconfigure
            db.StringSet("TestValue2", "Value2");
            db.KeyDelete("TestValue2");
        }

        static byte[] getChunk()
        {
            byte[] data = new byte[1024 * 1024 * 1];
            RNGCryptoServiceProvider csp = new RNGCryptoServiceProvider();
            csp.GetNonZeroBytes(data);
            return data;
        }

    }
}
