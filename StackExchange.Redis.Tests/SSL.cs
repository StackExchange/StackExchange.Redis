using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class SSL : TestBase
    {
        public SSL(ITestOutputHelper output) : base (output) { }

        [Theory]
        [InlineData(null, true)] // auto-infer port (but specify 6380)
        [InlineData(6380, true)] // all explicit
        // (note the 6379 port is closed)
        public void ConnectToAzure(int? port, bool ssl)
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCachePassword), TestConfig.Current.AzureCachePassword);

            var options = new ConfigurationOptions();
            if (port == null)
            {
                options.EndPoints.Add(TestConfig.Current.AzureCacheServer);
            }
            else
            {
                options.EndPoints.Add(TestConfig.Current.AzureCacheServer, port.Value);
            }
            options.Ssl = ssl;
            options.Password = TestConfig.Current.AzureCachePassword;
            Log(options.ToString());
            using (var connection = ConnectionMultiplexer.Connect(options))
            {
                var ttl = connection.GetDatabase().Ping();
                Log(ttl.ToString());
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ConnectToSSLServer(bool useSsl, bool specifyHost)
        {
            var server = TestConfig.Current.SslServer;
            int? port = TestConfig.Current.SslPort;
            string password = "";
            bool isAzure = false;
            if (string.IsNullOrWhiteSpace(server) && useSsl)
            {
                // we can bounce it past azure instead?
                server = TestConfig.Current.AzureCacheServer;
                password = TestConfig.Current.AzureCachePassword;
                port = null;
                isAzure = true;
            }
            Skip.IfNoConfig(nameof(TestConfig.Config.SslServer), server);

            var config = new ConfigurationOptions
            {
                AllowAdmin = true,
                SyncTimeout = Debugger.IsAttached ? int.MaxValue : 5000,
                Password = password,
            };
            var map = new Dictionary<string, string>
            {
                ["config"] = null // don't rely on config working
            };
            if (!isAzure) map["cluster"] = null;
            config.CommandMap = CommandMap.Create(map);
            if (port != null) config.EndPoints.Add(server, port.Value);
            else config.EndPoints.Add(server);

            if (useSsl)
            {
                config.Ssl = useSsl;
                if (specifyHost)
                {
                    config.SslHost = server;
                }
                config.CertificateValidation += (sender, cert, chain, errors) =>
                {
                    Log("errors: " + errors);
                    Log("cert issued to: " + cert.Subject);
                    return true; // fingers in ears, pretend we don't know this is wrong
                };
            }

            var configString = config.ToString();
            Log("config: " + configString);
            var clone = ConfigurationOptions.Parse(configString);
            Assert.Equal(configString, clone.ToString());

            using (var log = new StringWriter())
            using (var muxer = ConnectionMultiplexer.Connect(config, log))
            {
                Log("Connect log:");
                Log(log.ToString());
                Log("====");
                muxer.ConnectionFailed += OnConnectionFailed;
                muxer.InternalError += OnInternalError;
                var db = muxer.GetDatabase();
                await db.PingAsync();
                using (var file = File.Create("ssl-" + useSsl + "-" + specifyHost + ".zip"))
                {
                    muxer.ExportConfiguration(file);
                }
                RedisKey key = "SE.Redis";

                const int AsyncLoop = 2000;
                // perf; async
                await db.KeyDeleteAsync(key);
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < AsyncLoop; i++)
                {
                    try
                    {
                        await db.StringIncrementAsync(key, flags: CommandFlags.FireAndForget);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failure on i={i}: {ex.Message}");
                        throw;
                    }
                }
                // need to do this inside the timer to measure the TTLB
                long value = (long)await db.StringGetAsync(key);
                watch.Stop();
                Assert.Equal(AsyncLoop, value);
                Log("F&F: {0} INCR, {1:###,##0}ms, {2} ops/s; final value: {3}",
                    AsyncLoop,
                    (long)watch.ElapsedMilliseconds,
                    (long)(AsyncLoop / watch.Elapsed.TotalSeconds),
                    value);

                // perf: sync/multi-threaded
                // TestConcurrent(db, key, 30, 10);
                //TestConcurrent(db, key, 30, 20);
                //TestConcurrent(db, key, 30, 30);
                //TestConcurrent(db, key, 30, 40);
                //TestConcurrent(db, key, 30, 50);
            }
        }

        private void TestConcurrent(IDatabase db, RedisKey key, int SyncLoop, int Threads)
        {
            long value;
            db.KeyDelete(key, CommandFlags.FireAndForget);
            var time = RunConcurrent(delegate
            {
                for (int i = 0; i < SyncLoop; i++)
                {
                    db.StringIncrement(key);
                }
            }, Threads, timeout: 45000);
            value = (long)db.StringGet(key);
            Assert.Equal(SyncLoop * Threads, value);
            Log("Sync: {0} INCR using {1} threads, {2:###,##0}ms, {3} ops/s; final value: {4}",
                SyncLoop * Threads, Threads,
                (long)time.TotalMilliseconds,
                (long)((SyncLoop * Threads) / time.TotalSeconds),
                value);
        }

        [Fact]
        public void RedisLabsSSL()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.RedisLabsSslServer), TestConfig.Current.RedisLabsSslServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.RedisLabsPfxPath), TestConfig.Current.RedisLabsPfxPath);


            var cert = new X509Certificate2(TestConfig.Current.RedisLabsPfxPath, "");
            Assert.NotNull(cert);
            Writer.WriteLine("Thumbprint: " + cert.Thumbprint);

            int timeout = 5000;
            if (Debugger.IsAttached) timeout *= 100;
            var options = new ConfigurationOptions
            {
                EndPoints = { { TestConfig.Current.RedisLabsSslServer, TestConfig.Current.RedisLabsSslPort } },
                ConnectTimeout = timeout,
                AllowAdmin = true,
                CommandMap = CommandMap.Create(new HashSet<string> {
                    "subscribe", "unsubscribe", "cluster"
                }, false)
            };
            
            options.TrustIssuer("redislabs_ca.pem");



            if (!Directory.Exists(Me())) Directory.CreateDirectory(Me());
#if LOGOUTPUT
            ConnectionMultiplexer.EchoPath = Me();
#endif
            options.Ssl = true;
            options.CertificateSelection += delegate
            {
                return cert;
            };
            RedisKey key = Me();
            using (var conn = ConnectionMultiplexer.Connect(options))
            {
                var db = conn.GetDatabase();
                db.KeyDelete(key);
                string s = db.StringGet(key);
                Assert.Null(s);
                db.StringSet(key, "abc");
                s = db.StringGet(key);
                Assert.Equal("abc", s);

                var latency = db.Ping();
                Log("RedisLabs latency: {0:###,##0.##}ms", latency.TotalMilliseconds);

                using (var file = File.Create("RedisLabs.zip"))
                {
                    conn.ExportConfiguration(file);
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RedisLabsEnvironmentVariableClientCertificate(bool setEnv)
        {
            try
            {
                Skip.IfNoConfig(nameof(TestConfig.Config.RedisLabsSslServer), TestConfig.Current.RedisLabsSslServer);
                Skip.IfNoConfig(nameof(TestConfig.Config.RedisLabsPfxPath), TestConfig.Current.RedisLabsPfxPath);

                if (setEnv)
                {
                    Environment.SetEnvironmentVariable("SERedis_ClientCertPfxPath", TestConfig.Current.RedisLabsPfxPath);
                    Environment.SetEnvironmentVariable("SERedis_IssuerCertPath", "redislabs_ca.pem");
                    // check env worked
                    Assert.Equal(TestConfig.Current.RedisLabsPfxPath, Environment.GetEnvironmentVariable("SERedis_ClientCertPfxPath"));
                    Assert.Equal("redislabs_ca.pem", Environment.GetEnvironmentVariable("SERedis_IssuerCertPath"));
                }
                int timeout = 5000;
                if (Debugger.IsAttached) timeout *= 100;
                var options = new ConfigurationOptions
                {
                    EndPoints = { { TestConfig.Current.RedisLabsSslServer, TestConfig.Current.RedisLabsSslPort } },
                    ConnectTimeout = timeout,
                    AllowAdmin = true,
                    CommandMap = CommandMap.Create(new HashSet<string> {
                        "subscribe", "unsubscribe", "cluster"
                    }, false)
                };
                
                if (!Directory.Exists(Me())) Directory.CreateDirectory(Me());
#if LOGOUTPUT
            ConnectionMultiplexer.EchoPath = Me();
#endif
                options.Ssl = true;
                RedisKey key = Me();
                using (var conn = ConnectionMultiplexer.Connect(options))
                {
                    if (!setEnv) Assert.True(false, "Could not set environment");

                    var db = conn.GetDatabase();
                    db.KeyDelete(key);
                    string s = db.StringGet(key);
                    Assert.Null(s);
                    db.StringSet(key, "abc");
                    s = db.StringGet(key);
                    Assert.Equal("abc", s);

                    var latency = db.Ping();
                    Log("RedisLabs latency: {0:###,##0.##}ms", latency.TotalMilliseconds);

                    using (var file = File.Create("RedisLabs.zip"))
                    {
                        conn.ExportConfiguration(file);
                    }
                }
            }
            catch (RedisConnectionException ex)
            {
                if (setEnv || ex.FailureType != ConnectionFailureType.UnableToConnect)
                {
                    throw;
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("SERedis_ClientCertPfxPath", null);
            }
        }

        [Fact]
        public void SSLHostInferredFromEndpoints()
        {
            var options = new ConfigurationOptions()
            {
                EndPoints = {
                              { "mycache.rediscache.windows.net", 15000},
                              { "mycache.rediscache.windows.net", 15001 },
                              { "mycache.rediscache.windows.net", 15002 },
                            }
            };
            options.Ssl = true;
            Assert.True(options.SslHost == "mycache.rediscache.windows.net");
            options = new ConfigurationOptions()
            {
                EndPoints = {
                              { "121.23.23.45", 15000},
                            }
            };
            Assert.True(options.SslHost == null);
        }

        [Fact]
        public void SSLParseViaConfig_Issue883_ConfigObject()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCachePassword), TestConfig.Current.AzureCachePassword);

            var options = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                Ssl = true,
                ConnectRetry = 3,
                ConnectTimeout = 5000,
                SyncTimeout = 5000,
                DefaultDatabase = 0,
                EndPoints = { { TestConfig.Current.AzureCacheServer, 6380 } },
                Password = TestConfig.Current.AzureCachePassword
            };
            using (var conn = ConnectionMultiplexer.Connect(options))
            {
                conn.GetDatabase().Ping();
            }
        }

        [Fact]
        public void SSLParseViaConfig_Issue883_ConfigString()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCachePassword), TestConfig.Current.AzureCachePassword);

            var configString = $"{TestConfig.Current.AzureCacheServer}:6380,password={TestConfig.Current.AzureCachePassword},connectRetry=3,connectTimeout=5000,syncTimeout=5000,defaultDatabase=0,ssl=true,abortConnect=false";
            var options = ConfigurationOptions.Parse(configString);
            using (var conn = ConnectionMultiplexer.Connect(options))
            {
                conn.GetDatabase().Ping();
            }
        }
    }
}
