using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis.Benchmarks;

/// <summary>
/// How the cache-hit path scales with threads, and with how many distinct keys those threads share.
/// </summary>
/// <remarks>
/// <para>
/// Not a BenchmarkDotNet class: BDN measures one thread, and the question here is what happens across many.
/// Run with <c>dotnet run -c Release -f net10.0 -- cache-scaling [seconds]</c>.
/// </para>
/// <para>
/// <b>Why.</b> <see cref="CacheHitSendBenchmarks"/> puts a single-threaded hit at ~136ns, about 7.3M/s per core; the
/// RespFest league reports 30M/s across a 12-core / 24-thread machine, about 2.5M/s per core. Either the league's
/// per-op path costs more than the micro-benchmark's, or something stops it scaling. One write sits on the hit
/// path - the reply's reference count, taken and dropped on every hit - and on a hot key every thread writes the
/// same cache line. If that is the limit, one hot key collapses as threads are added while 100k keys scale.
/// </para>
/// <para>
/// The <c>refcount</c> rows isolate that one suspect: retain + release in a loop on one shared payload versus one
/// payload per thread, with nothing else in the way. Confirm with <c>perf c2c</c> over the 1-key row.
/// </para>
/// </remarks>
internal static class CacheHitScaling
{
    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private sealed class OneReplyExecutor : RespExecutorBase
    {
        private readonly byte[] _reply = Encoding.UTF8.GetBytes("$5\r\nhello\r\n");

        public override int Database => 0;

        public override RespPayload Send(in RespRequest request) => RespPayload.Create(_reply);

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static readonly int[] ThreadCounts = [1, 2, 6, 12, 24];
    private static readonly int[] KeyCounts = [1, 1_000, 100_000];

    /// <param name="args"><c>cache-scaling [seconds] [host:port]</c>; with an endpoint, the real-multiplexer rows run too.</param>
    public static void Run(string[] args)
    {
        if (args.Length > 1 && args[1] == "miss")
        {
            RunMisses(args);
            return;
        }

        var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 3;
        var server = args.Length > 2 ? args[2] : null;
        Console.WriteLine($"cache-hit scaling; {Environment.ProcessorCount} logical processors, {seconds}s per row after 1s warm-up");
        Console.WriteLine();
        Console.WriteLine($"{"mode",-12} {"keys",8} {"threads",8} {"total M/s",10} {"per thread M/s",15} {"ns/op/thread",13}");

        foreach (var keys in KeyCounts)
        {
            foreach (var threads in ThreadCounts)
            {
                Report("full", keys, threads, Measure(threads, seconds, FullPath(keys)));
            }
        }

        // the same hit through a REAL multiplexer, as RespFest drives it: the stub executor above skips the
        // multiplexer executor entirely, so it cannot see anything that path costs - including the routing a
        // capability query once did on every cached read
        if (server is not null)
        {
            foreach (var keys in KeyCounts)
            {
                foreach (var threads in ThreadCounts)
                {
                    using var muxer = Multiplexer(server);
                    Report("muxer", keys, threads, Measure(threads, seconds, MultiplexerPath(muxer, keys)));
                }
            }
        }

        foreach (var threads in ThreadCounts)
        {
            Report("refcount", 1, threads, Measure(threads, seconds, SharedRefCount()));
        }

        foreach (var threads in ThreadCounts)
        {
            Report("refcount", threads, threads, Measure(threads, seconds, PerThreadRefCount(threads)));
        }
    }

    private static void Report(string mode, int keys, int threads, long opsPerSecond)
    {
        var perThread = (double)opsPerSecond / threads;
        Console.WriteLine($"{mode,-12} {keys,8:N0} {threads,8} {opsPerSecond / 1e6,10:N2} {perThread / 1e6,15:N2} {1e9 / perThread,13:N1}");
    }

    /// <summary>The whole group-method hit, each thread walking the key set from its own offset.</summary>
    private static Func<int, Action<long>> FullPath(int keyCount)
    {
        var cache = new RespClientCache();
        var context = new RespDatabaseContext(new RespContext().WithExecutor(new OneReplyExecutor()).WithCache(cache));
        var keys = Enumerable.Range(0, keyCount).Select(i => (RedisKey)$"key:{i:D8}").ToArray();
        foreach (var key in keys) _ = context.Strings.GetAsync(key, Readable).GetAwaiter().GetResult(); // prime: every op a hit

        return thread => iterations =>
        {
            var index = (int)((long)thread * keys.Length / ThreadCounts.Max()); // spread the starting points
            for (long i = 0; i < iterations; i++)
            {
                _ = context.Strings.GetAsync(keys[index], Readable).GetAwaiter().GetResult();
                if (++index == keys.Length) index = 0;
            }
        };
    }

    /// <summary>
    /// RespFest's <c>cache-miss-conc64</c>, as near as a loop gets: GET 1 KiB over a keyspace 20x a 32 MiB cache, so
    /// every read misses, fills and evicts. Reports throughput with the GC's view of it - collections per generation
    /// and bytes allocated per op - because what a fill keeps, and for how long, is the question.
    /// </summary>
    /// <remarks><c>cache-scaling miss [seconds] [host:port] [nocache|admit]</c>.</remarks>
    private static void RunMisses(string[] args)
    {
        var seconds = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 5;
        var server = args.Length > 3 ? args[3] : "127.0.0.1:6379";
        var noCache = args.Length > 4 && args[4] == "nocache"; // the baseline: what a miss costs with nothing to fill
        var admitOnRepeat = args.Length > 4 && args[4] == "admit"; // CacheAdmission.OnRepeatedMiss
        const int KeyCount = 200_000;

        var config = ConfigurationOptions.Parse(server);
        config.Protocol = RedisProtocol.Resp3;
        if (!noCache)
        {
            config.ClientCache = new CacheOptions
            {
                MaxBytes = 32L * 1024 * 1024,
                DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromHours(1) },
                Admission = admitOnRepeat ? CacheAdmission.OnRepeatedMiss : CacheAdmission.OnFirstMiss,
            };
        }
        using var muxer = ConnectionMultiplexer.Connect(config);
        var context = muxer.GetDatabaseContext();
        var keys = Enumerable.Range(0, KeyCount).Select(i => (RedisKey)$"cache-scaling-miss:{i:D8}").ToArray();
        var value = (RedisValue)new byte[1024];
        foreach (var key in keys) Wait(context.Strings.SetAsync(key, value));

        Console.WriteLine($"cache-miss: GET 1 KiB, {KeyCount:N0} keys, 32 MiB cache; async callers, {seconds}s per row after 2s warm-up");
        Console.WriteLine($"{"callers",8} {"total k/s",10} {"cpu us/op",10} {"gen0",6} {"gen1",6} {"gen2",6} {"alloc B/op",11} {"pause %",8}");
        foreach (var callers in new[] { 1, 64 })
        {
            // RespFest's shape: N async callers, each awaiting its own GET in a loop; every op counted, not batches
            long ops = 0;
            var measuring = 0;
            var stop = 0;
            // each caller walks a share of the keyspace of its own, as RespFest does (200,000 / 64 = 3,125 each): with
            // shared walks the callers trail one another through the same keys, and all but the first HIT - which
            // measured a ~95% hit rate, not the miss path
            var share = KeyCount / callers;
            async Task Caller(int caller)
            {
                var first = caller * share;
                var step = 0;
                while (Volatile.Read(ref stop) == 0)
                {
                    _ = await context.Strings.GetAsync(keys[first + step]).ConfigureAwait(false);
                    if (++step == share) step = 0;
                    if (Volatile.Read(ref measuring) == 1) Interlocked.Increment(ref ops);
                }
            }

            var running = Enumerable.Range(0, callers).Select(c => Task.Run(() => Caller(c))).ToArray();
            Thread.Sleep(2000);

            var g0 = GC.CollectionCount(0);
            var g1 = GC.CollectionCount(1);
            var g2 = GC.CollectionCount(2);
            var allocated = AllocatedBytes();
            var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            var paused = PauseTime();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Volatile.Write(ref measuring, 1);
            Thread.Sleep(seconds * 1000);
            Volatile.Write(ref measuring, 0);
            var elapsed = watch.Elapsed;
            var cpuUsed = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu;
            var allocatedNow = AllocatedBytes();
            var pausedNow = PauseTime();
            var (d0, d1, d2) = (GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2);
            Volatile.Write(ref stop, 1);
            Task.WaitAll(running);

            var done = (double)Math.Max(1, Volatile.Read(ref ops));
            Console.WriteLine($"{callers,8} {done / elapsed.TotalSeconds / 1e3,10:N1} {cpuUsed.TotalMilliseconds * 1000 / done,10:N2} {d0,6} {d1,6} {d2,6} {(allocatedNow - allocated) / done,11:N0} {(pausedNow - paused).TotalMilliseconds * 100 / elapsed.TotalMilliseconds,8:N1}");
        }

        Console.WriteLine($"cache: {muxer.ClientCache?.Count:N0} entries, {muxer.ClientCache?.Bytes:N0} bytes, {muxer.ClientCache?.Stored:N0} stored, {muxer.ClientCache?.RedundantFills:N0} redundant, {muxer.ClientCache?.RefusedNotAdmitted:N0} not admitted");
    }

    /// <summary>Time the GC has paused the process so far; zero on .NET Framework, which cannot say.</summary>
#if NET
    private static TimeSpan PauseTime() => GC.GetTotalPauseDuration();
#else
    private static TimeSpan PauseTime() => TimeSpan.Zero;
#endif

    /// <summary>Bytes allocated so far by this process; zero on .NET Framework, which cannot say.</summary>
#if NET
    private static long AllocatedBytes() => GC.GetTotalAllocatedBytes(precise: false);
#else
    private static long AllocatedBytes() => 0;
#endif

    private static ConnectionMultiplexer Multiplexer(string server)
    {
        var config = ConfigurationOptions.Parse(server);
        config.Protocol = RedisProtocol.Resp3; // invalidations are pushes; the cache refuses RESP2
        config.ClientCache = new CacheOptions { DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromHours(1) } };
        return ConnectionMultiplexer.Connect(config);
    }

    /// <summary>The whole hit as RespFest takes it: <c>GetDatabaseContext().Strings.GetAsync</c> over a connected multiplexer.</summary>
    private static Func<int, Action<long>> MultiplexerPath(ConnectionMultiplexer muxer, int keyCount)
    {
        var context = muxer.GetDatabaseContext();
        var keys = Enumerable.Range(0, keyCount).Select(i => (RedisKey)$"cache-scaling:{i:D8}").ToArray();
        foreach (var key in keys) Wait(context.Strings.SetAsync(key, "hello"));
        foreach (var key in keys) Wait(context.Strings.GetAsync(key)); // prime: every op a hit

        return thread => iterations =>
        {
            var index = (int)((long)thread * keys.Length / ThreadCounts.Max());
            for (long i = 0; i < iterations; i++)
            {
                Wait(context.Strings.GetAsync(keys[index]));
                if (++index == keys.Length) index = 0;
            }
        };
    }

    /// <summary>
    /// Wait for a <see cref="ValueTask{TResult}"/>: a hit has already completed, but anything that went to the server
    /// is a pooled operation, whose result may not be read before it completes - so go through a task for those.
    /// </summary>
    private static T Wait<T>(ValueTask<T> pending) => pending.IsCompleted ? pending.Result : pending.AsTask().GetAwaiter().GetResult();

    private static Func<int, Action<long>> SharedRefCount()
    {
        var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"));
        return _ => iterations => RetainRelease(payload, iterations);
    }

    private static Func<int, Action<long>> PerThreadRefCount(int threads)
    {
        var payloads = Enumerable.Range(0, threads).Select(_ => RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"))).ToArray();
        return thread => iterations => RetainRelease(payloads[thread], iterations);
    }

    private static void RetainRelease(RespPayload payload, long iterations)
    {
        for (long i = 0; i < iterations; i++)
        {
            if (payload.TryRetain()) payload.Release();
        }
    }

    /// <summary>Run <paramref name="threads"/> workers in fixed-size batches until time is up; ops per second, all threads.</summary>
    private static long Measure(int threads, int seconds, Func<int, Action<long>> workerFactory)
    {
        const long Batch = 10_000;
        var workers = Enumerable.Range(0, threads).Select(workerFactory).ToArray();
        var counts = new long[threads * 16]; // padded: one cache line per thread, so counting is not itself contended
        var phase = 0; // 0 = warm-up, 1 = measuring, 2 = stop
        using var start = new Barrier(threads + 1);

        var running = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            var work = workers[t];
            start.SignalAndWait();
            while (true)
            {
                var now = Volatile.Read(ref phase);
                if (now == 2) break;
                work(Batch);
                if (now == 1) counts[t * 16] += Batch;
            }
        }) { IsBackground = true }).ToArray();

        foreach (var thread in running) thread.Start();
        start.SignalAndWait();
        Thread.Sleep(1000);
        var watch = Stopwatch.StartNew();
        Volatile.Write(ref phase, 1);
        Thread.Sleep(seconds * 1000);
        Volatile.Write(ref phase, 2);
        watch.Stop();
        foreach (var thread in running) thread.Join();

        long total = 0;
        for (int t = 0; t < threads; t++) total += counts[t * 16];
        return (long)(total / watch.Elapsed.TotalSeconds);
    }
}
