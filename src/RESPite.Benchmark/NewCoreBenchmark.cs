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

    protected override RespContext CreateBatch(RespContext client) => client.CreateBatch().Context;

    protected override Func<ValueTask> GetFlush(RespContext client)
    {
        if (client.Connection is RespBatch batch)
        {
            return () =>
            {
                return new(batch.FlushAsync());
            };
        }

        return base.GetFlush(client);
    }

    [DisplayName("PING_INLINE")]
    private Task<RespParsers.ResponseSummary> PingInline(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.PingInlineAsync(_payload), flush);

    [DisplayName("PING_BULK")]
    private Task<RespParsers.ResponseSummary> PingBulk(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.PingAsync(_payload), flush);

    [DisplayName("INCR")]
    private Task<int> Incr(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.IncrAsync(_counterKey), flush);

    [DisplayName("GET")]
    private Task<RespParsers.ResponseSummary> Get(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.GetAsync(_getSetKey), flush);

    private Task GetInit(RespContext ctx) => ctx.SetAsync(_getSetKey, _payload).AsTask();

    [DisplayName("SET")]
    private Task<RespParsers.ResponseSummary> Set(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.SetAsync(_getSetKey, _payload), flush);

    [DisplayName("LPUSH")]
    private Task<int> LPush(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.LPushAsync(_listKey, _payload), flush);

    [DisplayName("RPUSH")]
    private Task<int> RPush(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.RPushAsync(_listKey, _payload), flush);

    [DisplayName("LRANGE_100")]
    private Task<RespParsers.ResponseSummary> LRange100(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.LRangeAsync(_listKey, 0, 99), flush);

    [DisplayName("LRANGE_300")]
    private Task<RespParsers.ResponseSummary> LRange300(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.LRangeAsync(_listKey, 0, 299), flush);

    [DisplayName("LRANGE_500")]
    private Task<RespParsers.ResponseSummary> LRange500(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.LRangeAsync(_listKey, 0, 499), flush);

    [DisplayName("LRANGE_600")]
    private Task<RespParsers.ResponseSummary> LRange600(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.LRangeAsync(_listKey, 0, 599), flush);

    [DisplayName("LPOP")]
    private Task<RespParsers.ResponseSummary> LPop(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.LPopAsync(_listKey), flush);

    [DisplayName("RPOP")]
    private Task<RespParsers.ResponseSummary> RPop(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.RPopAsync(_listKey), flush);

    private Task LPopInit(RespContext ctx) => ctx.LPushAsync(_listKey, _payload, TotalOperations).AsTask();

    [DisplayName("SADD")]
    private Task<int> SAdd(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.SAddAsync(_setKey, "element:__rand_int__"), flush);

    [DisplayName("HSET")]
    private Task<int> HSet(RespContext ctx, Func<ValueTask> flush) =>
        Pipeline(() => ctx.HSetAsync(_hashKey, "element:__rand_int__", _payload), flush);

    [DisplayName("ZADD")]
    private Task<int> ZAdd(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.ZAddAsync(_sortedSetKey, 0, "element:__rand_int__"), flush);

    [DisplayName("ZPOPMIN")]
    private Task<RespParsers.ResponseSummary> ZPopMin(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.ZPopMinAsync(_sortedSetKey), flush);

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
    private Task<RespParsers.ResponseSummary> SPop(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.SPopAsync(_setKey), flush);

    private async Task SPopInit(RespContext ctx)
    {
        int ops = TotalOperations;
        for (int i = 0; i < ops; i++)
        {
            await ctx.SAddAsync(_setKey, "element:__rand_int__").ConfigureAwait(false);
        }
    }

    [DisplayName("MSET"), Description("10 keys")]
    private Task<bool> MSet(RespContext ctx, Func<ValueTask> flush) => Pipeline(() => ctx.MSetAsync(_pairs), flush);

    private Task LRangeInit(RespContext ctx) => ctx.LPushAsync(_listKey, _payload, TotalOperations).AsTask();

    [DisplayName("XADD")]
    private Task<RespParsers.ResponseSummary> XAdd(RespContext ctx, Func<ValueTask> flush) =>
        Pipeline(() => ctx.XAddAsync(_streamKey, "*", "myfield", _payload), flush);
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
    internal static partial RespParsers.ResponseSummary LRange(this in RespContext ctx, string key, int start, int stop);

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
