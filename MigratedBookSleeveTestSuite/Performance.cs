using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using StackExchange.Redis;

namespace Tests
{
    [TestFixture]
    public class Performance
    {
        [Test]
        public void VerifyPerformanceImprovement()
        {
            int asyncTimer, sync, op = 0, asyncFaF, syncFaF;
            using (var muxer= Config.GetUnsecuredConnection())
            {
                // do these outside the timings, just to ensure the core methods are JITted etc
                for (int db = 0; db < 5; db++)
                {
                    muxer.GetDatabase(db).KeyDeleteAsync("perftest");
                }

                var timer = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++)
                {
                    // want to test multiplex scenario; test each db, but to make it fair we'll
                    // do in batches of 10 on each
                    for (int db = 0; db < 5; db++)
                    {
                        var conn = muxer.GetDatabase(db);
                        for (int j = 0; j < 10; j++)
                            conn.StringIncrementAsync("perftest");
                    }
                }
                asyncFaF = (int)timer.ElapsedMilliseconds;
                Task<RedisValue>[] final = new Task<RedisValue>[5];
                for (int db = 0; db < 5; db++)
                    final[db] = muxer.GetDatabase(db).StringGetAsync("perftest");
                muxer.WaitAll(final);
                timer.Stop();
                asyncTimer = (int)timer.ElapsedMilliseconds;
                Console.WriteLine("async to completion (local): {0}ms", timer.ElapsedMilliseconds);
                for (int db = 0; db < 5; db++)
                    Assert.AreEqual(1000, (long)final[db].Result, "async, db:" + db);
            }

            using (var conn = new Redis(Config.LocalHost, 6379))
            {
                // do these outside the timings, just to ensure the core methods are JITted etc
                for (int db = 0; db < 5; db++)
                {
                    conn.Db = db;
                    conn.Remove("perftest");
                }

                var timer = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++)
                {
                    // want to test multiplex scenario; test each db, but to make it fair we'll
                    // do in batches of 10 on each
                    for (int db = 0; db < 5; db++)
                    {
                        conn.Db = db;
                        op++;
                        for (int j = 0; j < 10; j++)
                        {
                            conn.Increment("perftest");
                            op++;
                        }
                    }
                }
                syncFaF = (int)timer.ElapsedMilliseconds;
                string[] final = new string[5];
                for (int db = 0; db < 5; db++)
                {
                    conn.Db = db;
                    final[db] = Encoding.ASCII.GetString(conn.Get("perftest"));
                }
                timer.Stop();
                sync = (int)timer.ElapsedMilliseconds;
                Console.WriteLine("sync to completion (local): {0}ms", timer.ElapsedMilliseconds);
                for (int db = 0; db < 5; db++)
                    Assert.AreEqual("1000", final[db], "async, db:" + db);
            }
            int effectiveAsync = ((10 * asyncTimer) + 3) / 10;
            int effectiveSync = ((10 * sync) + (op * 3)) / 10;
            Console.WriteLine("async to completion with assumed 0.3ms LAN latency: " + effectiveAsync);
            Console.WriteLine("sync to completion with assumed 0.3ms LAN latency: " + effectiveSync);
            Console.WriteLine("fire-and-forget: {0}ms sync vs {1}ms async ", syncFaF, asyncFaF);
            Assert.Less(effectiveAsync, effectiveSync, "Everything");
            Assert.Less(asyncFaF, syncFaF, "Fire and Forget");
        }
    }
}
