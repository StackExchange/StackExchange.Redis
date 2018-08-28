using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class Performance : TestBase
    {
        public Performance(ITestOutputHelper output) : base(output) { }

        [FactLongRunning]
        public void VerifyPerformanceImprovement()
        {
            int asyncTimer, sync, op = 0, asyncFaF, syncFaF;
            var key = Me();
            using (var muxer = Create())
            {
                // do these outside the timings, just to ensure the core methods are JITted etc
                for (int db = 0; db < 5; db++)
                {
                    muxer.GetDatabase(db).KeyDeleteAsync(key);
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
                            conn.StringIncrementAsync(key);
                    }
                }
                asyncFaF = (int)timer.ElapsedMilliseconds;
                var final = new Task<RedisValue>[5];
                for (int db = 0; db < 5; db++)
                    final[db] = muxer.GetDatabase(db).StringGetAsync(key);
                muxer.WaitAll(final);
                timer.Stop();
                asyncTimer = (int)timer.ElapsedMilliseconds;
                Log("async to completion (local): {0}ms", timer.ElapsedMilliseconds);
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
                    conn.Remove(key);
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
                            conn.Increment(key);
                            op++;
                        }
                    }
                }
                syncFaF = (int)timer.ElapsedMilliseconds;
                string[] final = new string[5];
                for (int db = 0; db < 5; db++)
                {
                    conn.Db = db;
                    final[db] = Encoding.ASCII.GetString(conn.Get(key));
                }
                timer.Stop();
                sync = (int)timer.ElapsedMilliseconds;
                Log("sync to completion (local): {0}ms", timer.ElapsedMilliseconds);
                for (int db = 0; db < 5; db++)
                {
                    Assert.Equal("1000", final[db]); // "async, db:" + db
                }
            }
            int effectiveAsync = ((10 * asyncTimer) + 3) / 10;
            int effectiveSync = ((10 * sync) + (op * 3)) / 10;
            Log("async to completion with assumed 0.3ms LAN latency: " + effectiveAsync);
            Log("sync to completion with assumed 0.3ms LAN latency: " + effectiveSync);
            Log("fire-and-forget: {0}ms sync vs {1}ms async ", syncFaF, asyncFaF);
            Assert.True(effectiveAsync < effectiveSync, "Everything");
            Assert.True(asyncFaF < syncFaF, "Fire and Forget");
        }

        [Fact]
        public async Task BasicStringGetPerf()
        {
            using (var conn = Create())
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                await db.StringSetAsync(key, "some value").ForAwait();

                // this is just to JIT everything before we try testing
                var syncVal = db.StringGet(key);
                var asyncVal = await db.StringGetAsync(key).ForAwait();

                var syncTimer = Stopwatch.StartNew();
                syncVal = db.StringGet(key);
                syncTimer.Stop();

                var asyncTimer = Stopwatch.StartNew();
                asyncVal = await db.StringGetAsync(key).ForAwait();
                asyncTimer.Stop();

                Log($"Sync: {syncTimer.ElapsedMilliseconds}; Async: {asyncTimer.ElapsedMilliseconds}");
                Assert.Equal("some value", syncVal);
                Assert.Equal("some value", asyncVal);
                // let's allow 20% async overhead
                // But with a floor, since the base can often be zero
                Assert.True(asyncTimer.ElapsedMilliseconds <= System.Math.Max(syncTimer.ElapsedMilliseconds * 1.2M, 50));
            }
        }
    }
}
