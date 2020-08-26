using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class MassiveOps : TestBase
    {
        public MassiveOps(ITestOutputHelper output) : base(output) { }

        [FactLongRunning]
        public async Task LongRunning()
        {
            var key = Me();
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, "test value", flags: CommandFlags.FireAndForget);
                for (var i = 0; i < 200; i++)
                {
                    var val = await db.StringGetAsync(key).ForAwait();
                    Assert.Equal("test value", val);
                    await Task.Delay(50).ForAwait();
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MassiveBulkOpsAsync(bool withContinuation)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                var conn = muxer.GetDatabase();
                await conn.PingAsync().ForAwait();
                void nonTrivial(Task _)
                {
                    Thread.SpinWait(5);
                }
                var watch = Stopwatch.StartNew();
                for (int i = 0; i <= AsyncOpsQty; i++)
                {
                    var t = conn.StringSetAsync(key, i);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    if (withContinuation) t.ContinueWith(nonTrivial);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                }
                Assert.Equal(AsyncOpsQty, await conn.StringGetAsync(key).ForAwait());
                watch.Stop();
                Log("{2}: Time for {0} ops: {1}ms ({3}, any order); ops/s: {4}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    withContinuation ? "with continuation" : "no continuation", AsyncOpsQty / watch.Elapsed.TotalSeconds);
            }
        }

        [TheoryLongRunning]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(50)]
        public void MassiveBulkOpsSync(int threads)
        {
            int workPerThread = SyncOpsQty / threads;
            using (var muxer = Create(syncTimeout: 30000))
            {
                RedisKey key = Me();
                var conn = muxer.GetDatabase();
                conn.KeyDelete(key, CommandFlags.FireAndForget);
                var timeTaken = RunConcurrent(delegate
                {
                    for (int i = 0; i < workPerThread; i++)
                    {
                        conn.StringIncrement(key, flags: CommandFlags.FireAndForget);
                    }
                }, threads);

                int val = (int)conn.StringGet(key);
                Assert.Equal(workPerThread * threads, val);
                Log("{2}: Time for {0} ops on {3} threads: {1}ms (any order); ops/s: {4}",
                    threads * workPerThread, timeTaken.TotalMilliseconds, Me(), threads, (workPerThread * threads) / timeTaken.TotalSeconds);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        public void MassiveBulkOpsFireAndForget(int threads)
        {
            using (var muxer = Create(syncTimeout: 30000))
            {
                RedisKey key = Me();
                var conn = muxer.GetDatabase();
                conn.Ping();

                conn.KeyDelete(key, CommandFlags.FireAndForget);
                int perThread = AsyncOpsQty / threads;
                var elapsed = RunConcurrent(delegate
                {
                    for (int i = 0; i < perThread; i++)
                    {
                        conn.StringIncrement(key, flags: CommandFlags.FireAndForget);
                    }
                    conn.Ping();
                }, threads);
                var val = (long)conn.StringGet(key);
                Assert.Equal(perThread * threads, val);

                Log("{2}: Time for {0} ops over {4} threads: {1:###,###}ms (any order); ops/s: {3:###,###,##0}",
                    val, elapsed.TotalMilliseconds, Me(),
                    val / elapsed.TotalSeconds, threads);
            }
        }
    }
}
