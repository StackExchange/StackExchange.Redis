using System;
using System.ComponentModel;
using System.Diagnostics;
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

    protected override Task DeleteAsync(RespContext client, string key) => client.DelAsync(key).AsTask();

    protected override RespContext WithCancellation(RespContext client, CancellationToken cancellationToken)
        => client.WithCancellationToken(cancellationToken);

    protected override Task InitAsync(RespContext client) => client.PingAsync().AsTask();

    public NewCoreBenchmark(string[] args) : base(args)
    {
        _clients = new RespContext[ClientCount];

        _connectionPool = new(count: Multiplexed ? 1 : ClientCount);
        _connectionPool.ConnectionError += (_, e) => Program.WriteException(e.Exception, e.Operation);
        _pairs = new (string, byte[])[10];

        for (var i = 0; i < 10; i++)
        {
            _pairs[i] = ($"key:__rand_int__{i}", Payload);
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

        await RunAsync(GetSetKey, Set, GetName(Get)).ConfigureAwait(false);
        await RunAsync(GetSetKey, Get).ConfigureAwait(false);

        await RunAsync(CounterKey, Incr, true).ConfigureAwait(false);

        await RunAsync(ListKey, LPush, GetName(LPop)).ConfigureAwait(false);
        await RunAsync(ListKey, LPop).ConfigureAwait(false);

        await RunAsync(ListKey, RPush, GetName(RPop)).ConfigureAwait(false);
        await RunAsync(ListKey, RPop).ConfigureAwait(false);

        await RunAsync(SetKey, SAdd, GetName(SPop)).ConfigureAwait(false);
        await RunAsync(SetKey, SPop).ConfigureAwait(false);

        await RunAsync(HashKey, HSet).ConfigureAwait(false);

        await RunAsync(SortedSetKey, ZAdd, GetName(ZPopMin)).ConfigureAwait(false);
        await RunAsync(SortedSetKey, ZPopMin).ConfigureAwait(false);

        await RunAsync(null, MSet).ConfigureAwait(false);
        await RunAsync(StreamKey, XAdd).ConfigureAwait(false);

        // leave until last, they're slower
        if (RunTest(GetName(LRange100)) ||
            RunTest(GetName(LRange300)) ||
            RunTest(GetName(LRange500)) ||
            RunTest(GetName(LRange600)))
        {
            await LRangeInit650(GetClient(0)).ConfigureAwait(false);
            await RunAsync(ListKey, LRange100, false, 10).ConfigureAwait(false);
            await RunAsync(ListKey, LRange300, false, 10).ConfigureAwait(false);
            await RunAsync(ListKey, LRange500, false, 10).ConfigureAwait(false);
            await RunAsync(ListKey, LRange600, false, 10).ConfigureAwait(false);
        }

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
    // ReSharper disable once UnusedMember.Local
    private ValueTask<RespParsers.ResponseSummary> PingInline(RespContext ctx) => ctx.PingInlineAsync(Payload);

    [DisplayName("PING_BULK")]
    private ValueTask<RespParsers.ResponseSummary> PingBulk(RespContext ctx) => ctx.PingAsync(Payload);

    [DisplayName("INCR")]
    private ValueTask<int> Incr(RespContext ctx) => ctx.IncrAsync(CounterKey);

    [DisplayName("GET")]
    private ValueTask<RespParsers.ResponseSummary> Get(RespContext ctx) => ctx.GetAsync(GetSetKey);

    [DisplayName("SET")]
    private ValueTask<RespParsers.ResponseSummary> Set(RespContext ctx) => ctx.SetAsync(GetSetKey, Payload);

    [DisplayName("LPUSH")]
    private ValueTask<int> LPush(RespContext ctx) => ctx.LPushAsync(ListKey, Payload);

    [DisplayName("RPUSH")]
    private ValueTask<int> RPush(RespContext ctx) => ctx.RPushAsync(ListKey, Payload);

    [DisplayName("LRANGE_100")]
    private ValueTask<RespParsers.ResponseSummary> LRange100(RespContext ctx) => ctx.LRangeAsync(ListKey, 0, 99);

    [DisplayName("LRANGE_300")]
    private ValueTask<RespParsers.ResponseSummary> LRange300(RespContext ctx) => ctx.LRangeAsync(ListKey, 0, 299);

    [DisplayName("LRANGE_500")]
    private ValueTask<RespParsers.ResponseSummary> LRange500(RespContext ctx) => ctx.LRangeAsync(ListKey, 0, 499);

    [DisplayName("LRANGE_600")]
    private ValueTask<RespParsers.ResponseSummary> LRange600(RespContext ctx) => ctx.LRangeAsync(ListKey, 0, 599);

    [DisplayName("LPOP")]
    private ValueTask<RespParsers.ResponseSummary> LPop(RespContext ctx) => ctx.LPopAsync(ListKey);

    [DisplayName("RPOP")]
    private ValueTask<RespParsers.ResponseSummary> RPop(RespContext ctx) => ctx.RPopAsync(ListKey);

    [DisplayName("SADD")]
    private ValueTask<int> SAdd(RespContext ctx) => ctx.SAddAsync(SetKey, "element:__rand_int__");

    [DisplayName("HSET")]
    private ValueTask<int> HSet(RespContext ctx) => ctx.HSetAsync(HashKey, "element:__rand_int__", Payload);

    [DisplayName("ZADD")]
    private ValueTask<int> ZAdd(RespContext ctx) => ctx.ZAddAsync(SortedSetKey, 0, "element:__rand_int__");

    [DisplayName("ZPOPMIN")]
    private ValueTask<RespParsers.ResponseSummary> ZPopMin(RespContext ctx) => ctx.ZPopMinAsync(SortedSetKey);

    [DisplayName("SPOP")]
    private ValueTask<RespParsers.ResponseSummary> SPop(RespContext ctx) => ctx.SPopAsync(SetKey);

    [DisplayName("MSET"), Description("10 keys")]
    private ValueTask<bool> MSet(RespContext ctx) => ctx.MSetAsync(_pairs);

    private async ValueTask LRangeInit650(RespContext ctx)
    {
        await ctx.DelAsync(ListKey).ConfigureAwait(false);
        await ctx.LPushAsync(ListKey, Payload, 650);
        if (await ctx.LLenAsync(ListKey).ConfigureAwait(false) != 650)
        {
            throw new InvalidOperationException();
        }
    }

    [DisplayName("XADD")]
    private ValueTask<RespParsers.ResponseSummary> XAdd(RespContext ctx) =>
        ctx.XAddAsync(StreamKey, "*", "myfield", Payload);

    protected override async Task RunBasicLoopAsync(int clientId)
    {
        // The purpose of this is to represent a more realistic loop using natural code
        // rather than code that is drowning in test infrastructure.
        var client = GetClient(clientId);
        var depth = PipelineDepth;
        int tickCount = 0; // this is just so we don't query DateTime.
        long previousValue = (await client.GetInt32Async(CounterKey).ConfigureAwait(false)) ?? 0,
            currentValue = previousValue;
        var watch = Stopwatch.StartNew();
        long previousMillis = watch.ElapsedMilliseconds;

        bool Tick()
        {
            var currentMillis = watch.ElapsedMilliseconds;
            var elapsedMillis = currentMillis - previousMillis;
            if (elapsedMillis >= 1000)
            {
                if (clientId == 0) // only one client needs to update the UI
                {
                    var qty = currentValue - previousValue;
                    var seconds = elapsedMillis / 1000.0;
                    Console.WriteLine(
                        $"{qty:#,###,##0} ops in {seconds:#0.00}s, {qty / seconds:#,###,##0}/s\ttotal: {currentValue:#,###,###,##0}");

                    // reset for next UI update
                    previousValue = currentValue;
                    previousMillis = currentMillis;
                }

                if (currentMillis >= 20_000)
                {
                    if (clientId == 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine(
                            $"\t Overall: {currentValue:#,###,###,##0} ops in {currentMillis / 1000:#0.00}s, {currentValue / (currentMillis / 1000.0):#,###,##0}/s");
                        Console.WriteLine();
                    }

                    return true; // stop after some time
                }
            }

            tickCount = 0;
            return false;
        }

        if (depth <= 1)
        {
            while (true)
            {
                currentValue = await client.IncrAsync(CounterKey).ConfigureAwait(false);

                if (++tickCount >= 1000 && Tick()) break; // only check whether to output every N iterations
            }
        }
        else
        {
            ValueTask<int>[] pending = new ValueTask<int>[depth];
            await using var batch = client.CreateBatch(depth);
            var ctx = batch.Context;
            while (true)
            {
                for (int i = 0; i < depth; i++)
                {
                    pending[i] = ctx.IncrAsync(CounterKey);
                }

                await batch.FlushAsync().ConfigureAwait(false);
                batch.EnsureCapacity(depth); // batches don't assume re-use
                for (var i = 0; i < depth; i++)
                {
                    currentValue = await pending[i].ConfigureAwait(false);
                }

                tickCount += depth;
                if (tickCount >= 1000 && Tick()) break; // only check whether to output every N iterations
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
    internal static partial int LLen(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial int LPush(this in RespContext ctx, string key, byte[] payload);

    [RespCommand(Formatter = LPushFormatter.Name)]
    internal static partial int LPush(this in RespContext ctx, string key, byte[] payload, int count);

    private sealed class LPushFormatter : IRespFormatter<(string Key, byte[] Payload, int Count)>
    {
        public const string Name = $"{nameof(LPushFormatter)}.{nameof(Instance)}";
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
    internal static partial int? GetInt32(this in RespContext ctx, string key);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary XAdd(
        this in RespContext ctx,
        string key,
        string id,
        string field,
        byte[] value);

    [RespCommand]
    internal static partial RespParsers.ResponseSummary Get(this in RespContext ctx, string key);

    [RespCommand(Formatter = PairsFormatter.Name)] // custom command formatter
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
        public const string Name = $"{nameof(PairsFormatter)}.{nameof(Instance)}";
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
