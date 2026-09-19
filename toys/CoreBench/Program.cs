using System.Diagnostics;
using System.Net.Sockets;
using RESPite.Operations;
using RESPite.Transports;
using StackExchange.Redis;
using StackExchange.Redis.Caching;

var host = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("REDIS_PORT"), out var p) ? p : 6379;

var arms = Arg(args, "--arm", "all");
var work = Arg(args, "--work", "incr");
var seconds = double.Parse(Arg(args, "--seconds", "3"));
var workerCounts = Arg(args, "--workers", "1,2,4,8,16,32")
    .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();

Console.WriteLine($"# {host}:{port}, work={work}, {seconds}s per measurement, server GC={System.Runtime.GCSettings.IsServerGC}");
Console.WriteLine();
Console.WriteLine($"{"arm",-10} {"workers",7} {"ops/s",14} {"bytes/op",10} {"gen0",7} {"gen1",6} {"gen2",6}");

foreach (var workers in workerCounts)
{
    if (arms is "all" or "old") await RunAsync("old", workers, OldCoreAsync);
    if (arms is "all" or "new") await RunAsync("new", workers, ctx => NewCoreAsync(ctx, cache: false));
    if (arms is "all" or "newcache") await RunAsync("newcache", workers, ctx => NewCoreAsync(ctx, cache: true));
    if (arms is "all" or "exec") await RunAsync("exec", workers, LayerAsync);
    if (arms is "render") await RunAsync("render", workers, RenderAsync);
    if (arms is "direct") await RunAsync("direct", workers, DirectAsync);
    if (arms is "noop") await RunAsync("noop", workers, NoopAsync);
    Console.WriteLine();
}

// ---- arms ---------------------------------------------------------------------------------------

async Task<long> OldCoreAsync(Context ctx)
{
    var muxer = await ConnectionMultiplexer.ConnectAsync($"{host}:{port}");
    try
    {
        var db = muxer.GetDatabase();
        return await DriveAsync(ctx, async (worker, key) =>
        {
            if (work == "get") { _ = await db.StringGetAsync(key); }
            else { _ = await db.StringIncrementAsync(key); }
        });
    }
    finally
    {
        await muxer.CloseAsync();
        muxer.Dispose();
    }
}

async Task<long> NewCoreAsync(Context ctx, bool cache)
{
    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    await socket.ConnectAsync(host, port);
    await using var transport = new StreamDuplexTransport(new NetworkStream(socket, ownsSocket: true));
    var connection = new RespConnection(transport);

    using var clientCache = cache ? new RespClientCache() : null;
    var raw = new RespContext().WithExecutor(new RespConnectionExecutor(connection, 0));
    if (clientCache is not null) raw = raw.WithCache(clientCache);
    var context = new RespDatabaseContext(raw);

    // the cache arm measures a cacheable GET, per the request; INCR is not cacheable and never would be
    var flags = cache ? CommandFlags.CommandRetryReadOnly : CommandFlags.None;

    return await DriveAsync(ctx, async (worker, key) =>
    {
        if (work == "get" || cache) { _ = await context.Strings.GetAsync(key, flags); }
        else { _ = await context.Strings.IncrementAsync(key); }
    });
}

// Subtractive probe: the executor alone, with the request rendered once up front and the reply
// released by hand. Everything the full arm does above this - handler, parse, ValueTask - is removed,
// so the difference is that layer's cost.
async Task<long> LayerAsync(Context ctx)
{
    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    await socket.ConnectAsync(host, port);
    await using var transport = new StreamDuplexTransport(new NetworkStream(socket, ownsSocket: true));
    var connection = new RespConnection(transport);
    var executor = new RespConnectionExecutor(connection, 0);
    var raw = new RespContext();

    return await DriveAsync(ctx, async (worker, key) =>
    {
        // Detach transfers ownership out of the frame, so the REQUEST is what needs disposing - the
        // executor copies the bytes and never releases the caller's reference. Missing this leaks a
        // pooled buffer per operation, and the pool then allocates a fresh one every time.
        using var request = raw.Render($"{RedisCommand.INCR}{key}").Detach();
        using var payload = await executor.SendAsync(request);
    });
}

// Subtractive probe: render and release, no IO at all. Isolates what the interpolated writer costs
// from what the connection costs.
async Task<long> RenderAsync(Context ctx)
{
    var raw = new RespContext();
    return await DriveAsync(ctx, (worker, key) =>
    {
        using var request = raw.Render($"{RedisCommand.INCR}{key}").Detach();
        return Task.CompletedTask;
    });
}

// Subtractive probe: an operation that parses straight to long, sent to the connection directly. No
// RespPayload, so no copy of the reply and no payload object - which is the layer RespPayload.Create
// itself documents as scaffolding ("in the real thing this is where the response frame's own lease
// would be shared instead"). The gap between this and `exec` is what that copy costs.
async Task<long> DirectAsync(Context ctx)
{
    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    await socket.ConnectAsync(host, port);
    await using var transport = new StreamDuplexTransport(new NetworkStream(socket, ownsSocket: true));
    var connection = new RespConnection(transport);
    var raw = new RespContext();

    return await DriveAsync(ctx, async (worker, key) =>
    {
        using var request = raw.Render($"{RedisCommand.INCR}{key}").Detach();
        var op = new Int64Message();
        op.Attach(request.Span);
        connection.Send(op);
        _ = await new ValueTask<long>(op, op.Token);
    });
}

// The harness floor: an async lambda awaiting nothing. Every arm above pays this - a state machine box
// and a Task per operation - so it has to be subtracted before any absolute number means anything.
async Task<long> NoopAsync(Context ctx)
    => await DriveAsync(ctx, async (worker, key) => await Task.CompletedTask);

// ---- harness ------------------------------------------------------------------------------------

async Task RunAsync(string arm, int workers, Func<Context, Task<long>> body)
{
    var ctx = new Context(workers, TimeSpan.FromSeconds(seconds));

    // warm up: JIT, connection, and for the cache arm, one fill per key so the steady state is hits
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

async Task<long> DriveAsync(Context ctx, Func<int, RedisKey, Task> step)
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
            RedisKey key = $"corebench:{work}:{worker}";
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

static string Arg(string[] args, string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

/// <summary>An operation that reads an integer reply, with no payload object in between.</summary>
internal sealed class Int64Message : RespMessageBase<long>
{
    protected override long Parse(ref RESPite.Messages.RespReader reader) => reader.ReadInt64();

    internal void Attach(ReadOnlySpan<byte> request)
    {
        var pool = System.Buffers.ArrayPool<byte>.Shared;
        var buffer = pool.Rent(request.Length);
        request.CopyTo(buffer);
        SetRequest(new ReadOnlyMemory<byte>(buffer, 0, request.Length), pool, default);
    }
}

internal sealed class Context(int workers, TimeSpan duration)
{
    public int Workers { get; } = workers;

    public TimeSpan Duration { get; } = duration;

    public bool Warmup { get; set; }
}
