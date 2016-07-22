using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using StackExchange.Redis;

namespace Tests
{
    [TestFixture(Description = "Validates that the test environment is configured and responding")]
    public class Config
    {
        public static string CreateUniqueName()
        {
            return Guid.NewGuid().ToString("N");
        }

        internal static IServer GetServer(ConnectionMultiplexer conn)
        {
            return conn.GetServer(conn.GetEndPoints()[0]);
        }

        static readonly SocketManager socketManager = new SocketManager();
        static Config()
        {
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Trace.WriteLine(args.Exception, "UnobservedTaskException");
                args.SetObserved();
            };
        }

        public const string LocalHost = "127.0.0.1"; //"192.168.0.10"; //"127.0.0.1";
        public const string RemoteHost = "ubuntu";

        const int unsecuredPort = 6379, securedPort = 6381,
            clusterPort0 = 7000, clusterPort1 = 7001, clusterPort2 = 7002;


#if CLUSTER
        internal static RedisCluster GetCluster(TextWriter log = null)
        {
            string clusterConfiguration =
                RemoteHost + ":" + clusterPort0 + "," +
                RemoteHost + ":" + clusterPort1 + "," +
                RemoteHost + ":" + clusterPort2;
            return RedisCluster.Connect(clusterConfiguration, log);
        }
#endif

        //const int unsecuredPort = 6380, securedPort = 6381;

        internal static ConnectionMultiplexer GetRemoteConnection(bool open = true, bool allowAdmin = false, bool waitForOpen = false, int syncTimeout = 5000, int ioTimeout = 5000)
        {
            return GetConnection(RemoteHost, unsecuredPort, open, allowAdmin, waitForOpen, syncTimeout, ioTimeout);
        }
        private static ConnectionMultiplexer GetConnection(string host, int port, bool open = true, bool allowAdmin = false, bool waitForOpen = false, int syncTimeout = 5000, int ioTimeout = 5000)
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { { host, port } },
                AllowAdmin = allowAdmin,
                SyncTimeout = syncTimeout,
                SocketManager = socketManager
            };
            var conn = ConnectionMultiplexer.Connect(options);
            conn.InternalError += (s, args) =>
            {
                Trace.WriteLine(args.Exception.Message, args.Origin);
            };
            if (open)
            {
                if (waitForOpen) conn.GetDatabase().Ping();
            }
            return conn;
        }
        internal static ConnectionMultiplexer GetUnsecuredConnection(bool open = true, bool allowAdmin = false, bool waitForOpen = false, int syncTimeout = 5000, int ioTimeout = 5000)
        {
            return GetConnection(LocalHost, unsecuredPort, open, allowAdmin, waitForOpen, syncTimeout, ioTimeout);
        }

        internal static ConnectionMultiplexer GetSecuredConnection(bool open = true)
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { { LocalHost, securedPort } },
                Password = "changeme",
                SyncTimeout = 6000,
                SocketManager = socketManager
            };
            var conn = ConnectionMultiplexer.Connect(options);
            conn.InternalError += (s, args) =>
            {
                Trace.WriteLine(args.Exception.Message, args.Origin);
            };
            return conn;
        }

        internal static RedisFeatures GetFeatures(ConnectionMultiplexer muxer)
        {
            return GetServer(muxer).Features;
        }

        [Test]
        public void CanOpenUnsecuredConnection()
        {
            using (var conn = GetUnsecuredConnection(false))
            {
                var server = GetServer(conn);
                server.Ping();
            }
        }

        [Test]
        public void CanOpenSecuredConnection()
        {
            using (var conn = GetSecuredConnection(false))
            {
                var server = GetServer(conn);
                server.Ping();
            }
        }

        [Test]
        public void CanNotOpenNonsenseConnection_IP()
        {
            Assert.Throws<RedisConnectionException>(() =>
            {
                var log = new StringWriter();
                try {
                    using (var conn = ConnectionMultiplexer.Connect(Config.LocalHost + ":6500")) { }
                }
                finally {
                    Console.WriteLine(log);
                }
            });
        }

        [Test]
        public void CanNotOpenNonsenseConnection_DNS()
        {
            Assert.Throws<RedisConnectionException>(() =>
            {
                var log = new StringWriter();
                try
                {
                    using (var conn = ConnectionMultiplexer.Connect("doesnot.exist.ds.aasd981230d.com:6500", log)) { }
                }
                finally
                {
                    Console.WriteLine(log);
                }
            });
        }

        [Test]
        public void CreateDisconnectedNonsenseConnection_IP()
        {
            var log = new StringWriter();
            try
            {
                using (var conn = ConnectionMultiplexer.Connect(Config.LocalHost + ":6500,abortConnect=false")) {
                    Assert.IsFalse(conn.GetServer(conn.GetEndPoints().Single()).IsConnected);
                    Assert.IsFalse(conn.GetDatabase().IsConnected(default(RedisKey)));
                }
            }
            finally
            {
                Console.WriteLine(log);
            }
        }
        [Test]
        public void CreateDisconnectedNonsenseConnection_DNS()
        {
            var log = new StringWriter();
            try
            {
                using (var conn = ConnectionMultiplexer.Connect("doesnot.exist.ds.aasd981230d.com:6500,abortConnect=false", log)) {
                    Assert.IsFalse(conn.GetServer(conn.GetEndPoints().Single()).IsConnected);
                    Assert.IsFalse(conn.GetDatabase().IsConnected(default(RedisKey)));
                }
            }
            finally
            {
                Console.WriteLine(log);
            }
        }

        [Test]
        public void ConfigurationOptionsDefaultForAzure()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.windows.net");
            Assert.IsTrue(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.IsFalse(options.AbortOnConnectFail);
        }

        [Test]
        public void ConfigurationOptionsForAzureWhenSpecified()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.windows.net,abortConnect=true, version=2.1.1");
            Assert.IsTrue(options.DefaultVersion.Equals(new Version(2, 1, 1)));
            Assert.IsTrue(options.AbortOnConnectFail);
        }

        [Test]
        public void ConfigurationOptionsDefaultForAzureChina()
        {
            // added a few upper case chars to validate comparison
            var options = ConfigurationOptions.Parse("contoso.REDIS.CACHE.chinacloudapi.cn");
            Assert.IsTrue(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.IsFalse(options.AbortOnConnectFail);
        }

        [Test]
        public void ConfigurationOptionsDefaultForAzureGermany()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.cloudapi.de");
            Assert.IsTrue(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.IsFalse(options.AbortOnConnectFail);
        }

        [Test]
        public void ConfigurationOptionsDefaultForAzureUSGov()
        {
            var options = ConfigurationOptions.Parse("contoso.redis.cache.usgovcloudapi.net");
            Assert.IsTrue(options.DefaultVersion.Equals(new Version(3, 0, 0)));
            Assert.IsFalse(options.AbortOnConnectFail);
        }

        [Test]
        public void ConfigurationOptionsDefaultForNonAzure()
        {
            var options = ConfigurationOptions.Parse("redis.contoso.com");
            Assert.IsTrue(options.DefaultVersion.Equals(new Version(2, 0, 0)));
            Assert.IsTrue(options.AbortOnConnectFail);
        }

        [Test]
        public void ConfigurationOptionsDefaultWhenNoEndpointsSpecifiedYet()
        {
            var options = new ConfigurationOptions();
            Assert.IsTrue(options.DefaultVersion.Equals(new Version(2, 0, 0)));
            Assert.IsTrue(options.AbortOnConnectFail);
        }

        internal static void AssertNearlyEqual(double x, double y)
        {
            if (Math.Abs(x - y) > 0.00001) Assert.AreEqual(x, y);
        }
    }
}