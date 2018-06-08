using StackExchange.Redis.Tests.Helpers;
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
        public BookSleeveTestBase(ITestOutputHelper output)
        {
            Output = output;
            Output.WriteFrameworkVersion();
        }

        static BookSleeveTestBase()
        {
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Trace.WriteLine(args.Exception, "UnobservedTaskException");
                args.SetObserved();
            };
        }

        public static string CreateUniqueName() => Guid.NewGuid().ToString("N");
        internal static IServer GetServer(ConnectionMultiplexer conn) => conn.GetServer(conn.GetEndPoints()[0]);
        private static readonly SocketManager socketManager = new SocketManager();

        internal static ConnectionMultiplexer GetRemoteConnection(bool open = true, bool allowAdmin = false, bool waitForOpen = false, int syncTimeout = 5000, int ioTimeout = 5000)
        {
            return GetConnection(TestConfig.Current.RemoteServer, TestConfig.Current.RemotePort, open, allowAdmin, waitForOpen, syncTimeout, ioTimeout);
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
            return GetConnection(TestConfig.Current.MasterServer, TestConfig.Current.MasterPort, open, allowAdmin, waitForOpen, syncTimeout, ioTimeout);
        }

        internal static ConnectionMultiplexer GetSecuredConnection()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.SecureServer), TestConfig.Current.SecureServer);

            var options = new ConfigurationOptions
            {
                EndPoints = { { TestConfig.Current.SecureServer, TestConfig.Current.SecurePort } },
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
