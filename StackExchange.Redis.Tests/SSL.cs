using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class SSL : TestBase
    {
        public SSL(ITestOutputHelper output) : base (output) { }

        [Theory]
        [InlineData(null, true)]
        [InlineData(null, false)]
        [InlineData(6380, true)]
        [InlineData(6379, false)]
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
            Output.WriteLine(options.ToString());
            using (var connection = ConnectionMultiplexer.Connect(options))
            {
                var ttl = connection.GetDatabase().Ping();
                Output.WriteLine(ttl.ToString());
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ConnectToSSLServer(bool useSsl, bool specifyHost)
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.SslServer), TestConfig.Current.SslServer);

            var config = new ConfigurationOptions
            {
                CommandMap = CommandMap.Create( // looks like "config" is disabled
                    new Dictionary<string, string>
                    {
                        ["config"] = null,
                        ["cluster"] = null
                    }
                ),
                EndPoints = { { TestConfig.Current.SslServer, TestConfig.Current.SslPort } },
                AllowAdmin = true,
                SyncTimeout = Debugger.IsAttached ? int.MaxValue : 5000
            };
            if (useSsl)
            {
                config.Ssl = useSsl;
                if (specifyHost)
                {
                    config.SslHost = TestConfig.Current.SslServer;
                }
                config.CertificateValidation += (sender, cert, chain, errors) =>
                {
                    Output.WriteLine("errors: " + errors);
                    Output.WriteLine("cert issued to: " + cert.Subject);
                    return true; // fingers in ears, pretend we don't know this is wrong
                };
            }

            var configString = config.ToString();
            Output.WriteLine("config: " + configString);
            var clone = ConfigurationOptions.Parse(configString);
            Assert.Equal(configString, clone.ToString());

            using (var log = new StringWriter())
            using (var muxer = ConnectionMultiplexer.Connect(config, log))
            {
                Output.WriteLine("Connect log:");
                Output.WriteLine(log.ToString());
                Output.WriteLine("====");
                muxer.ConnectionFailed += OnConnectionFailed;
                muxer.InternalError += OnInternalError;
                var db = muxer.GetDatabase();
                db.Ping();
                using (var file = File.Create("ssl-" + useSsl + "-" + specifyHost + ".zip"))
                {
                    muxer.ExportConfiguration(file);
                }
                RedisKey key = "SE.Redis";

                const int AsyncLoop = 2000;
                // perf; async
                db.KeyDelete(key, CommandFlags.FireAndForget);
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < AsyncLoop; i++)
                {
                    db.StringIncrement(key, flags: CommandFlags.FireAndForget);
                }
                // need to do this inside the timer to measure the TTLB
                long value = (long)db.StringGet(key);
                watch.Stop();
                Assert.Equal(AsyncLoop, value);
                Output.WriteLine("F&F: {0} INCR, {1:###,##0}ms, {2} ops/s; final value: {3}",
                    AsyncLoop,
                    (long)watch.ElapsedMilliseconds,
                    (long)(AsyncLoop / watch.Elapsed.TotalSeconds),
                    value);

                // perf: sync/multi-threaded
                TestConcurrent(db, key, 30, 10);
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
            Output.WriteLine("Sync: {0} INCR using {1} threads, {2:###,##0}ms, {3} ops/s; final value: {4}",
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
            options.CertificateSelection += delegate
            {
                return new X509Certificate2(TestConfig.Current.RedisLabsPfxPath, "");
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
                Output.WriteLine("RedisLabs latency: {0:###,##0.##}ms", latency.TotalMilliseconds);

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
                    Output.WriteLine("RedisLabs latency: {0:###,##0.##}ms", latency.TotalMilliseconds);

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
    }
}