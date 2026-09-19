using System.Diagnostics;
using StackExchange.Redis;

namespace CoreBench;

/// <summary>
/// The harness, and every arm that needs nothing but the shipped public API.
/// </summary>
/// <remarks>
/// Linked into <c>CoreBench.Baseline</c> as well as <c>CoreBench</c>, so the 3.3.0 comparison runs the
/// <i>same</i> harness rather than a re-typed one that has quietly drifted. The arms that need internal
/// types live in CoreBench's own Program.
/// </remarks>
internal static class Bench
{
    public static string Host { get; private set; } = "127.0.0.1";

    public static int Port { get; private set; } = 6379;

    public static string Work { get; private set; } = "incr";

    public static double Seconds { get; private set; } = 3;

    public static string Arms { get; private set; } = "all";

    public static int[] WorkerCounts { get; private set; } = [1];

    public static void Configure(string[] args)
    {
        Host = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "127.0.0.1";
        Port = int.TryParse(Environment.GetEnvironmentVariable("REDIS_PORT"), out var p) ? p : 6379;
        Arms = Arg(args, "--arm", "all");
        Work = Arg(args, "--work", "incr");
        Seconds = double.Parse(Arg(args, "--seconds", "3"));
        WorkerCounts = Arg(args, "--workers", "1,2,4,8,16,32")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    }

    public static void WriteHeader(string label)
    {
        Console.WriteLine($"# {label} :: {Host}:{Port}, work={Work}, {Seconds}s per measurement, server GC={System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine();
        Console.WriteLine($"{"arm",-10} {"workers",7} {"ops/s",14} {"bytes/op",10} {"gen0",7} {"gen1",6} {"gen2",6}");
    }

    /// <summary>The shipped path: multiplexer, IDatabase, the message pipeline.</summary>
    public static async Task<long> OldCoreAsync(Context ctx)
    {
        var muxer = await ConnectionMultiplexer.ConnectAsync($"{Host}:{Port}");
        try
        {
            var db = muxer.GetDatabase();
            return await DriveAsync(ctx, async (worker, key) =>
            {
                if (Work == "get") { _ = await db.StringGetAsync(key); }
                else { _ = await db.StringIncrementAsync(key); }
            });
        }
        finally
        {
            await muxer.CloseAsync();
            muxer.Dispose();
        }
    }

    /// <summary>
    /// The harness floor for a SUSPENDING await: the same lambda shape, awaiting something that really
    /// does yield. Measured at 0 for a completed task, so this isolates the caller's state-machine box.
    /// </summary>
    public static Task<long> YieldAsync(Context ctx)
        => DriveAsync(ctx, async (worker, key) => await Task.Yield());

    /// <summary>The harness floor with no suspension at all.</summary>
    public static Task<long> NoopAsync(Context ctx)
        => DriveAsync(ctx, static (worker, key) => Task.CompletedTask);

    public static async Task RunAsync(string arm, int workers, Func<Context, Task<long>> body)
    {
        var ctx = new Context(workers, TimeSpan.FromSeconds(Seconds));

        ctx.Warmup = true;
        await body(ctx);
        ctx.Warmup = false;

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        var (g0, g1, g2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();

        var ops = await body(ctx);

        watch.Stop();
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;

        Console.WriteLine(
            $"{arm,-10} {workers,7} {ops / watch.Elapsed.TotalSeconds,14:N0} {(double)allocated / ops,10:N1} " +
            $"{GC.CollectionCount(0) - g0,7} {GC.CollectionCount(1) - g1,6} {GC.CollectionCount(2) - g2,6}");
    }

    public static async Task<long> DriveAsync(Context ctx, Func<int, RedisKey, Task> step)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(ctx.Duration.TotalSeconds * Stopwatch.Frequency);
        var counts = new long[ctx.Workers];

        // one key per worker: a shared counter would serialise on the SERVER, which is the thing being
        // controlled for - the question here is what the CLIENT does under concurrency
        var tasks = new Task[ctx.Workers];
        for (var i = 0; i < ctx.Workers; i++)
        {
            var worker = i;
            tasks[i] = Task.Run(async () =>
            {
                RedisKey key = $"corebench:{Work}:{worker}";
                long count = 0;
                if (ctx.Warmup)
                {
                    for (var n = 0; n < 200; n++) { await step(worker, key); count++; }
                }
                else
                {
                    while (Stopwatch.GetTimestamp() < deadline) { await step(worker, key); count++; }
                }

                counts[worker] = count;
            });
        }

        await Task.WhenAll(tasks);
        return counts.Sum();
    }

    public static string Arg(string[] args, string name, string fallback)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
}

internal sealed class Context(int workers, TimeSpan duration)
{
    public int Workers { get; } = workers;

    public TimeSpan Duration { get; } = duration;

    public bool Warmup { get; set; }
}
