using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class BookSleeveTestBase
    {
        public ITestOutputHelper Output { get; }
        public BookSleeveTestBase(ITestOutputHelper output) => Output = output;

        static BookSleeveTestBase()
        {
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Trace.WriteLine(args.Exception, "UnobservedTaskException");
                args.SetObserved();
            };
        }

        public const string LocalHost = "127.0.0.1"; //"192.168.0.10"; //"127.0.0.1";
        public const string RemoteHost = "127.0.0.1"; // "ubuntu";

        private const int
            unsecuredPort = 6379,
            securedPort = 6381,
            clusterPort0 = 7000,
            clusterPort1 = 7001,
            clusterPort2 = 7002;

        public static string CreateUniqueName() => Guid.NewGuid().ToString("N");
        internal static IServer GetServer(ConnectionMultiplexer conn) => conn.GetServer(conn.GetEndPoints()[0]);
        private static readonly SocketManager socketManager = new SocketManager();

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
                SocketManager = socketManager,
                ResponseTimeout = ioTimeout
            };
            var conn = ConnectionMultiplexer.Connect(options);
            conn.InternalError += (s, args) => Trace.WriteLine(args.Exception.Message, args.Origin);
            if (open && waitForOpen)
            {
                conn.GetDatabase().Ping();
            }
            return conn;
        }

        internal static ConnectionMultiplexer GetUnsecuredConnection(bool open = true, bool allowAdmin = false, bool waitForOpen = false, int syncTimeout = 5000, int ioTimeout = 5000)
        {
            return GetConnection(LocalHost, unsecuredPort, open, allowAdmin, waitForOpen, syncTimeout, ioTimeout);
        }

        internal static ConnectionMultiplexer GetSecuredConnection()
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { { LocalHost, securedPort } },
                Password = "changeme",
                SyncTimeout = 6000,
                SocketManager = socketManager
            };
            var conn = ConnectionMultiplexer.Connect(options);
            conn.InternalError += (s, args) => Trace.WriteLine(args.Exception.Message, args.Origin);
            return conn;
        }

        internal static RedisFeatures GetFeatures(ConnectionMultiplexer muxer) => GetServer(muxer).Features;

        internal static void AssertNearlyEqual(double x, double y)
        {
            if (Math.Abs(x - y) > 0.00001) Assert.Equal(x, y);
        }
    }
}
