using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Resp;

namespace BasicTest;

// influenced by redis-benchmark, see .md file
public partial class RespBenchmark : IDisposable
{
    private readonly RespConnectionPool _connectionPool;

    private readonly RespContext[] _clients;
    public int ClientCount => _clients.Length;

    private readonly int _operationsPerClient, _pipelineDepth;
    public const int DefaultPort = 6379, DefaultRequests = 100_000, DefaultPipelineDepth = 1, DefaultClients = 50;
    private readonly byte[] _payload;
    private readonly (string Key, byte[] Value)[] _pairs;

    private const string _key = "key:__rand_int__";

    public RespBenchmark(
        int port = DefaultPort,
        int requests = DefaultRequests,
        int pipelineDepth = DefaultPipelineDepth,
        int clients = DefaultClients)
    {
        if (clients <= 0) throw new ArgumentOutOfRangeException(nameof(clients));
        if (pipelineDepth <= 0) throw new ArgumentOutOfRangeException(nameof(pipelineDepth));
        _payload = "abc"u8.ToArray();
        _operationsPerClient = requests / clients;
        _pipelineDepth = pipelineDepth;
        _connectionPool = new(count: clients);
        _pairs = new (string, byte[])[10];
        for (var i = 0; i < 10; i++)
        {
            _pairs[i] = ($"{_key}{i}", _payload);
        }
        _clients = new RespContext[clients];
        for (int i = 0; i < clients; i++) // init all
        {
            _clients[i] = new(_connectionPool.GetConnection());
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
        await RunAsync(LRange100, LRangeInit).ConfigureAwait(false);
        await RunAsync(LRange300, LRangeInit).ConfigureAwait(false);
        await RunAsync(LRange500, LRangeInit).ConfigureAwait(false);
        await RunAsync(MSet).ConfigureAwait(false);
        await CleanupAsync().ConfigureAwait(false);
    }

    private async Task Pipeline(Func<ValueTask> operation)
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
    private Task PingBulk(RespContext ctx) => Pipeline(() => PingAsync(ctx, _payload));

    [RespCommand]
    internal partial byte[] Ping(in RespContext ctx, byte[] payload);

    [DisplayName("INCR")]
    private Task Incr(RespContext ctx) => Pipeline(() => IncrAsync(ctx, _key));

    [RespCommand]
    private partial int Incr(in RespContext ctx, string key);

    [RespCommand]
    private partial void Del(in RespContext ctx, string key);

    [DisplayName("GET")]
    private Task Get(RespContext ctx) => Pipeline(() => GetAsync(ctx, _key));

    [RespCommand]
    private partial byte[] Get(in RespContext ctx, string key);

    private Task GetInit(RespContext ctx) => SetAsync(ctx, _key, _payload).AsTask();

    [DisplayName("SET")]
    private Task Set(RespContext ctx) => Pipeline(() => SetAsync(ctx, _key, _payload));

    [RespCommand]
    private partial void Set(in RespContext ctx, string key, byte[] payload);

    [DisplayName("LPUSH")]
    private Task LPush(RespContext ctx) => Pipeline(() => LPushAsync(ctx, _key, _payload));

    [DisplayName("LRANGE_100 (100 of 450)")]
    private Task LRange100(RespContext ctx) => Pipeline(() => LRangeAsync(ctx, _key, 0, 99));

    [DisplayName("LRANGE_300 (300 of 450)")]
    private Task LRange300(RespContext ctx) => Pipeline(() => LRangeAsync(ctx, _key, 0, 299));

    [DisplayName("LRANGE_500 (450 of 450)")]
    private Task LRange500(RespContext ctx) => Pipeline(() => LRangeAsync(ctx, _key, 0, 499));

    [RespCommand]
    private partial byte[][] LRange(in RespContext ctx, string key, int start, int stop);

    [DisplayName("MSET (10 keys)")]
    private Task MSet(RespContext ctx) => Pipeline(() => MSetAsync(ctx, _pairs));

    // here we also demo a custom command formatter
    private ValueTask MSetAsync(in RespContext ctx, (string, byte[])[] pairs)
        => ctx.Command("mset"u8, pairs, PairsFormatter.Instance).AsValueTask();

    private sealed class PairsFormatter : IRespFormatter<(string Key, byte[] Value)[]>
    {
        public static readonly PairsFormatter Instance = new PairsFormatter();

        public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in (string Key, byte[] Value)[] request)
        {
            writer.WriteCommand(command, 2 * request.Length);
            foreach (var pair in request)
            {
                writer.WriteKey(pair.Key);
                writer.WriteBulkString(pair.Value);
            }
        }
    }
    private async Task LRangeInit(RespContext ctx)
    {
        for (int i = 0; i < 450; i++)
        {
            await LPushAsync(ctx, _key, _payload).ConfigureAwait(false);
        }
    }

    [RespCommand]
    private partial void LPush(in RespContext ctx, string key, byte[] payload);

    private async Task RunAsync(Func<RespContext, Task> action, Func<RespContext, Task> init = null)
    {
        await CleanupAsync().ConfigureAwait(false);
        if (init is not null) await init(_clients[0]).ConfigureAwait(false);

        string name = action.Method.Name;
        if (action.Method.GetCustomAttribute(typeof(DisplayNameAttribute)) is DisplayNameAttribute
            {
                DisplayName: { Length: > 0 }
            } attr)
        {
            name = attr.DisplayName;
        }

        var clients = _clients;
        var pending = new Task[clients.Length];
        int index = 0;
        var watch = Stopwatch.StartNew();
        foreach (var client in clients)
        {
            pending[index++] = Task.Run(() => action(client));
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
             {clients.Length:#,##0} parallel clients

             """);
    }

    public async Task CleanupAsync()
    {
        var client = _clients[0];
        await DelAsync(client, _key).ConfigureAwait(false);
        foreach (var pair in _pairs)
        {
            await DelAsync(client, pair.Key).ConfigureAwait(false);
        }
    }
    public async Task InitAsync()
    {
        foreach (var client in _clients)
        {
            await PingAsync(client).ConfigureAwait(false);
        }
    }

    [RespCommand]
    private partial void Ping(in RespContext ctx);
}
