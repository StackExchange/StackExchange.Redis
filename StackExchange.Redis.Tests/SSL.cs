using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class SSL : TestBase
    {
        [Test]
        [TestCase(null, true)]
        [TestCase(null, false)]
        [TestCase(6380, true)]
        [TestCase(6379, false)]
        public void ConnectToAzure(int? port, bool ssl)
        {
            string name, password;
            GetAzureCredentials(out name, out password);
            var options = new ConfigurationOptions();
            if (port == null)
            {
                options.EndPoints.Add(name + ".redis.cache.windows.net");
            } else
            {
                options.EndPoints.Add(name + ".redis.cache.windows.net", port.Value);
            }
            options.Ssl = ssl;
            options.Password = password;
            Console.WriteLine(options);
            using(var connection = ConnectionMultiplexer.Connect(options))
            {
                var ttl = connection.GetDatabase().Ping();
                Console.WriteLine(ttl);
            }
        }

        [Test]
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ConnectToSSLServer(bool useSsl, bool specifyHost)
        {
            string host = null;
            
            const string path = @"D:\RedisSslHost.txt"; // because I choose not to advertise my server here!
            if (File.Exists(path)) host = File.ReadLines(path).First();
            if (string.IsNullOrWhiteSpace(host)) Assert.Inconclusive("no ssl host specified at: " + path);

            var config = new ConfigurationOptions
            {
                CommandMap = CommandMap.Create( // looks like "config" is disabled
                    new Dictionary<string, string>
                    {
                        { "config", null },
                        { "cluster", null }
                    }
                ),
                EndPoints = { { host } },
                AllowAdmin = true,
                SyncTimeout = Debugger.IsAttached ? int.MaxValue : 5000
            };
            if(useSsl)
            {
                config.Ssl = useSsl;
                if (specifyHost)
                {
                    config.SslHost = host;
                }
                config.CertificateValidation += (sender, cert, chain, errors) =>
                {
                    Console.WriteLine("errors: " + errors);
                    Console.WriteLine("cert issued to: " + cert.Subject);
                    return true; // fingers in ears, pretend we don't know this is wrong
                };
            }

            var configString = config.ToString();
            Console.WriteLine("config: " + configString);
            var clone = ConfigurationOptions.Parse(configString);
            Assert.AreEqual(configString, clone.ToString(), "config string");

            using(var log = new StringWriter())
            using (var muxer = ConnectionMultiplexer.Connect(config, log))
            {
                Console.WriteLine("Connect log:");
                Console.WriteLine(log);
                Console.WriteLine("====");
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
                Assert.AreEqual(AsyncLoop, value);
                Console.WriteLine("F&F: {0} INCR, {1:###,##0}ms, {2} ops/s; final value: {3}",
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


        private static void TestConcurrent(IDatabase db, RedisKey key, int SyncLoop, int Threads)
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
            Assert.AreEqual(SyncLoop * Threads, value);
            Console.WriteLine("Sync: {0} INCR using {1} threads, {2:###,##0}ms, {3} ops/s; final value: {4}",
                SyncLoop * Threads, Threads,
                (long)time.TotalMilliseconds,
                (long)((SyncLoop * Threads) / time.TotalSeconds),
                value);
        }



        const string RedisLabsSslHostFile = @"d:\RedisLabsSslHost.txt";
        const string RedisLabsPfxPath = @"d:\RedisLabsUser.pfx";

        [Test]
        public void RedisLabsSSL()
        {
            if (!File.Exists(RedisLabsSslHostFile)) Assert.Inconclusive();
            string hostAndPort = File.ReadAllText(RedisLabsSslHostFile);
            int timeout = 5000;
            if (Debugger.IsAttached) timeout *= 100;
            var options = new ConfigurationOptions
            {
                EndPoints = { hostAndPort },
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
            options.CertificateSelection += delegate {
                return new X509Certificate2(RedisLabsPfxPath, "");
            };
            RedisKey key = Me();
            using(var conn = ConnectionMultiplexer.Connect(options))
            {
                var db = conn.GetDatabase();
                db.KeyDelete(key);
                string s = db.StringGet(key);
                Assert.IsNull(s);
                db.StringSet(key, "abc");
                s = db.StringGet(key);
                Assert.AreEqual("abc", s);
                
                var latency = db.Ping();
                Console.WriteLine("RedisLabs latency: {0:###,##0.##}ms", latency.TotalMilliseconds);

                using (var file = File.Create("RedisLabs.zip"))
                {
                    conn.ExportConfiguration(file);
                }
            }
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void RedisLabsEnvironmentVariableClientCertificate(bool setEnv)
        {
            try
            {
                if (setEnv)
                {
                    Environment.SetEnvironmentVariable("SERedis_ClientCertPfxPath", RedisLabsPfxPath);
                }
                if (!File.Exists(RedisLabsSslHostFile)) Assert.Inconclusive();
                string hostAndPort = File.ReadAllText(RedisLabsSslHostFile);
                int timeout = 5000;
                if (Debugger.IsAttached) timeout *= 100;
                var options = new ConfigurationOptions
                {
                    EndPoints = { hostAndPort },
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
                    if (!setEnv) Assert.Fail();

                    var db = conn.GetDatabase();
                    db.KeyDelete(key);
                    string s = db.StringGet(key);
                    Assert.IsNull(s);
                    db.StringSet(key, "abc");
                    s = db.StringGet(key);
                    Assert.AreEqual("abc", s);

                    var latency = db.Ping();
                    Console.WriteLine("RedisLabs latency: {0:###,##0.##}ms", latency.TotalMilliseconds);

                    using (var file = File.Create("RedisLabs.zip"))
                    {
                        conn.ExportConfiguration(file);
                    }
                }
            }
            catch(RedisConnectionException ex)
            {
                if(setEnv || ex.FailureType != ConnectionFailureType.UnableToConnect)
                {
                    throw;
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("SERedis_ClientCertPfxPath", null);
            }

        }

        [Test]
        public void SSLHostInferredFromEndpoints() {
            var options = new ConfigurationOptions() {
                EndPoints = { 
                              { "mycache.rediscache.windows.net", 15000},
                              { "mycache.rediscache.windows.net", 15001 },
                              { "mycache.rediscache.windows.net", 15002 },
                            }
                };
            options.Ssl = true;
            Assert.True(options.SslHost == "mycache.rediscache.windows.net");
            options = new ConfigurationOptions() {
                EndPoints = { 
                              { "121.23.23.45", 15000},
                            }
            };
            Assert.True(options.SslHost == null);
        }

    }
}
