using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Connections;
using RESPite.Messages;

namespace RESPite.Benchmark;

public sealed class NewCoreBenchmark : BenchmarkBase<RespContext>
{
    public override string ToString() => "new IO core";

    private readonly RespConnectionPool _connectionPool;

    private readonly RespContext[] _clients;
    private readonly (string Key, byte[] Value)[] _pairs;

    protected override RespContext GetClient(int index) => _clients[index];

    protected override Task Delete(RespContext client, string key) => client.DelAsync(key).AsTask();

    protected override RespContext WithCancellation(RespContext client, CancellationToken cancellationToken)
        => client.WithCancellationToken(cancellationToken);

    protected override Task InitAsync(RespContext client) => client.PingAsync().AsTask();

    public NewCoreBenchmark(string[] args) : base(args)
    {
        _clients = new RespContext[ClientCount];

        _connectionPool = new(count: Multiplexed ? 1 : ClientCount);
        _connectionPool.ConnectionError += (sender, args) => Program.WriteException(args.Exception, args.Operation);
        _pairs = new (string, byte[])[10];

        for (var i = 0; i < 10; i++)
        {
            _pairs[i] = ($"{"key:__rand_int__"}{i}", _payload);
        }

        if (Multiplexed)
        {
            var conn = _connectionPool.GetConnection().Synchronized();
            var ctx = conn.Context;
            for (int i = 0; i < ClientCount; i++) // init all
            {
                _clients[i] = ctx;
            }
        }
        else
        {
            for (int i = 0; i < ClientCount; i++) // init all
            {
                var conn = _connectionPool.GetConnection();
                if (PipelineDepth > 1)
                {
                    conn = conn.Synchronized();
                }

                _clients[i] = conn.Context;
            }
        }
    }

    public override void Dispose()
    {
        _connectionPool.Dispose();
        foreach (var client in _clients)
        {
            client.Connection.Dispose();
        }
    }

    protected override async Task OnCleanupAsync(RespContext client)
    {
        foreach (var pair in _pairs)
        {
            await client.DelAsync(pair.Key).ConfigureAwait(false);
        }
    }

    public override async Task RunAll()
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

    protected override RespContext CreateBatch(RespContext client) => client.CreateBatch(PipelineDepth).Context;

    protected override ValueTask Flush(RespContext client)
    {
        if (client.Connection is RespBatch batch)
        {
            return new(batch.FlushAsync());
        }

        return default;
    }

    protected override void PrepareBatch(RespContext client, int count)
    {
        if (client.Connection is RespBatch batch)
        {
            batch.EnsureCapacity(count);
        }
    }

    [DisplayName("PING_INLINE")]
    private ValueTask<RespParsers.ResponseSummary> PingInline(RespContext ctx) => ctx.PingInlineAsync(_payload);

    [DisplayName("PING_BULK")]
    private ValueTask<RespParsers.ResponseSummary> PingBulk(RespContext ctx) => ctx.PingAsync(_payload);

    [DisplayName("INCR")]
    private ValueTask<int> Incr(RespContext ctx) => ctx.IncrAsync(_counterKey);

    [DisplayName("GET")]
    private ValueTask<RespParsers.ResponseSummary> Get(RespContext ctx) => ctx.GetAsync(_getSetKey);

    private ValueTask GetInit(RespContext ctx) => ctx.SetAsync(_getSetKey, _payload).AsUntypedValueTask();

    [DisplayName("SET")]
    private ValueTask<RespParsers.ResponseSummary> Set(RespContext ctx) => ctx.SetAsync(_getSetKey, _payload);

    [DisplayName("LPUSH")]
    private ValueTask<int> LPush(RespContext ctx) => ctx.LPushAsync(_listKey, _payload);

    [DisplayName("RPUSH")]
    private ValueTask<int> RPush(RespContext ctx) => ctx.RPushAsync(_listKey, _payload);

    [DisplayName("LRANGE_100")]
    private ValueTask<RespParsers.ResponseSummary> LRange100(RespContext ctx) => ctx.LRangeAsync(_listKey, 0, 99);

    [DisplayName("LRANGE_300")]
    private ValueTask<RespParsers.ResponseSummary> LRange300(RespContext ctx) => ctx.LRangeAsync(_listKey, 0, 299);

    [DisplayName("LRANGE_500")]
    private ValueTask<RespParsers.ResponseSummary> LRange500(RespContext ctx) => ctx.LRangeAsync(_listKey, 0, 499);

    [DisplayName("LRANGE_600")]
    private ValueTask<RespParsers.ResponseSummary> LRange600(RespContext ctx) => ctx.LRangeAsync(_listKey, 0, 599);

    [DisplayName("LPOP")]
    private ValueTask<RespParsers.ResponseSummary> LPop(RespContext ctx) => ctx.LPopAsync(_listKey);

    [DisplayName("RPOP")]
    private ValueTask<RespParsers.ResponseSummary> RPop(RespContext ctx) => ctx.RPopAsync(_listKey);

    private ValueTask LPopInit(RespContext ctx) =>
        ctx.LPushAsync(_listKey, _payload, TotalOperations).AsUntypedValueTask();

    [DisplayName("SADD")]
    private ValueTask<int> SAdd(RespContext ctx) => ctx.SAddAsync(_setKey, "element:__rand_int__");

    [DisplayName("HSET")]
    private ValueTask<int> HSet(RespContext ctx) => ctx.HSetAsync(_hashKey, "element:__rand_int__", _payload);

    [DisplayName("ZADD")]
    private ValueTask<int> ZAdd(RespContext ctx) => ctx.ZAddAsync(_sortedSetKey, 0, "element:__rand_int__");

    [DisplayName("ZPOPMIN")]
    private ValueTask<RespParsers.ResponseSummary> ZPopMin(RespContext ctx) => ctx.ZPopMinAsync(_sortedSetKey);

    private async ValueTask ZPopMinInit(RespContext ctx)
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
    private ValueTask<RespParsers.ResponseSummary> SPop(RespContext ctx) => ctx.SPopAsync(_setKey);

    private async ValueTask SPopInit(RespContext ctx)
    {
        int ops = TotalOperations;
        for (int i = 0; i < ops; i++)
        {
            await ctx.SAddAsync(_setKey, "element:__rand_int__").ConfigureAwait(false);
        }
    }

    [DisplayName("MSET"), Description("10 keys")]
    private ValueTask<bool> MSet(RespContext ctx) => ctx.MSetAsync(_pairs);

    private ValueTask LRangeInit(RespContext ctx) =>
        ctx.LPushAsync(_listKey, _payload, TotalOperations).AsUntypedValueTask();

    [DisplayName("XADD")]
    private ValueTask<RespParsers.ResponseSummary> XAdd(RespContext ctx) =>
        ctx.XAddAsync(_streamKey, "*", "myfield", _payload);

    public async Task<int> RunBasicLoopAsync()
    {
        var client = GetClient(0);
        _ = await client.DelAsync(_counterKey).ConfigureAwait(false);

        if (ClientCount <= 1)
        {
            await RunBasicLoopAsync(0);
        }
        else
        {
            Task[] tasks = new Task[ClientCount];
            for (int i = 0; i < ClientCount; i++)
            {
                var loopSnapshot = i;
                tasks[i] = Task.Run(() => RunBasicLoopAsync(loopSnapshot));
            }

            await Task.WhenAll(tasks);
        }

        return 0;
    }

    public async Task RunBasicLoopAsync(int clientId)
    {
        var client = GetClient(clientId);
        var depth = PipelineDepth;
        int localCount = 0;
        long lastValue = await client.GetInt32Async(_counterKey).ConfigureAwait(false),
            currentValue = lastValue;
        var previous = DateTime.UtcNow;

        void Tick()
        {
            DateTime now;
            if (clientId == 0 && ((now = DateTime.Now) - previous).TotalSeconds >= 1)
            {
                Console.WriteLine($"{currentValue - lastValue} ops in {now - previous}");
                previous = now;
                lastValue = currentValue;
                localCount = 0;
            }
        }
        if (depth <= 1)
        {
            while (true)
            {
                await client.IncrAsync(_counterKey).ConfigureAwait(false);

                if (++localCount >= 1000) Tick();
            }
        }
        else
        {
            ValueTask<int>[] pending = new ValueTask<int>[depth];
            using (var batch = client.CreateBatch(depth))
            {
                var ctx = batch.Context;
                while (true)
                {
                    for (int i = 0; i < depth; i++)
                    {
                        pending[i] = ctx.IncrAsync(_counterKey);
                    }

                    await batch.FlushAsync().ConfigureAwait(false);
                    batch.EnsureCapacity(depth); // batches don't assume re-use
                    for (int i = 0; i < depth; i++)
                    {
                        currentValue = await pending[i].ConfigureAwait(false);
                    }

                    localCount += depth;
                    if (localCount >= 1000) Tick();
                }
            }
        }
    }
}

internal static partial class RedisCommands
{
    [RespCommand]
    internal static partial RespParsers.ResponseSummary Ping(this in RespContext ctx);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary SPop(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial int SAdd(this in RespContext ctx, string key, string payload);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary Set(this in RespContext ctx, string key, byte[] payload);

    [RespCommand]
    internal static partial int LPush(this in RespContext ctx, string key, byte[] payload);

    [RespCommand(Formatter = "LPushFormatter.Instance")]
    internal static partial int LPush(this in RespContext ctx, string key, byte[] payload, int count);

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
    internal static partial RespParsers.ResponseSummary LPop(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary RPop(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary
        LRange(this in RespContext ctx, string key, int start, int stop);

    [RespCommand]
    internal static partial int HSet(this in RespContext ctx, string key, string field, byte[] payload);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary Ping(this in RespContext ctx, byte[] payload);

    [RespCommand]
    internal static partial int Incr(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary Del(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary ZPopMin(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial int ZAdd(this in RespContext ctx, string key, double score, string payload);

    [RespCommand("get")]
    internal static partial int GetInt32(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary XAdd(
        this in RespContext ctx,
        string key,
        string id,
        string field,
        byte[] value);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary Get(this in RespContext ctx, string key);

    [RespCommand(Formatter = "PairsFormatter.Instance")] // custom command formatter
    internal static partial bool MSet(this in RespContext ctx, (string, byte[])[] pairs);

    internal static RespParsers.ResponseSummary PingInline(this in RespContext ctx, byte[] payload)
        => ctx.Command("ping"u8, payload, InlinePingFormatter.Instance).Wait(RespParsers.ResponseSummary.Parser);

    internal static ValueTask<RespParsers.ResponseSummary> PingInlineAsync(this in RespContext ctx, byte[] payload)
        => ctx.Command("ping"u8, payload, InlinePingFormatter.Instance)
            .Send(RespParsers.ResponseSummary.Parser);

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
