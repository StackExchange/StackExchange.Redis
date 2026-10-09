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
