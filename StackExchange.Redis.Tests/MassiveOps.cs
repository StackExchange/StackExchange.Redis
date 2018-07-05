using System;
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MassiveBulkOpsAsync(bool withContinuation)
        {
            using (var muxer = Create())
            {
                RedisKey key = "MBOA";
                var conn = muxer.GetDatabase();
                await conn.PingAsync().ForAwait();
                Action<Task> nonTrivial = delegate
                {
                    Thread.SpinWait(5);
                };
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
                Output.WriteLine("{2}: Time for {0} ops: {1}ms ({3}, {4}); ops/s: {5}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    withContinuation ? "with continuation" : "no continuation", "any order",
                    AsyncOpsQty / watch.Elapsed.TotalSeconds);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(50)]
        public void MassiveBulkOpsSync(int threads)
        {
            int workPerThread = SyncOpsQty / threads;
            using (var muxer = Create(syncTimeout: 30000))
            {
                RedisKey key = "MBOS";
                var conn = muxer.GetDatabase();
                conn.KeyDelete(key);
#if DEBUG
                long oldAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
#endif
                var timeTaken = RunConcurrent(delegate
                {
                    for (int i = 0; i < workPerThread; i++)
                    {
                        conn.StringIncrement(key);
                    }
                }, threads);

                int val = (int)conn.StringGet(key);
                Assert.Equal(workPerThread * threads, val);
                Output.WriteLine("{2}: Time for {0} ops on {4} threads: {1}ms ({3}); ops/s: {5}",
                    threads * workPerThread, timeTaken.TotalMilliseconds, Me()
                    , "any order", threads, (workPerThread * threads) / timeTaken.TotalSeconds);
#if DEBUG
                long newAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
                Output.WriteLine("ResultBox allocations: {0}", newAlloc - oldAlloc);
                Assert.True(newAlloc - oldAlloc <= 2 * threads, "number of box allocations");
#endif
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        public void MassiveBulkOpsFireAndForget(int threads)
        {
            using (var muxer = Create(syncTimeout: 30000))
            {
#if DEBUG
                long oldAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
#endif
                RedisKey key = "MBOF";
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

                Output.WriteLine("{2}: Time for {0} ops over {5} threads: {1:###,###}ms ({3}); ops/s: {4:###,###,##0}",
                    val, elapsed.TotalMilliseconds, Me(),
                    "any order",
                    val / elapsed.TotalSeconds, threads);
#if DEBUG
                long newAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
                Output.WriteLine("ResultBox allocations: {0}",
                    newAlloc - oldAlloc);
                Assert.True(newAlloc - oldAlloc <= 4);
#endif
            }
        }
    }
}
