using System.Diagnostics;
using StackExchange.Redis;

#if !RELEASE
Console.WriteLine("Warning: not running in release mode; results may be sub-optimal.");
#endif
using Benchmarker bench = new()
{
    // This is the operation (or operations) that we're benchmarking; you can change this to whatever you want to benchmark;
    // it should be representative of your real workload, in a database that is in a representative state (e.g. has the right indexes,
    // data volume, etc).
    Work = db => db.PingAsync(),
    // optional: set Connections, PipelineDepth, etc
    Clients = 100,
    PipelineDepth = 20, // not suitable for all workloads, note! for pure query workloads this might need to be 1
};
var count = bench.RepeatCount;

Console.WriteLine($"Running {count} times, {bench.Clients} clients, {bench.IterationsPerClient} iterations each, pipeline depth {bench.PipelineDepth}");
for (int i = 0; i < count; i++)
{
    var watch = Stopwatch.StartNew();
    await bench.RunAsync();
    watch.Stop();
    var opsPerSecond = (bench.IterationsPerClient * bench.Clients) / watch.Elapsed.TotalSeconds;
    Console.WriteLine($"Run {i + 1} of {count} completed in {watch.ElapsedMilliseconds}ms, {opsPerSecond:N0} ops/s");
}

internal sealed class Benchmarker : IDisposable
{
    public int Connections { get; set; } = 1;
    public int IterationsPerClient { get; set; } = 10_000;
    public int PipelineDepth { get; set; } = 1;
    public int RepeatCount { get; set; } = 10;
    private int? _clients;
    public int Clients
    {
        get => _clients ?? Connections;
        set => _clients = value;
    }

    public required Func<IDatabase, Task> Work { get; set; }

    // optional run-once setup
    public Func<IDatabase, Task>? Init { get; set; }

    private IDatabase[] _conns = [];

    void IDisposable.Dispose()
    {
        foreach (var db in _conns)
        {
            db?.Multiplexer?.Dispose();
        }
    }

    private async Task ConnectAllAsync()
    {
        Console.WriteLine($"Connecting...");
        var arr = new IDatabase[Connections];
        for (int i = 0; i < arr.Length; i++)
        {
            var db = arr[i] = await ConnectAsync();
            var id = await db.ExecuteAsync("client", "id");
            Console.WriteLine($"Client {i} connected, id: {id}");
        }
        _conns = arr;
        if (Init is not null)
        {
            Console.WriteLine("Initializing...");
            await Init(_conns[0]);
            Console.WriteLine("Initialized");
        }
    }
    public async Task RunAsync()
    {
        // spin up the connections, if not already done
        if (_conns.Length is 0) await ConnectAllAsync();

        Task[] clients = new Task[Clients];
        for (int i = 0; i < clients.Length; i++)
        {
            // round-robin the connections to clients
            var db = _conns[i % _conns.Length]; // explicit local to avoid capture-context problems
            clients[i] = Task.Run(() => RunClientAsync(db)); // intentionally not awaited - concurrency
        }
        await Task.WhenAll(clients);
    }

    private async Task RunClientAsync(IDatabase db)
    {
        // run DoWorkAsync in a loop using a pipeline of depth PipelineDepth - just track the
        // last outstanding operation so we can await it when the pipe is full, or at the end
        int count = IterationsPerClient, maxDepth = PipelineDepth, depth = 0;
        var work = Work;
        Task? last = null;
        for (int i = 0; i < count; i++)
        {
            var pending = work(db); // intentionally not awaited - pipeline
            if (last is not null)
            {
                _ = last.ContinueWith(static t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            }

            if (++depth >= maxDepth)
            {
                // pipeline is full; pause until the last one completes
                await pending;
                last = null;
            }
            else
            {
                // leave it outstanding
                last = pending;
            }
        }

        if (last is not null)
        {
            await last;
        }
    }

    private async Task<IDatabase> ConnectAsync()
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { { "127.0.0.1", 6379 } },
            TieBreaker = "",
            AllowAdmin = true,
            Protocol = RedisProtocol.Resp3,
            // turn off pub-sub
            CommandMap = CommandMap.Create(new HashSet<string>() { "SUBSCRIBE" }, available: false),
            ConfigurationChannel = "",
        };
        var muxer = await ConnectionMultiplexer.ConnectAsync(options);
        return muxer.GetDatabase();
    }
}
