using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class MassiveOpsTests : TestBase
{
    public MassiveOpsTests(ITestOutputHelper output) : base(output) { }

    [FactLongRunning]
    public async Task LongRunning()
    {
        using var conn = Create();

        var key = Me();
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MassiveBulkOpsAsync(bool withContinuation)
    {
        using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        await db.PingAsync().ForAwait();
        static void nonTrivial(Task _)
        {
            Thread.SpinWait(5);
        }
        var watch = Stopwatch.StartNew();
        for (int i = 0; i <= AsyncOpsQty; i++)
        {
            var t = db.StringSetAsync(key, i);
            if (withContinuation)
            {
                // Intentionally unawaited
                _ = t.ContinueWith(nonTrivial);
            }
        }
        Assert.Equal(AsyncOpsQty, await db.StringGetAsync(key).ForAwait());
        watch.Stop();
        Log("{2}: Time for {0} ops: {1}ms ({3}, any order); ops/s: {4}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
            withContinuation ? "with continuation" : "no continuation", AsyncOpsQty / watch.Elapsed.TotalSeconds);
    }

    [TheoryLongRunning]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(50)]
    public void MassiveBulkOpsSync(int threads)
    {
        using var conn = Create(syncTimeout: 30000);

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        int workPerThread = SyncOpsQty / threads;
        var timeTaken = RunConcurrent(delegate
        {
            for (int i = 0; i < workPerThread; i++)
            {
                db.StringIncrement(key, flags: CommandFlags.FireAndForget);
            }
        }, threads);

        int val = (int)db.StringGet(key);
        Assert.Equal(workPerThread * threads, val);
        Log("{2}: Time for {0} ops on {3} threads: {1}ms (any order); ops/s: {4}",
            threads * workPerThread, timeTaken.TotalMilliseconds, Me(), threads, (workPerThread * threads) / timeTaken.TotalSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void MassiveBulkOpsFireAndForget(int threads)
    {
        using var conn = Create(syncTimeout: 30000);

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.Ping();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        int perThread = AsyncOpsQty / threads;
        var elapsed = RunConcurrent(delegate
        {
            for (int i = 0; i < perThread; i++)
            {
                db.StringIncrement(key, flags: CommandFlags.FireAndForget);
            }
            db.Ping();
        }, threads);
        var val = (long)db.StringGet(key);
        Assert.Equal(perThread * threads, val);

        Log("{2}: Time for {0} ops over {4} threads: {1:###,###}ms (any order); ops/s: {3:###,###,##0}",
            val, elapsed.TotalMilliseconds, Me(),
            val / elapsed.TotalSeconds, threads);
    }
}
