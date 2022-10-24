using System.IO;
using System;
using Newtonsoft.Json;
using System.Threading;
using System.Linq;
using System.Net.Sockets;

namespace StackExchange.Redis.Tests;

public static class TestConfig
{
    private const string FileName = "TestConfig.json";

    public static Config Current { get; }

    private static int _db = 17;
    public static int GetDedicatedDB(IConnectionMultiplexer? conn = null)
    {
        int db = Interlocked.Increment(ref _db);
        if (conn != null) Skip.IfMissingDatabase(conn, db);
        return db;
    }

    static TestConfig()
    {
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
        public bool LogToConsole { get; set; }

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
    }
}
