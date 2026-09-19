using CoreBench;

Bench.Configure(args);
Bench.WriteHeader("StackExchange.Redis 3.3.0 (shipped package)");

foreach (var workers in Bench.WorkerCounts)
{
    if (Bench.Arms is "all" or "old") await Bench.RunAsync("3.3.0", workers, Bench.OldCoreAsync);
    if (Bench.Arms is "yield") await Bench.RunAsync("yield", workers, Bench.YieldAsync);
    if (Bench.Arms is "noop") await Bench.RunAsync("noop", workers, Bench.NoopAsync);
}
