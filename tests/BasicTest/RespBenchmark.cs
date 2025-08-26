using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Resp;
using Void = Resp.Void;

namespace BasicTest;

// influenced by redis-benchmark, see .md file
public partial class RespBenchmark : IDisposable
{
    private readonly RespConnectionPool _connectionPool;

    private readonly RespContext[] _clients;
    public int ClientCount => _clients.Length;

    private readonly int _operationsPerClient;

    public const bool DefaultMultiplexed = false, DefaultCancel = false;
    public const string DefaultTests = "";
    private readonly byte[] _payload;
    private readonly (string Key, byte[] Value)[] _pairs;

    private const string
        _getSetKey = "key:__rand_int__",
        _counterKey = "counter:__rand_int__",
        _listKey = "mylist",
        _setKey = "myset",
        _hashKey = "myhash",
        _sortedSetKey = "myzset",
        _streamKey = "mystream";

    private readonly HashSet<string> _tests = new(StringComparer.OrdinalIgnoreCase);

    public int Port { get; } = 6379;
    public int PipelineDepth { get; } = 1;
    public bool Multiplexed { get; }
    public bool SupportCancel { get; }
    public bool Loop { get; }
    public bool Quiet { get; }

    public RespBenchmark(string[] args)
    {
        int operations = 100_000;
        int clients = 50;
        string tests = "";
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-p" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                    Port = tmp;
                    break;
                case "-c" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                    clients = tmp;
                    break;
                case "-n" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                    operations = tmp;
                    break;
                case "-P" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                    PipelineDepth = tmp;
                    break;
                case "+m":
                    Multiplexed = true;
                    break;
                case "-m":
                    Multiplexed = false;
                    break;
                case "+x":
                    SupportCancel = true;
                    break;
                case "-c":
                    SupportCancel = false;
                    break;
                case "-l":
                    Loop = true;
                    break;
                case "-q":
                    Quiet = true;
                    break;
                case "-t" when i != args.Length - 1:
                    tests = args[++i];
                    break;
            }
        }

        _payload = "abc"u8.ToArray();
        _clients = new RespContext[clients];
        _operationsPerClient = operations / ClientCount;
        _connectionPool = new(count: Multiplexed ? 1 : clients);
        _pairs = new (string, byte[])[10];
        if (!string.IsNullOrWhiteSpace(tests))
        {
            foreach (var test in tests.Split(','))
            {
                var t = test.Trim();
                if (!string.IsNullOrWhiteSpace(t)) _tests.Add(t);
            }
        }

        for (var i = 0; i < 10; i++)
        {
            _pairs[i] = ($"{"key:__rand_int__"}{i}", _payload);
        }

        if (Multiplexed)
        {
            var conn = _connectionPool.GetConnection().ForPipeline();
            var ctx = new RespContext(conn);
            for (int i = 0; i < clients; i++) // init all
            {
                _clients[i] = ctx;
            }
        }
        else
        {
            for (int i = 0; i < clients; i++) // init all
            {
                var conn = _connectionPool.GetConnection();
                if (PipelineDepth > 1)
                {
                    conn = conn.ForPipeline();
                }

                _clients[i] = new(conn);
            }
        }
    }

    public void Dispose()
    {
        _connectionPool.Dispose();
        foreach (var client in _clients)
        {
            client.Connection.Dispose();
        }
    }

    public async Task RunAll()
    {
        do
        {
            await InitAsync().ConfigureAwait(false);
            // await RunAsync(PingInline).ConfigureAwait(false);
            await RunAsync(null, PingBulk).ConfigureAwait(false);

            await RunAsync(_getSetKey, Set).ConfigureAwait(false);
            await RunAsync(_getSetKey, Get, GetInit).ConfigureAwait(false);
            await RunAsync(_counterKey, Incr).ConfigureAwait(false);
            await RunAsync(_listKey, LPush).ConfigureAwait(false);
            await RunAsync(_listKey, RPush).ConfigureAwait(false);
            await RunAsync(_listKey, LPop, LPopInit).ConfigureAwait(false);
            await RunAsync(_listKey, RPop, LPopInit).ConfigureAwait(false);
            await RunAsync(_setKey, SAdd).ConfigureAwait(false);
            await RunAsync(_hashKey, HSet).ConfigureAwait(false);
            await RunAsync(_setKey, SPop, SPopInit).ConfigureAwait(false);
            await RunAsync(_sortedSetKey, ZAdd).ConfigureAwait(false);
            await RunAsync(_sortedSetKey, ZPopMin, ZPopMinInit).ConfigureAwait(false);
            await RunAsync(null, MSet).ConfigureAwait(false);
            await RunAsync(_streamKey, XAdd).ConfigureAwait(false);

            // leave until last, they're slower
            await RunAsync(_listKey, LRange100, LRangeInit).ConfigureAwait(false);
            await RunAsync(_listKey, LRange300, LRangeInit).ConfigureAwait(false);
            await RunAsync(_listKey, LRange500, LRangeInit).ConfigureAwait(false);
            await RunAsync(_listKey, LRange600, LRangeInit).ConfigureAwait(false);

            await CleanupAsync().ConfigureAwait(false);
        }
        while (Loop);
    }

    private async Task<Void> Pipeline(Func<ValueTask> operation)
    {
        if (PipelineDepth == 1)
        {
            for (var i = 0; i < _operationsPerClient; i++)
            {
                await operation().ConfigureAwait(false);
            }
        }
        else
        {
            var queue = new Queue<ValueTask>(_operationsPerClient);
            for (var i = 0; i < _operationsPerClient; i++)
            {
                if (queue.Count == _operationsPerClient)
                {
                    await queue.Dequeue().ConfigureAwait(false);
                }

                queue.Enqueue(operation());
            }

            while (queue.Count > 0)
            {
                await queue.Dequeue().ConfigureAwait(false);
            }
        }

        return Void.Instance;
    }

    private async Task<T> Pipeline<T>(Func<ValueTask<T>> operation)
    {
        int i = 0;
        try
        {
            T result = default;
            if (PipelineDepth == 1)
            {
                for (; i < _operationsPerClient; i++)
                {
                    result = await operation().ConfigureAwait(false);
                }
            }
            else
            {
                var queue = new Queue<ValueTask<T>>(_operationsPerClient);
                for (; i < _operationsPerClient; i++)
                {
                    if (queue.Count == _operationsPerClient)
                    {
                        _ = await queue.Dequeue().ConfigureAwait(false);
                    }

                    queue.Enqueue(operation());
                }

                while (queue.Count > 0)
                {
                    result = await queue.Dequeue().ConfigureAwait(false);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{operation.Method.Name} failed after {i} operations: {ex.Message}",
                ex);
        }
    }

    [DisplayName("PING_INLINE")]
    private Task<ResponseSummary> PingInline(RespContext ctx) => Pipeline(() => ctx.PingInlineAsync(_payload));

    [DisplayName("PING_BULK")]
    private Task<ResponseSummary> PingBulk(RespContext ctx) => Pipeline(() => ctx.PingAsync(_payload));

    [DisplayName("INCR")]
    private Task<int> Incr(RespContext ctx) => Pipeline(() => ctx.IncrAsync(_counterKey));

    [DisplayName("GET")]
    private Task<ResponseSummary> Get(RespContext ctx) => Pipeline(() => ctx.GetAsync(_getSetKey));

    private Task GetInit(RespContext ctx) => ctx.SetAsync(_getSetKey, _payload).AsTask();

    [DisplayName("SET")]
    private Task<ResponseSummary> Set(RespContext ctx) => Pipeline(() => ctx.SetAsync(_getSetKey, _payload));

    [DisplayName("LPUSH")]
    private Task<int> LPush(RespContext ctx) => Pipeline(() => ctx.LPushAsync(_listKey, _payload));

    [DisplayName("RPUSH")]
    private Task<int> RPush(RespContext ctx) => Pipeline(() => ctx.RPushAsync(_listKey, _payload));

    [DisplayName("LRANGE_100")]
    private Task<ResponseSummary> LRange100(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_listKey, 0, 99));

    [DisplayName("LRANGE_300")]
    private Task<ResponseSummary> LRange300(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_listKey, 0, 299));

    [DisplayName("LRANGE_500")]
    private Task<ResponseSummary> LRange500(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_listKey, 0, 499));

    [DisplayName("LRANGE_600")]
    private Task<ResponseSummary> LRange600(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_listKey, 0, 599));

    [DisplayName("LPOP")]
    private Task<ResponseSummary> LPop(RespContext ctx) => Pipeline(() => ctx.LPopAsync(_listKey));

    [DisplayName("RPOP")]
    private Task<ResponseSummary> RPop(RespContext ctx) => Pipeline(() => ctx.RPopAsync(_listKey));

    private Task LPopInit(RespContext ctx) => ctx.LPushAsync(_listKey, _payload, TotalOperations).AsTask();

    [DisplayName("SADD")]
    private Task<int> SAdd(RespContext ctx) => Pipeline(() => ctx.SAddAsync(_setKey, "element:__rand_int__"));

    [DisplayName("HSET")]
    private Task<int> HSet(RespContext ctx) =>
        Pipeline(() => ctx.HSetAsync(_hashKey, "element:__rand_int__", _payload));

    [DisplayName("ZADD")]
    private Task<int> ZAdd(RespContext ctx) => Pipeline(() => ctx.ZAddAsync(_sortedSetKey, 0, "element:__rand_int__"));

    [DisplayName("ZPOPMIN")]
    private Task<ResponseSummary> ZPopMin(RespContext ctx) => Pipeline(() => ctx.ZPopMinAsync(_sortedSetKey));

    private async Task ZPopMinInit(RespContext ctx)
    {
        int ops = TotalOperations;
        var rand = new Random();
        for (int i = 0; i < ops; i++)
        {
            await ctx.ZAddAsync(_sortedSetKey, (rand.NextDouble() * 2000) - 1000, "element:__rand_int__")
                .ConfigureAwait(false);
        }
    }

    [DisplayName("SPOP")]
    private Task<ResponseSummary> SPop(RespContext ctx) => Pipeline(() => ctx.SPopAsync(_setKey));

    private async Task SPopInit(RespContext ctx)
    {
        int ops = TotalOperations;
        for (int i = 0; i < ops; i++)
        {
            await ctx.SAddAsync(_setKey, "element:__rand_int__").ConfigureAwait(false);
        }
    }

    [DisplayName("MSET"), Description("10 keys")]
    private Task<Void> MSet(RespContext ctx) => Pipeline(() => ctx.MSetAsync(_pairs));

    private async Task LRangeInit(RespContext ctx)
    {
        for (int i = 0; i < TotalOperations; i++)
        {
            await ctx.LPushAsync(_listKey, _payload).ConfigureAwait(false);
        }
    }

    [DisplayName("XADD")]
    private Task<ResponseSummary> XAdd(RespContext ctx) =>
        Pipeline(() => ctx.XAddAsync(_streamKey, "*", "myfield", _payload));

    private int TotalOperations => _operationsPerClient * _clients.Length;

    private async Task RunAsync<T>(
        string key,
        Func<RespContext, Task<T>> action,
        Func<RespContext, Task> init = null,
        string format = "")
    {
        string name = action.Method.Name;

        if (action.Method.GetCustomAttribute(typeof(DisplayNameAttribute)) is DisplayNameAttribute
            {
                DisplayName: { Length: > 0 }
            } dna)
        {
            name = dna.DisplayName;
        }

        // skip test if not needed
        if (_tests.Count != 0 && !_tests.Contains(name)) return;

        // include additional test metadata
        string description = "";
        if (action.Method.GetCustomAttribute(typeof(DescriptionAttribute)) is DescriptionAttribute
            {
                Description: { Length: > 0 }
            } da)
        {
            description = $" ({da.Description})";
        }

        if (Quiet)
        {
            Console.Write($"{name}:");
        }
        else
        {
            Console.Write(
                $"====== {name}{description} ====== (clients: {_clients.Length:#,##0}, ops: {TotalOperations:#,##0}");
            if (Multiplexed)
            {
                Console.Write(", mux");
            }

            if (PipelineDepth > 1)
            {
                Console.Write($", pipeline: {PipelineDepth:#,##0}");
            }

            Console.WriteLine(")");
        }

        try
        {
            if (key is not null)
            {
                await _clients[0].DelAsync(key).ConfigureAwait(false);
            }

            if (init is not null)
            {
                await init(_clients[0]).ConfigureAwait(false);
            }

            var clients = _clients;
            var pending = new Task<T>[clients.Length];
            int index = 0;
#if DEBUG
            DebugCounters.Flush();
#endif
            // optionally support cancellation, applied per-test
            CancellationToken cancellationToken = CancellationToken.None;
            using var cts = SupportCancel ? new CancellationTokenSource(TimeSpan.FromSeconds(20)) : null;
            if (SupportCancel) cancellationToken = cts!.Token;

            var watch = Stopwatch.StartNew();
            foreach (var client in clients)
            {
                pending[index++] = Task.Run(() => action(client.WithCancellationToken(cancellationToken)));
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
            watch.Stop();

            var seconds = watch.Elapsed.TotalSeconds;
            var rate = TotalOperations / seconds;
            if (Quiet)
            {
                Console.WriteLine($"\t{rate:###,###,##0} requests per second");
                return;
            }
            else
            {
                Console.WriteLine(
                    $"{TotalOperations:###,###,##0} requests completed in {seconds:0.00} seconds, {rate:###,###,##0} ops/sec");
            }

            if (typeof(T) != typeof(Void) && !Quiet)
            {
                if (string.IsNullOrWhiteSpace(format))
                {
                    format = "Typical result: {0}";
                }

                T result = await pending[pending.Length - 1];
                Console.WriteLine(format, result);
            }
        }
        catch (Exception ex)
        {
            if (Quiet) Console.WriteLine();
            Console.Error.WriteLine(ex.Message);
        }
        finally
        {
#if DEBUG
            var counters = DebugCounters.Flush(); // flush even if not showing
            if (Quiet)
            {
                if (counters.WriteBytes != 0)
                {
                    Console.Write($"Write: {FormatBytes(counters.WriteBytes)}");
                    if (counters.WriteCount != 0) Console.Write($"; {counters.WriteCount:#,##0} sync");
                    if (counters.AsyncWriteInlineCount != 0)
                        Console.Write($"; {counters.AsyncWriteInlineCount:#,##0} async-inline");
                    if (counters.AsyncWriteCount != 0) Console.Write($"; {counters.AsyncWriteCount:#,##0} full-async");
                    Console.WriteLine();
                }

                if (counters.ReadBytes != 0)
                {
                    Console.Write($"Read: {FormatBytes(counters.ReadBytes)}");
                    if (counters.ReadCount != 0) Console.Write($"; {counters.ReadCount:#,##0} sync");
                    if (counters.AsyncReadInlineCount != 0)
                        Console.Write($"; {counters.AsyncReadInlineCount:#,##0} async-inline");
                    if (counters.AsyncReadCount != 0) Console.Write($"; {counters.AsyncReadCount:#,##0} full-async");
                    Console.WriteLine();
                }

                if (counters.DiscardAverage + counters.DiscardPartialCount != 0)
                {
                    Console.Write($"Discard average: {FormatBytes(counters.DiscardAverage)}");
                    if (counters.DiscardFullCount != 0) Console.Write($"; {counters.DiscardFullCount} full");
                    if (counters.DiscardPartialCount != 0) Console.Write($"; {counters.DiscardPartialCount} partial");
                    Console.WriteLine();
                }

                if (counters.CopyOutCount != 0)
                {
                    Console.WriteLine(
                        $"Copy out: {FormatBytes(counters.CopyOutBytes)}; {counters.CopyOutCount:#,##0} times");
                }

                if (counters.PipelineFullAsyncCount != 0
                    | counters.PipelineSendAsyncCount != 0
                    | counters.PipelineFullSyncCount != 0)
                {
                    Console.Write("Pipelining");
                    if (counters.PipelineFullSyncCount != 0)
                        Console.Write($"; full sync: {counters.PipelineFullSyncCount:#,##0}");
                    if (counters.PipelineSendAsyncCount != 0)
                        Console.Write($"; send async: {counters.PipelineSendAsyncCount:#,##0}");
                    if (counters.PipelineFullAsyncCount != 0)
                        Console.Write($"; full async: {counters.PipelineFullAsyncCount:#,##0}");
                    Console.WriteLine();
                }

                static string FormatBytes(long bytes)
                {
                    if (bytes > 1024 * 1024)
                    {
                        return $"{bytes >> 20:#,##0} MiB";
                    }

                    if (bytes > 1024)
                    {
                        return $"{bytes >> 10:#,##0} KiB";
                    }

                    return $"{bytes} B";
                }
            }
#endif
            if (!Quiet) Console.WriteLine();
        }
    }

    public async Task CleanupAsync()
    {
        try
        {
            var client = _clients[0];
            await client.DelAsync(_getSetKey).ConfigureAwait(false);
            await client.DelAsync(_counterKey).ConfigureAwait(false);
            await client.DelAsync(_listKey).ConfigureAwait(false);
            await client.DelAsync(_setKey).ConfigureAwait(false);
            await client.DelAsync(_hashKey).ConfigureAwait(false);
            await client.DelAsync(_sortedSetKey).ConfigureAwait(false);
            await client.DelAsync(_streamKey).ConfigureAwait(false);
            foreach (var pair in _pairs)
            {
                await client.DelAsync(pair.Key).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cleanup: {ex.Message}");
        }
    }

    public async Task InitAsync()
    {
        foreach (var client in _clients)
        {
            await client.PingAsync().ConfigureAwait(false);
        }
    }
}

internal static partial class RedisCommands
{
    [RespCommand]
    internal static partial ResponseSummary Ping(this in RespContext ctx);

    [RespCommand]
    internal static partial ResponseSummary SPop(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial int SAdd(this in RespContext ctx, string key, string payload);

    [RespCommand]
    internal static partial ResponseSummary Set(this in RespContext ctx, string key, byte[] payload);

    [RespCommand]
    internal static partial int LPush(this in RespContext ctx, string key, byte[] payload);

    [RespCommand(Formatter = "LPushFormatter.Instance")]
    internal static partial void LPush(this in RespContext ctx, string key, byte[] payload, int count);

    private sealed class LPushFormatter : IRespFormatter<(string Key, byte[] Payload, int Count)>
    {
        private LPushFormatter() { }
        public static readonly LPushFormatter Instance = new();
        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (string Key, byte[] Payload, int Count) request)
        {
            writer.WriteCommand(command, request.Count + 1);
            writer.WriteKey(request.Key);
            for (int i = 0; i < request.Count; i++)
            {
                // duplicate for lazy bulk load
                writer.WriteBulkString(request.Payload);
            }
        }
    }

    [RespCommand]
    internal static partial int RPush(this in RespContext ctx, string key, byte[] payload);

    [RespCommand]
    internal static partial ResponseSummary LPop(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial ResponseSummary RPop(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial ResponseSummary LRange(this in RespContext ctx, string key, int start, int stop);

    [RespCommand]
    internal static partial int HSet(this in RespContext ctx, string key, string field, byte[] payload);

    [RespCommand]
    internal static partial ResponseSummary Ping(this in RespContext ctx, byte[] payload);

    [RespCommand]
    internal static partial int Incr(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial ResponseSummary Del(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial ResponseSummary ZPopMin(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial int ZAdd(this in RespContext ctx, string key, double score, string payload);

    [RespCommand]
    internal static partial ResponseSummary XAdd(
        this in RespContext ctx,
        string key,
        string id,
        string field,
        byte[] value);

    [RespCommand]
    internal static partial ResponseSummary Get(this in RespContext ctx, string key);

    [RespCommand(Formatter = "PairsFormatter.Instance")] // custom command formatter
    internal static partial void MSet(this in RespContext ctx, (string, byte[])[] pairs);

    internal static ResponseSummary PingInline(this in RespContext ctx, byte[] payload)
        => ctx.Command("ping"u8, payload, InlinePingFormatter.Instance).Wait(ResponseSummary.Parser);

    internal static ValueTask<ResponseSummary> PingInlineAsync(this in global::Resp.RespContext ctx, byte[] payload)
        => ctx.Command("ping"u8, payload, InlinePingFormatter.Instance)
            .AsValueTask(ResponseSummary.Parser);

    private sealed class InlinePingFormatter : IRespFormatter<byte[]>
    {
        private InlinePingFormatter() { }
        public static readonly InlinePingFormatter Instance = new();

        public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in byte[] request)
        {
            writer.WriteRaw(command);
            writer.WriteRaw(" "u8);
            writer.WriteRaw(request);
            writer.WriteRaw("\r\n"u8);
        }
    }

    private sealed class PairsFormatter : IRespFormatter<(string Key, byte[] Value)[]>
    {
        public static readonly PairsFormatter Instance = new PairsFormatter();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (string Key, byte[] Value)[] request)
        {
            writer.WriteCommand(command, 2 * request.Length);
            foreach (var pair in request)
            {
                writer.WriteKey(pair.Key);
                writer.WriteBulkString(pair.Value);
            }
        }
    }
}
