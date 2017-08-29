using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class Performance : BookSleeveTestBase
    {
        public Performance(ITestOutputHelper output) : base(output) { }

        [FactLongRunning]
        public void VerifyPerformanceImprovement()
        {
            int asyncTimer, sync, op = 0, asyncFaF, syncFaF;
            using (var muxer = GetUnsecuredConnection())
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
                var final = new Task<RedisValue>[5];
                for (int db = 0; db < 5; db++)
                    final[db] = muxer.GetDatabase(db).StringGetAsync("perftest");
                muxer.WaitAll(final);
                timer.Stop();
                asyncTimer = (int)timer.ElapsedMilliseconds;
                Output.WriteLine("async to completion (local): {0}ms", timer.ElapsedMilliseconds);
                for (int db = 0; db < 5; db++)
                {
                    Assert.Equal(1000, (long)final[db].Result); // "async, db:" + db
                }
            }

            using (var conn = new RedisSharp.Redis(TestConfig.Current.MasterServer, TestConfig.Current.MasterPort))
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
                Output.WriteLine("sync to completion (local): {0}ms", timer.ElapsedMilliseconds);
                for (int db = 0; db < 5; db++)
                {
                    Assert.Equal("1000", final[db]); // "async, db:" + db
                }
            }
            int effectiveAsync = ((10 * asyncTimer) + 3) / 10;
            int effectiveSync = ((10 * sync) + (op * 3)) / 10;
            Output.WriteLine("async to completion with assumed 0.3ms LAN latency: " + effectiveAsync);
            Output.WriteLine("sync to completion with assumed 0.3ms LAN latency: " + effectiveSync);
            Output.WriteLine("fire-and-forget: {0}ms sync vs {1}ms async ", syncFaF, asyncFaF);
            Assert.True(effectiveAsync < effectiveSync, "Everything");
            Assert.True(asyncFaF < syncFaF, "Fire and Forget");
        }
    }
}
