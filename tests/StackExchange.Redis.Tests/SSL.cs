using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
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
            options.CertificateValidation += ShowCertFailures(Writer);
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
                lock (log)
                {
                    Log(log.ToString());
                }
                Log("====");
                muxer.ConnectionFailed += OnConnectionFailed;
                muxer.InternalError += OnInternalError;
                var db = muxer.GetDatabase();
                await db.PingAsync().ForAwait();
                using (var file = File.Create("ssl-" + useSsl + "-" + specifyHost + ".zip"))
                {
                    muxer.ExportConfiguration(file);
                }
                RedisKey key = "SE.Redis";

                const int AsyncLoop = 2000;
                // perf; async
                await db.KeyDeleteAsync(key).ForAwait();
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < AsyncLoop; i++)
                {
                    try
                    {
                        await db.StringIncrementAsync(key, flags: CommandFlags.FireAndForget).ForAwait();
                    }
                    catch (Exception ex)
                    {
                        Log($"Failure on i={i}: {ex.Message}");
                        throw;
                    }
                }
                // need to do this inside the timer to measure the TTLB
                long value = (long)await db.StringGetAsync(key).ForAwait();
                watch.Stop();
                Assert.Equal(AsyncLoop, value);
                Log("F&F: {0} INCR, {1:###,##0}ms, {2} ops/s; final value: {3}",
                    AsyncLoop,
                    watch.ElapsedMilliseconds,
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

        //private void TestConcurrent(IDatabase db, RedisKey key, int SyncLoop, int Threads)
        //{
        //    long value;
        //    db.KeyDelete(key, CommandFlags.FireAndForget);
        //    var time = RunConcurrent(delegate
        //    {
        //        for (int i = 0; i < SyncLoop; i++)
        //        {
        //            db.StringIncrement(key);
        //        }
        //    }, Threads, timeout: 45000);
        //    value = (long)db.StringGet(key);
        //    Assert.Equal(SyncLoop * Threads, value);
        //    Log("Sync: {0} INCR using {1} threads, {2:###,##0}ms, {3} ops/s; final value: {4}",
        //        SyncLoop * Threads, Threads,
        //        (long)time.TotalMilliseconds,
        //        (long)((SyncLoop * Threads) / time.TotalSeconds),
        //        value);
        //}

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
                db.KeyDelete(key, CommandFlags.FireAndForget);
                string s = db.StringGet(key);
                Assert.Null(s);
                db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
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
                    db.KeyDelete(key, CommandFlags.FireAndForget);
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

        private void Check(string name, object x, object y)
        {
            Writer.WriteLine($"{name}: {(x == null ? "(null)" : x.ToString())} vs {(y == null ? "(null)" : y.ToString())}");
            Assert.Equal(x, y);
        }

        [Fact]
        public void Issue883_Exhaustive()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                var all = CultureInfo.GetCultures(CultureTypes.AllCultures);
                Writer.WriteLine($"Checking {all.Length} cultures...");
                foreach (var ci in all)
                {
                    Writer.WriteLine("Tessting: " + ci.Name);
                    CultureInfo.CurrentCulture = ci;

                    var a = ConnectionMultiplexer.PrepareConfig("myDNS:883,password=mypassword,connectRetry=3,connectTimeout=5000,syncTimeout=5000,defaultDatabase=0,ssl=true,abortConnect=false");
                    var b = ConnectionMultiplexer.PrepareConfig(new ConfigurationOptions
                    {
                        EndPoints = { { "myDNS", 883 } },
                        Password = "mypassword",
                        ConnectRetry = 3,
                        ConnectTimeout = 5000,
                        SyncTimeout = 5000,
                        DefaultDatabase = 0,
                        Ssl = true,
                        AbortOnConnectFail = false,
                    });
                    Writer.WriteLine($"computed: {b.ToString(true)}");

                    Writer.WriteLine("Checking endpoints...");
                    var c = a.EndPoints.Cast<DnsEndPoint>().Single();
                    var d = b.EndPoints.Cast<DnsEndPoint>().Single();
                    Check(nameof(c.Host), c.Host, d.Host);
                    Check(nameof(c.Port), c.Port, d.Port);
                    Check(nameof(c.AddressFamily), c.AddressFamily, d.AddressFamily);

                    var fields = typeof(ConfigurationOptions).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Writer.WriteLine($"Comparing {fields.Length} fields...");
                    Array.Sort(fields, (x, y) => string.CompareOrdinal(x.Name, y.Name));
                    foreach (var field in fields)
                    {
                        Check(field.Name, field.GetValue(a), field.GetValue(b));
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
            }
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
            options.CertificateValidation += ShowCertFailures(Writer);
            using (var conn = ConnectionMultiplexer.Connect(options))
            {
                conn.GetDatabase().Ping();
            }
        }

        public static RemoteCertificateValidationCallback ShowCertFailures(TextWriterOutputHelper output) {
            if (output == null) return null;

            return (sender, certificate, chain, sslPolicyErrors) =>
            {
                void WriteStatus(X509ChainStatus[] status)
                {
                    if (status != null)
                    {
                        for (int i = 0; i < status.Length; i++)
                        {
                            var item = status[i];
                            output.WriteLine($"\tstatus {i}: {item.Status}, {item.StatusInformation}");
                        }
                    }
                }
                lock (output)
                {
                    if (certificate != null)
                    {
                        output.WriteLine($"Subject: {certificate.Subject}");
                    }
                    output.WriteLine($"Policy errors: {sslPolicyErrors}");
                    if (chain != null)
                    {
                        WriteStatus(chain.ChainStatus);

                        var elements = chain.ChainElements;
                        if (elements != null)
                        {
                            int index = 0;
                            foreach (var item in elements)
                            {
                                output.WriteLine($"{index++}: {item.Certificate.Subject}; {item.Information}");
                                WriteStatus(item.ChainElementStatus);
                            }
                        }
                    }
                }
                return sslPolicyErrors == SslPolicyErrors.None;
            };
        }

        [Fact]
        public void SSLParseViaConfig_Issue883_ConfigString()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCachePassword), TestConfig.Current.AzureCachePassword);

            var configString = $"{TestConfig.Current.AzureCacheServer}:6380,password={TestConfig.Current.AzureCachePassword},connectRetry=3,connectTimeout=5000,syncTimeout=5000,defaultDatabase=0,ssl=true,abortConnect=false";
            var options = ConfigurationOptions.Parse(configString);
            options.CertificateValidation += ShowCertFailures(Writer);
            using (var conn = ConnectionMultiplexer.Connect(options))
            {
                conn.GetDatabase().Ping();
            }
        }

        [Fact]
        public void ConfigObject_Issue1407_ToStringIncludesSslProtocols()
        {
            var sslProtocols = SslProtocols.Tls12 | SslProtocols.Tls;
            var sourceOptions = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                Ssl = true,
                SslProtocols = sslProtocols,
                ConnectRetry = 3,
                ConnectTimeout = 5000,
                SyncTimeout = 5000,
                DefaultDatabase = 0,
                EndPoints = { { "endpoint.test", 6380 } },
                Password = "123456"
            };

            var targetOptions = ConfigurationOptions.Parse(sourceOptions.ToString());
            Assert.Equal(sourceOptions.SslProtocols, targetOptions.SslProtocols);
        }
    }
}
