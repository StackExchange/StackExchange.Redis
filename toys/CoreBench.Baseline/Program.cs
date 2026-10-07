using CoreBench;

Bench.Configure(args);
// the central package version, whatever it currently is - so read it rather than restate it
var shipped = typeof(StackExchange.Redis.ConnectionMultiplexer).Assembly
    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
    .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "shipped";
Bench.WriteHeader($"StackExchange.Redis {shipped} (shipped package)");

foreach (var workers in Bench.WorkerCounts)
{
    if (Bench.Arms is "all" or "old") await Bench.RunAsync(shipped, workers, Bench.OldCoreAsync);
    if (Bench.Arms is "yield") await Bench.RunAsync("yield", workers, Bench.YieldAsync);
    if (Bench.Arms is "noop") await Bench.RunAsync("noop", workers, Bench.NoopAsync);
}
