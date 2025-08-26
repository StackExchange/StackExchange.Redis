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

    private readonly int _operationsPerClient, _pipelineDepth;
    private readonly bool _multiplexed, _cancel, _quiet;
    public const int DefaultPort = 6379, DefaultRequests = 100_000, DefaultPipelineDepth = 1, DefaultClients = 50;
    public const bool DefaultMultiplexed = false, DefaultCancel = false;
    public const string DefaultTests = "";
    private readonly byte[] _payload;
    private readonly (string Key, byte[] Value)[] _pairs;

    private const string _key = "key:__rand_int__";
    private readonly HashSet<string> _tests = new(StringComparer.OrdinalIgnoreCase);

    public RespBenchmark(
        int port = DefaultPort,
        int requests = DefaultRequests,
        int pipelineDepth = DefaultPipelineDepth,
        int clients = DefaultClients,
        bool multiplexed = DefaultMultiplexed,
        bool cancel = DefaultCancel,
        string tests = DefaultTests,
        bool quiet = false)
    {
        if (clients <= 0) throw new ArgumentOutOfRangeException(nameof(clients));
        if (pipelineDepth <= 0) throw new ArgumentOutOfRangeException(nameof(pipelineDepth));
        _payload = "abc"u8.ToArray();
        _operationsPerClient = requests / clients;
        _pipelineDepth = pipelineDepth;
        _multiplexed = multiplexed;
        _cancel = cancel;
        _quiet = quiet;
        _connectionPool = new(count: multiplexed ? 1 : clients);
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
            _pairs[i] = ($"{_key}{i}", _payload);
        }

        _clients = new RespContext[clients];
        if (multiplexed)
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
                if (_pipelineDepth > 1)
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

    public async Task RunAll(bool loop)
    {
        do
        {
            await InitAsync().ConfigureAwait(false);
            // await RunAsync(PingInline).ConfigureAwait(false);
            await RunAsync(PingBulk).ConfigureAwait(false);
            await RunAsync(Incr).ConfigureAwait(false);
            await RunAsync(Get, GetInit).ConfigureAwait(false);
            await RunAsync(Set).ConfigureAwait(false);
            await RunAsync(LPush).ConfigureAwait(false);
            await RunAsync(LRange100, LRangeInit450).ConfigureAwait(false);
            await RunAsync(LRange300, LRangeInit450).ConfigureAwait(false);
            await RunAsync(LRange500, LRangeInit450).ConfigureAwait(false);
            await RunAsync(LPop, LPopInit).ConfigureAwait(false);
            await RunAsync(SAdd).ConfigureAwait(false);
            await RunAsync(SPop, SPopInit).ConfigureAwait(false);
            await RunAsync(MSet).ConfigureAwait(false);
            await CleanupAsync().ConfigureAwait(false);
        }
        // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
        while (loop);
    }

    private async Task<Void> Pipeline(Func<ValueTask> operation)
    {
        if (_pipelineDepth == 1)
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
            if (_pipelineDepth == 1)
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
    private Task<int> Incr(RespContext ctx) => Pipeline(() => ctx.IncrAsync(_key));

    [DisplayName("GET")]
    private Task<ResponseSummary> Get(RespContext ctx) => Pipeline(() => ctx.GetAsync(_key));

    private Task GetInit(RespContext ctx) => ctx.SetAsync(_key, _payload).AsTask();

    [DisplayName("SET")]
    private Task<Void> Set(RespContext ctx) => Pipeline(() => ctx.SetAsync(_key, _payload));

    [DisplayName("LPUSH")]
    private Task<Void> LPush(RespContext ctx) => Pipeline(() => ctx.LPushAsync(_key, _payload));

    [DisplayName("LRANGE_100"), Description("(100 of 450)")]
    private Task<ResponseSummary> LRange100(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_key, 0, 99));

    [DisplayName("LRANGE_300"), Description("(300 of 450)")]
    private Task<ResponseSummary> LRange300(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_key, 0, 299));

    [DisplayName("LRANGE_500"), Description("(450 of 450)")]
    private Task<ResponseSummary> LRange500(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_key, 0, 499));

    [DisplayName("LPOP")]
    private Task<ResponseSummary> LPop(RespContext ctx) => Pipeline(() => ctx.LPopAsync(_key));

    private async Task LPopInit(RespContext ctx)
    {
        int ops = TotalOperations;
        for (int i = 0; i < ops; i++)
        {
            await ctx.LPushAsync(_key, _payload).ConfigureAwait(false);
        }
    }

    [DisplayName("SADD")]
    private Task<Void> SAdd(RespContext ctx) => Pipeline(() => ctx.SAddAsync(_key, _payload));

    [DisplayName("SPOP")]
    private Task<ResponseSummary> SPop(RespContext ctx) => Pipeline(() => ctx.SPopAsync(_key));

    private async Task SPopInit(RespContext ctx)
    {
        int ops = TotalOperations + 5;
        for (int i = 0; i < ops; i++)
        {
            await ctx.SAddAsync(_key, _payload).ConfigureAwait(false);
        }
    }

    [DisplayName("MSET (10 keys)")]
    private Task<Void> MSet(RespContext ctx) => Pipeline(() => ctx.MSetAsync(_pairs));

    private async Task LRangeInit450(RespContext ctx)
    {
        for (int i = 0; i < 450; i++)
        {
            await ctx.LPushAsync(_key, _payload).ConfigureAwait(false);
        }
    }

    private int TotalOperations => _operationsPerClient * _clients.Length;

    private async Task RunAsync<T>(
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
            description = $" {description}";
        }

        if (_quiet)
        {
            Console.Write($"{name}: ");
        }
        else
        {
            Console.Write(
                $"====== {name}{description} ====== (clients: {_clients.Length:#,##0}, ops: {TotalOperations:#,##0}");
            if (_multiplexed)
            {
                Console.Write(", mux");
            }
            if (_pipelineDepth > 1)
            {
                Console.Write($", pipeline: {_pipelineDepth:#,##0}");
            }
            Console.WriteLine(")");
        }

        try
        {
            await CleanupAsync().ConfigureAwait(false);
            if (init is not null) await init(_clients[0]).ConfigureAwait(false);

            var clients = _clients;
            var pending = new Task<T>[clients.Length];
            int index = 0;
#if DEBUG
            DebugCounters.Flush();
#endif
            // optionally support cancellation, applied per-test
            CancellationToken cancellationToken = CancellationToken.None;
            using var cts = _cancel ? new CancellationTokenSource(TimeSpan.FromSeconds(20)) : null;
            if (_cancel) cancellationToken = cts!.Token;

            var watch = Stopwatch.StartNew();
            foreach (var client in clients)
            {
                pending[index++] = Task.Run(() => action(client.WithCancellationToken(cancellationToken)));
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
            watch.Stop();

            var seconds = watch.Elapsed.TotalSeconds;
            var rate = TotalOperations / seconds;
            if (_quiet)
            {
                Console.WriteLine($"{rate:###,###,##0.00} requests per second");
                return;
            }
            else
            {
                Console.WriteLine(
                    $"{TotalOperations:###,###,##0} requests completed in {seconds:0.00} seconds, {rate::###,###,##0.00} ops/sec");
            }
            if (typeof(T) != typeof(Void) && !_quiet)
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
            if (_quiet) Console.WriteLine();
            Console.Error.WriteLine(ex.Message);
        }
        finally
        {
#if DEBUG
            var counters = DebugCounters.Flush(); // flush even if not showing
            if (_quiet)
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
            if (!_quiet) Console.WriteLine();
        }
    }

    public async Task CleanupAsync()
    {
        try
        {
            var client = _clients[0];
            await client.DelAsync(_key).ConfigureAwait(false);
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
    internal static partial void SAdd(this in RespContext ctx, string key, byte[] payload);

    [RespCommand]
    internal static partial void Set(this in RespContext ctx, string key, byte[] payload);

    [RespCommand]
    internal static partial void LPush(this in RespContext ctx, string key, byte[] payload);

    [RespCommand]
    internal static partial ResponseSummary LPop(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial ResponseSummary LRange(this in RespContext ctx, string key, int start, int stop);

    [RespCommand]
    internal static partial ResponseSummary Ping(this in RespContext ctx, byte[] payload);

    [RespCommand]
    internal static partial int Incr(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial void Del(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial ResponseSummary Get(this in RespContext ctx, string key);

    [RespCommand(Formatter = "PairsFormatter.Instance")] // custom command formatter
    internal static partial void MSet(this in RespContext ctx, (string, byte[])[] pairs);

    internal static ResponseSummary PingInline(this in RespContext ctx, byte[] payload)
        => ctx.Command("ping"u8, payload, InlinePingFormatter.Instance).Wait(ResponseSummary.Parser);

    internal static ValueTask<ResponseSummary> PingInlineAsync(this in global::Resp.RespContext ctx, byte[] payload)
        => ctx.Command("ping"u8, payload, InlinePingFormatter.Instance)
            .AsValueTask<global::Resp.ResponseSummary>(ResponseSummary.Parser);

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
