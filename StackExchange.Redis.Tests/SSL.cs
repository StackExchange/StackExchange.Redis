using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using NUnit.Framework;
using System.Linq;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class SSL : TestBase
    {
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
                config.UseSsl = useSsl;
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
    }
}
