#if NEWCORE
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RESPite.Benchmark;

/// <summary>The same operations as <see cref="DatabaseApiBenchmark"/>, through the grouped context API.</summary>
public sealed class ContextApiBenchmark : BenchmarkBase<ContextApiBenchmark.Target>
{
    /// <summary>
    /// One caller: a keyspace target whose context is its database, or - while a batch is being
    /// composed - that batch. The groups (<c>Strings</c>, <c>Lists</c>, ...) and <c>PingAsync</c> come from
    /// the library's own extensions on <see cref="IRespKeyspaceTarget"/>, exactly as on a database.
    /// </summary>
    /// <remarks>
    /// A class, because the harness hands the same client to <see cref="PrepareBatch"/>, the operations and
    /// <see cref="Flush"/> in turn. Both contexts are captured when the batch starts and ends, so the
    /// operations never box the batch to ask it for one.
    /// </remarks>
    public sealed class Target(RespDatabaseContext db) : IRespKeyspaceTarget
    {
        private RespDatabaseContext _context = db;
        private RespContext _raw = ((IRespTarget)db).Context;
        private RespBatch _batch;

        public RespDatabaseContext Db { get; } = db;

        public bool IsBatching { get; private set; }

        public RespDatabaseContext Context => _context;

        RespContext IRespTarget.Context => _raw;

        public void BeginBatch()
        {
            _batch = Db.BeginBatch();
            IsBatching = true;
            _context = _batch.Context;
            _raw = ((IRespTarget)_context).Context;
        }

        /// <summary>Stop composing, and hand back the batch for the caller to execute.</summary>
        public RespBatch EndBatch()
        {
            var batch = _batch;
            _batch = default;
            IsBatching = false;
            _context = Db;
            _raw = ((IRespTarget)Db).Context;
            return batch;
        }
    }

    private static readonly string withVersion = $"RespDatabaseContext API, SE.Redis {DatabaseApiBenchmark.GetLibVersion()}";
    public override string ToString() => withVersion;

    private readonly ConnectionMultiplexer[] _connections;
    private readonly Target[] _clients;
    private readonly KeyValuePair<RedisKey, RedisValue>[] _pairs;

    public ContextApiBenchmark(string[] args) : base(args)
    {
        // as for the classic API: a multiplexer per client unless +m shares one
        var connectionCount = Multiplexed ? 1 : ClientCount;
        _connections = new ConnectionMultiplexer[connectionCount];
        for (var i = 0; i < connectionCount; i++)
        {
            _connections[i] = ConnectionMultiplexer.Connect($"{HostName}:{Port}");
        }

        _clients = new Target[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            _clients[i] = new Target(_connections[i % connectionCount].GetDatabase().Context);
        }

        _pairs = new KeyValuePair<RedisKey, RedisValue>[10];
        for (var i = 0; i < 10; i++)
        {
            _pairs[i] = new($"key:__rand_int__{i}", Payload);
        }
    }

    public override int ConnectionCount => _connections.Length;

    protected override Target GetClient(int index) => _clients[index];

    protected override Task DeleteAsync(Target client, string key) => client.Keys.DeleteAsync(key).AsTask();

    protected override Task InitAsync(Target client) => client.PingAsync().AsTask();

    public override void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.Dispose();
        }
    }

    protected override async Task OnCleanupAsync(Target client)
    {
        foreach (var pair in _pairs)
        {
            await client.Keys.DeleteAsync(pair.Key).ConfigureAwait(false);
        }
    }

    public override async Task RunAll()
    {
        await InitAsync().ConfigureAwait(false);
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

    // each caller batches on its own Target; the harness then drives it through PrepareBatch / Flush
    protected override Target CreateBatch(Target client) => new(client.Db);

    protected override void PrepareBatch(Target client, int count) => client.BeginBatch();

    protected override async ValueTask Flush(Target client)
    {
        if (!client.IsBatching) return;
        using var batch = client.EndBatch();
        await batch.ExecuteAsync().ConfigureAwait(false);
    }

    [DisplayName("PING_BULK")]
    private ValueTask<bool> PingBulk(Target client) => Done(client.PingAsync());

    [DisplayName("INCR")]
    private ValueTask<long> Incr(Target client) => client.Strings.IncrementAsync(CounterKey);

    [DisplayName("GET")]
    private async ValueTask<int> Get(Target client)
    {
        using var lease = await client.Strings.GetLeaseAsync(GetSetKey).ConfigureAwait(false);
        return lease?.Length ?? -1;
    }

    [DisplayName("SET")]
    private ValueTask<bool> Set(Target client) => client.Strings.SetAsync(GetSetKey, Payload);

    [DisplayName("LPUSH")]
    private ValueTask<long> LPush(Target client) => client.Lists.LeftPushAsync(ListKey, Payload);

    [DisplayName("RPUSH")]
    private ValueTask<long> RPush(Target client) => client.Lists.RightPushAsync(ListKey, Payload);

    [DisplayName("LPOP")]
    private ValueTask<RedisValue> LPop(Target client) => client.Lists.LeftPopAsync(ListKey);

    [DisplayName("RPOP")]
    private ValueTask<RedisValue> RPop(Target client) => client.Lists.RightPopAsync(ListKey);

    [DisplayName("SADD")]
    private ValueTask<bool> SAdd(Target client) => client.Sets.AddAsync(SetKey, "element:__rand_int__");

    [DisplayName("SPOP")]
    private ValueTask<RedisValue> SPop(Target client) => client.Sets.PopAsync(SetKey);

    [DisplayName("HSET")]
    private ValueTask<bool> HSet(Target client) => client.Hashes.SetAsync(HashKey, "element:__rand_int__", Payload);

    [DisplayName("ZADD")]
    private ValueTask<bool> ZAdd(Target client) => client.SortedSets.AddAsync(SortedSetKey, "element:__rand_int__", 0);

    [DisplayName("ZPOPMIN")]
    private async ValueTask<int> ZPopMin(Target client)
        => (await client.SortedSets.PopAsync(SortedSetKey).ConfigureAwait(false)).HasValue ? 1 : 0;

    [DisplayName("MSET"), Description("10 keys")]
    private ValueTask<bool> MSet(Target client) => client.Strings.SetAsync(_pairs);

    [DisplayName("XADD")]
    private ValueTask<RedisValue> XAdd(Target client) => client.Streams.AddAsync(StreamKey, "myfield", Payload);

    [DisplayName("LRANGE_100")]
    private ValueTask<int> LRange100(Target client) => Count(client, 99);

    [DisplayName("LRANGE_300")]
    private ValueTask<int> LRange300(Target client) => Count(client, 299);

    [DisplayName("LRANGE_500")]
    private ValueTask<int> LRange500(Target client) => Count(client, 499);

    [DisplayName("LRANGE_600")]
    private ValueTask<int> LRange600(Target client) => Count(client, 599);

    private async ValueTask<int> Count(Target client, long stop)
    {
        using var lease = await client.Lists.RangeAsync(ListKey, 0, stop).ConfigureAwait(false);
        return lease.Length;
    }

    private static async ValueTask<bool> Done(ValueTask pending)
    {
        await pending.ConfigureAwait(false);
        return true;
    }

    private async ValueTask LRangeInit650(Target client)
    {
        await client.Keys.DeleteAsync(ListKey).ConfigureAwait(false);
        using (var batch = client.Db.BeginBatch())
        {
            for (int i = 0; i < 650; i++)
            {
                _ = batch.Lists.LeftPushAsync(ListKey, Payload);
            }

            await batch.ExecuteAsync().ConfigureAwait(false);
        }

        if (await client.Lists.LengthAsync(ListKey).ConfigureAwait(false) != 650)
        {
            throw new InvalidOperationException();
        }
    }

    protected override async Task RunBasicLoopAsync(int clientId)
    {
        // The purpose of this is to represent a more realistic loop using natural code
        // rather than code that is drowning in test infrastructure.
        var db = GetClient(clientId).Db;
        var depth = PipelineDepth;
        int tickCount = 0; // this is just so we don't query DateTime.
        var tmp = await db.Strings.GetAsync(CounterKey).ConfigureAwait(false);
        long previousValue = tmp.IsNull ? 0 : (long)tmp, currentValue = previousValue;
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
                currentValue = await db.Strings.IncrementAsync(CounterKey).ConfigureAwait(false);

                if (++tickCount >= 1000 && Tick()) break; // only check whether to output every N iterations
            }
        }
        else
        {
            var pending = new ValueTask<long>[depth];
            while (true)
            {
                using (var batch = db.BeginBatch())
                {
                    for (int i = 0; i < depth; i++)
                    {
                        pending[i] = batch.Strings.IncrementAsync(CounterKey);
                    }

                    await batch.ExecuteAsync().ConfigureAwait(false);
                }

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
#endif
