using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly bool _multiplexed, _cancel;
    public const int DefaultPort = 6379, DefaultRequests = 100_000, DefaultPipelineDepth = 1, DefaultClients = 50;
    public const bool DefaultMultiplexed = false, DefaultCancel = false;
    private readonly byte[] _payload;
    private readonly (string Key, byte[] Value)[] _pairs;

    private const string _key = "key:__rand_int__";

    public RespBenchmark(
        int port = DefaultPort,
        int requests = DefaultRequests,
        int pipelineDepth = DefaultPipelineDepth,
        int clients = DefaultClients,
        bool multiplexed = DefaultMultiplexed,
        bool cancel = DefaultCancel)
    {
        if (clients <= 0) throw new ArgumentOutOfRangeException(nameof(clients));
        if (pipelineDepth <= 0) throw new ArgumentOutOfRangeException(nameof(pipelineDepth));
        _payload = "abc"u8.ToArray();
        _operationsPerClient = requests / clients;
        _pipelineDepth = pipelineDepth;
        _multiplexed = multiplexed;
        _cancel = cancel;
        _connectionPool = new(count: multiplexed ? 1 : clients);
        _pairs = new (string, byte[])[10];
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

    public async Task RunAll()
    {
        await InitAsync().ConfigureAwait(false);
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
        T result = default;
        if (_pipelineDepth == 1)
        {
            for (var i = 0; i < _operationsPerClient; i++)
            {
                result = await operation().ConfigureAwait(false);
            }
        }
        else
        {
            var queue = new Queue<ValueTask<T>>(_operationsPerClient);
            for (var i = 0; i < _operationsPerClient; i++)
            {
                if (queue.Count == _operationsPerClient)
                {
                    result = await queue.Dequeue().ConfigureAwait(false);
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

    [DisplayName("LRANGE_100 (100 of 450)")]
    private Task<ResponseSummary> LRange100(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_key, 0, 99));

    [DisplayName("LRANGE_300 (300 of 450)")]
    private Task<ResponseSummary> LRange300(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_key, 0, 299));

    [DisplayName("LRANGE_500 (450 of 450)")]
    private Task<ResponseSummary> LRange500(RespContext ctx) => Pipeline(() => ctx.LRangeAsync(_key, 0, 499));

    [DisplayName("LPOP")]
    private Task<ResponseSummary> LPop(RespContext ctx) => Pipeline(() => ctx.LPopAsync(_key));

    private async Task LPopInit(RespContext ctx)
    {
        int ops = _operationsPerClient * _clients.Length;
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
        int ops = (_operationsPerClient * _clients.Length) + 5;
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

    private async Task RunAsync<T>(
        Func<RespContext, Task<T>> action,
        Func<RespContext, Task> init = null,
        string format = "")
    {
        string name = action.Method.Name;

        if (action.Method.GetCustomAttribute(typeof(DisplayNameAttribute)) is DisplayNameAttribute
            {
                DisplayName: { Length: > 0 }
            } attr)
        {
            name = attr.DisplayName;
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var watch = Stopwatch.StartNew();
            foreach (var client in clients)
            {
                pending[index++] = Task.Run(() => action(client.WithCancellationToken(cts.Token)));
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
            watch.Stop();

            var totalOperations = _operationsPerClient * _clients.Length;
            var seconds = watch.Elapsed.TotalSeconds;
            var rate = (totalOperations / seconds) / 1000;
            Console.WriteLine(
                $"""
                 ====== {name} ======
                 {totalOperations:###,###,##0} requests completed in {seconds:0.00} seconds, {rate:0.00} kops/sec""
                 {clients.Length:#,##0} parallel clients{(_multiplexed ? ", multiplexed" : "")}
                 """);
#if DEBUG
            var counters = DebugCounters.Flush();
            Console.WriteLine(
                $"Read (s/a/MiB): {counters.Read:#,##0}/{counters.AsyncRead:#,##0}/{counters.ReadBytes >> 20:#,##0}, Grow: {counters.Grow:#,##0}, Shuffle (count/MiB): {counters.ShuffleCount:#,##0}/{counters.ShuffleBytes >> 20:#,##0}, Copy out (count/MiB): {counters.CopyOutCount}/{counters.CopyOutBytes >> 20:#,##0}");
            Console.WriteLine(
                $"Discard (full/partial/avg): {counters.DiscardFullCount:#,##0}/{counters.DiscardPartialCount:#,##0}/{counters.DiscardAverage:#,##0}");
#endif
            if (typeof(T) != typeof(Void))
            {
                if (string.IsNullOrWhiteSpace(format))
                {
                    format = "Typical result: {0}";
                }

                T result = await pending[pending.Length - 1];
                Console.WriteLine(format, result);
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"""
                 ====== {name} ======
                 {ex.Message}

                 """);
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
    internal static partial void Ping(this in RespContext ctx);

    [RespCommand(Parser = RespCommandAttribute.Parsers.Summary)]
    internal static partial ResponseSummary SPop(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial void SAdd(this in RespContext ctx, string key, byte[] payload);

    [RespCommand]
    internal static partial void Set(this in RespContext ctx, string key, byte[] payload);

    [RespCommand]
    internal static partial void LPush(this in RespContext ctx, string key, byte[] payload);

    [RespCommand(Parser = RespCommandAttribute.Parsers.Summary)]
    internal static partial ResponseSummary LPop(this in RespContext ctx, string key);

    [RespCommand(Parser = RespCommandAttribute.Parsers.Summary)]
    internal static partial ResponseSummary LRange(this in RespContext ctx, string key, int start, int stop);

    [RespCommand(Parser = RespCommandAttribute.Parsers.Summary)]
    internal static partial ResponseSummary Ping(this in RespContext ctx, byte[] payload);

    [RespCommand]
    internal static partial int Incr(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial void Del(this in RespContext ctx, string key);

    [RespCommand(Parser = RespCommandAttribute.Parsers.Summary)]
    internal static partial ResponseSummary Get(this in RespContext ctx, string key);

    [RespCommand(Formatter = "PairsFormatter.Instance")] // custom command formatter
    internal static partial void MSet(this in RespContext ctx, (string, byte[])[] pairs);

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
