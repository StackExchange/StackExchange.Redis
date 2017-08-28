using System.IO;
using Jil;
using System;

namespace StackExchange.Redis.Tests
{
    public static class TestConfig
    {
        private const string FilePath = "_TestConfig.json";

        public static Config Current { get; }

        static TestConfig()
        {
            Current = new Config();
            try
            {
                if (File.Exists(FilePath))
                {
                    var file = File.ReadAllText(FilePath);
                    Current = JSON.Deserialize<Config>(file);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error Deserializing _TestConfig.json: " + ex);
            }
        }

        public class Config
        {
            public string MasterServer { get; set; } = "127.0.0.1";
            public int MasterPort { get; set; } = 6379;

            public string SlaveServer { get; set; } = "127.0.0.1";
            public int SlavePort { get; set; } = 6380;

            public string SecureServer { get; set; } = "127.0.0.1";
            public int SecurePort { get; set; } = 6381;
            public string SecurePassword { get; set; } = "changeme";

            public string RemoteServer { get; set; } = "127.0.0.1";
            public int RemotePort { get; set; } = 6379;

            public string SentinelServer { get; set; } = "127.0.0.1";
            public int SentinelPort { get; set; } = 26379;
            public string SentinelSeviceName { get; set; } = "mymaster";

            public string ClusterServer { get; set; } = "127.0.0.1";
            public int ClusterStartPort { get; set; } = 7000;
            public int ClusterServerCount { get; set; } = 6;

            public string SslServer { get; set; }
            public int SslPort { get; set; }

            public string RedisLabsSslServer { get; set; }
            public int RedisLabsSslPort { get; set; } = 6379;
            public string RedisLabsPfxPath { get; set; }

            public string AzureCacheServer { get; set; }
            public string AzureCachePassword { get; set; }
        }
    }
}
