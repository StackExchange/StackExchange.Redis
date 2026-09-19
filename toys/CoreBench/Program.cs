using System.Net.Sockets;
using CoreBench;
using RESPite.Operations;
using RESPite.Transports;
using StackExchange.Redis;
using StackExchange.Redis.Caching;

Bench.Configure(args);
Bench.WriteHeader("this branch");

foreach (var workers in Bench.WorkerCounts)
{
    if (Bench.Arms is "all" or "old") await Bench.RunAsync("old", workers, Bench.OldCoreAsync);
    if (Bench.Arms is "all" or "new") await Bench.RunAsync("new", workers, ctx => NewCoreAsync(ctx, cache: false));
    if (Bench.Arms is "all" or "newcache") await Bench.RunAsync("newcache", workers, ctx => NewCoreAsync(ctx, cache: true));
    if (Bench.Arms is "exec") await Bench.RunAsync("exec", workers, ExecAsync);
    if (Bench.Arms is "direct") await Bench.RunAsync("direct", workers, DirectAsync);
    if (Bench.Arms is "render") await Bench.RunAsync("render", workers, RenderAsync);
    if (Bench.Arms is "yield") await Bench.RunAsync("yield", workers, Bench.YieldAsync);
    if (Bench.Arms is "noop") await Bench.RunAsync("noop", workers, Bench.NoopAsync);
    Console.WriteLine();
}

// ---- the new core, end to end ---------------------------------------------------------------------

async Task<long> NewCoreAsync(Context ctx, bool cache)
{
    await using var transport = await ConnectAsync();
    var connection = new RespConnection(transport);

    using var clientCache = cache ? new RespClientCache() : null;
    var raw = new RespContext().WithExecutor(new RespConnectionExecutor(connection, 0));
    if (clientCache is not null) raw = raw.WithCache(clientCache);
    var context = new RespDatabaseContext(raw);

    // the cache arm measures a cacheable GET, per the request; INCR is not cacheable and never would be
    var flags = cache ? CommandFlags.CommandRetryReadOnly : CommandFlags.None;

    return await Bench.DriveAsync(ctx, async (worker, key) =>
    {
        if (Bench.Work == "get" || cache) { _ = await context.Strings.GetAsync(key, flags); }
        else { _ = await context.Strings.IncrementAsync(key); }
    });
}

// ---- subtractive probes ----------------------------------------------------------------------------

// the executor alone: request rendered up front, reply released by hand. Everything the full arm adds
// above this - handler, parse, the typed ValueTask - is removed.
async Task<long> ExecAsync(Context ctx)
{
    await using var transport = await ConnectAsync();
    var executor = new RespConnectionExecutor(new RespConnection(transport), 0);
    var raw = new RespContext();

    return await Bench.DriveAsync(ctx, async (worker, key) =>
    {
        // Detach moves ownership out of the frame, so the REQUEST is what needs disposing: the executor
        // copies the bytes and never releases the caller's reference
        using var request = raw.Render($"{RedisCommand.INCR}{key}").Detach();
        using var payload = await executor.SendAsync(request);
    });
}

// an operation parsing straight to long, sent to the connection directly - no RespPayload, so no copy of
// the reply and no payload object. The gap from `exec` is what that copy costs. The operation is pooled
// by hand here, so this measures the library rather than the probe.
async Task<long> DirectAsync(Context ctx)
{
    await using var transport = await ConnectAsync();
    var connection = new RespConnection(transport);
    var raw = new RespContext();
    var pool = new Int64Message?[128];

    return await Bench.DriveAsync(ctx, async (worker, key) =>
    {
        using var request = raw.Render($"{RedisCommand.INCR}{key}").Detach();
        var op = Rent(pool);
        op.Attach(request.Span);
        connection.Send(op);
        try
        {
            _ = await new ValueTask<long>(op, op.Token);
        }
        finally
        {
            Return(pool, op);
        }
    });

    static Int64Message Rent(Int64Message?[] pool)
    {
        for (var i = 0; i < pool.Length; i++)
        {
            if (Interlocked.Exchange(ref pool[i], null) is { } reused) return reused;
        }

        return new Int64Message();
    }

    static void Return(Int64Message?[] pool, Int64Message op)
    {
        for (var i = 0; i < pool.Length; i++)
        {
            if (Interlocked.CompareExchange(ref pool[i], op, null) is null) return;
        }
    }
}

// render and release, no IO at all: what the interpolated writer costs on its own
Task<long> RenderAsync(Context ctx)
{
    var raw = new RespContext();
    return Bench.DriveAsync(ctx, (worker, key) =>
    {
        using var request = raw.Render($"{RedisCommand.INCR}{key}").Detach();
        return Task.CompletedTask;
    });
}

static async Task<StreamDuplexTransport> ConnectAsync()
{
    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    await socket.ConnectAsync(Bench.Host, Bench.Port);
    return new StreamDuplexTransport(new NetworkStream(socket, ownsSocket: true));
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
