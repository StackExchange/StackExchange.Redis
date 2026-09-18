using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;

namespace StackExchange.Redis.Tests;

public static class TestConfig
{
    private const string FileName = "RedisTestConfig.json";

    public static Config Current { get; }

    /// <summary>
    /// A floor, in milliseconds, for connection timeouts, from <c>REDIS_TESTS_MIN_TIMEOUT_MS</c>;
    /// zero (the default) leaves the library's own defaults alone.
    /// </summary>
    /// <remarks>
    /// This exists for CI machines that cannot honour the library's 5s defaults for reasons that have
    /// nothing to do with the code under test. It deliberately applies only where a test has not
    /// asked for a specific timeout, so tests that pick a short one to exercise timeout behaviour
    /// keep working.
    /// </remarks>
    public static int MinTimeoutMilliseconds { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("REDIS_TESTS_MIN_TIMEOUT_MS"), out var ms) && ms > 0 ? ms : 0;

#if NET
    private static int _db = 17;
#else
    private static int _db = 77;
#endif
    public static int GetDedicatedDB(IConnectionMultiplexer? conn = null)
    {
        int db = Interlocked.Increment(ref _db);
        if (conn != null) Skip.IfMissingDatabase(conn, db);
        return db;
    }

    static TestConfig()
    {
        // The suite opens a lot of connections at once (xunit runs 2x cores' worth of collections in
        // parallel), and the thread pool grows only ~1-2 threads per second past its minimum. On a
        // slow or contended machine that ramp is what turns a perfectly healthy server into
        // "Timeout performing PING (5000ms)": a synchronous caller parks waiting for a completion
        // that cannot get a thread. Raising the floor costs nothing on a fast machine, and it is the
        // same advice we give users in docs/Timeouts.md.
        try
        {
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            var target = Math.Max(64, Environment.ProcessorCount * 8);
            ThreadPool.SetMinThreads(Math.Max(workerThreads, target), Math.Max(completionPortThreads, target));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Unable to raise ThreadPool minimums: " + ex.Message);
        }

        Current = new Config();
        try
        {
            using (var stream = typeof(TestConfig).Assembly.GetManifestResourceStream("StackExchange.Redis.Tests." + FileName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream))
                    {
                        Current = JsonConvert.DeserializeObject<Config>(reader.ReadToEnd()) ?? new Config();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error Deserializing TestConfig.json: " + ex);
        }
    }

    public static bool IsServerRunning(string? host, int port)
    {
        if (host.IsNullOrEmpty())
        {
            return false;
        }

        try
        {
            using var client = new TcpClient(host, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public class Config
    {
        public bool UseSharedConnection { get; set; } = true;
        public bool RunLongRunning { get; set; }

        public string PrimaryServer { get; set; } = "127.0.0.1";
        public int PrimaryPort { get; set; } = 6379;
        public string PrimaryServerAndPort => PrimaryServer + ":" + PrimaryPort.ToString();

        public string ReplicaServer { get; set; } = "127.0.0.1";
        public int ReplicaPort { get; set; } = 6380;
        public string ReplicaServerAndPort => ReplicaServer + ":" + ReplicaPort.ToString();

        public string SecureServer { get; set; } = "127.0.0.1";
        public int SecurePort { get; set; } = 6381;
        public string SecurePassword { get; set; } = "changeme";
        public string SecureServerAndPort => SecureServer + ":" + SecurePort.ToString();

        // Separate servers for failover tests, so they don't wreak havoc on all others
        public string FailoverPrimaryServer { get; set; } = "127.0.0.1";
        public int FailoverPrimaryPort { get; set; } = 6382;
        public string FailoverPrimaryServerAndPort => FailoverPrimaryServer + ":" + FailoverPrimaryPort.ToString();

        public string FailoverReplicaServer { get; set; } = "127.0.0.1";
        public int FailoverReplicaPort { get; set; } = 6383;
        public string FailoverReplicaServerAndPort => FailoverReplicaServer + ":" + FailoverReplicaPort.ToString();

        public string IPv4Server { get; set; } = "127.0.0.1";
        public int IPv4Port { get; set; } = 6379;
        public string IPv6Server { get; set; } = "::1";
        public int IPv6Port { get; set; } = 6379;

        public string RemoteServer { get; set; } = "127.0.0.1";
        public int RemotePort { get; set; } = 6379;
        public string RemoteServerAndPort => RemoteServer + ":" + RemotePort.ToString();

        public string SentinelServer { get; set; } = "127.0.0.1";
        public int SentinelPortA { get; set; } = 26379;
        public int SentinelPortB { get; set; } = 26380;
        public int SentinelPortC { get; set; } = 26381;
        public string SentinelSeviceName { get; set; } = "myprimary";

        public string ClusterServer { get; set; } = "127.0.0.1";
        public int ClusterStartPort { get; set; } = 7000;
        public int ClusterServerCount { get; set; } = 6;
        public string ClusterServersAndPorts => string.Join(",", Enumerable.Range(ClusterStartPort, ClusterServerCount).Select(port => ClusterServer + ":" + port));

        public string? SslServer { get; set; } = "127.0.0.1";
        public int SslPort { get; set; } = 6384;
        public string SslServerAndPort => SslServer + ":" + SslPort.ToString();

        public string? RedisLabsSslServer { get; set; }
        public int RedisLabsSslPort { get; set; } = 6379;
        public string? RedisLabsPfxPath { get; set; }

        public string? AzureCacheServer { get; set; }
        public string? AzureCachePassword { get; set; }

        public string? SSDBServer { get; set; }
        public int SSDBPort { get; set; } = 8888;

        public string ProxyServer { get; set; } = "127.0.0.1";
        public int ProxyPort { get; set; } = 7015;

        public string ProxyServerAndPort => ProxyServer + ":" + ProxyPort.ToString();
        public string[] ActiveActiveEndpoints { get; set; } = [];
    }
}
