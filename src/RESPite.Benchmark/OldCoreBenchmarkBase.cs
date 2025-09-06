using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RESPite.Benchmark;

public abstract class OldCoreBenchmarkBase : BenchmarkBase<IDatabaseAsync>
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _client;
    private readonly KeyValuePair<RedisKey, RedisValue>[] _pairs;

    public OldCoreBenchmarkBase(string[] args) : base(args)
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        _connectionMultiplexer = Create(Port);
        _client = _connectionMultiplexer.GetDatabase();
        _pairs = new KeyValuePair<RedisKey, RedisValue>[10];

        for (var i = 0; i < 10; i++)
        {
            _pairs[i] = new($"{"key:__rand_int__"}{i}", Payload);
        }
    }

    protected abstract IConnectionMultiplexer Create(int port);

    protected override async Task OnCleanupAsync(IDatabaseAsync client)
    {
        foreach (var pair in _pairs)
        {
            await client.KeyDeleteAsync(pair.Key);
        }
    }

    protected override Task InitAsync(IDatabaseAsync client) => client.PingAsync();

    public override void Dispose()
    {
        _connectionMultiplexer.Dispose();
    }

    protected override IDatabaseAsync GetClient(int index) => _client;
    protected override Task DeleteAsync(IDatabaseAsync client, string key) => client.KeyDeleteAsync(key);

    public override async Task RunAll()
    {
        await InitAsync().ConfigureAwait(false);
        // await RunAsync(PingInline).ConfigureAwait(false);
        await RunAsync(null, PingBulk).ConfigureAwait(false);

        await RunAsync(GetSetKey, Set).ConfigureAwait(false);
        await RunAsync(GetSetKey, Get, GetInit).ConfigureAwait(false);
        await RunAsync(CounterKey, Incr).ConfigureAwait(false);
        await RunAsync(ListKey, LPush).ConfigureAwait(false);
        await RunAsync(ListKey, RPush).ConfigureAwait(false);
        await RunAsync(ListKey, LPop, LPopInit).ConfigureAwait(false);
        await RunAsync(ListKey, RPop, LPopInit).ConfigureAwait(false);
        await RunAsync(SetKey, SAdd).ConfigureAwait(false);
        await RunAsync(HashKey, HSet).ConfigureAwait(false);
        await RunAsync(SetKey, SPop, SPopInit).ConfigureAwait(false);
        await RunAsync(SortedSetKey, ZAdd).ConfigureAwait(false);
        await RunAsync(SortedSetKey, ZPopMin, ZPopMinInit).ConfigureAwait(false);
        await RunAsync(null, MSet).ConfigureAwait(false);
        await RunAsync(StreamKey, XAdd).ConfigureAwait(false);

        // leave until last, they're slower
        await RunAsync(ListKey, LRange100, LRangeInit).ConfigureAwait(false);
        await RunAsync(ListKey, LRange300, LRangeInit).ConfigureAwait(false);
        await RunAsync(ListKey, LRange500, LRangeInit).ConfigureAwait(false);
        await RunAsync(ListKey, LRange600, LRangeInit).ConfigureAwait(false);

        await CleanupAsync().ConfigureAwait(false);
    }

    protected override IDatabaseAsync CreateBatch(IDatabaseAsync client) => ((IDatabase)client).CreateBatch();

    protected override ValueTask Flush(IDatabaseAsync client)
    {
        if (client is IBatch batch)
        {
            batch.Execute();
        }

        return default;
    }

    protected override async Task RunBasicLoopAsync(int clientId)
    {
        // The purpose of this is to represent a more realistic loop using natural code
        // rather than code that is drowning in test infrastructure.
        var client = (IDatabase)GetClient(clientId); // need IDatabase for CreateBatch
        var depth = PipelineDepth;
        int tickCount = 0; // this is just so we don't query DateTime.
        var tmp = await client.StringGetAsync(CounterKey).ConfigureAwait(false);
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
                currentValue = await client.StringIncrementAsync(CounterKey).ConfigureAwait(false);

                if (++tickCount >= 1000 && Tick()) break; // only check whether to output every N iterations
            }
        }
        else
        {
            Task<long>[] pending = new Task<long>[depth];
            var batch = client.CreateBatch(depth);
            while (true)
            {
                for (int i = 0; i < depth; i++)
                {
                    pending[i] = batch.StringIncrementAsync(CounterKey);
                }

                batch.Execute();
                for (int i = 0; i < depth; i++)
                {
                    currentValue = await pending[i].ConfigureAwait(false);
                }

                tickCount += depth;
                if (tickCount >= 1000 && Tick()) break; // only check whether to output every N iterations
            }
        }
    }

    [DisplayName("GET")]
    private ValueTask<int> Get(IDatabaseAsync client) => GetAndMeasureString(client);

    private async ValueTask<int> GetAndMeasureString(IDatabaseAsync client)
    {
        using var lease = await client.StringGetLeaseAsync(GetSetKey).ConfigureAwait(false);
        return lease?.Length ?? -1;
    }

    [DisplayName("SET")]
    private ValueTask<bool> Set(IDatabaseAsync client) => client.StringSetAsync(GetSetKey, Payload).AsValueTask();

    private ValueTask GetInit(IDatabaseAsync client) =>
        client.StringSetAsync(GetSetKey, Payload).AsUntypedValueTask();

    private ValueTask<TimeSpan> PingInline(IDatabaseAsync client) => client.PingAsync().AsValueTask();

    [DisplayName("PING_BULK")]
    private ValueTask<TimeSpan> PingBulk(IDatabaseAsync client) => client.PingAsync().AsValueTask();

    [DisplayName("INCR")]
    private ValueTask<long> Incr(IDatabaseAsync client) => client.StringIncrementAsync(CounterKey).AsValueTask();

    [DisplayName("HSET")]
    private ValueTask<bool> HSet(IDatabaseAsync client) =>
        client.HashSetAsync(HashKey, "element:__rand_int__", Payload).AsValueTask();

    [DisplayName("SADD")]
    private ValueTask<bool> SAdd(IDatabaseAsync client) =>
        client.SetAddAsync(SetKey, "element:__rand_int__").AsValueTask();

    [DisplayName("LPUSH")]
    private ValueTask<long> LPush(IDatabaseAsync client) => client.ListLeftPushAsync(ListKey, Payload).AsValueTask();

    [DisplayName("RPUSH")]
    private ValueTask<long> RPush(IDatabaseAsync client) => client.ListRightPushAsync(ListKey, Payload).AsValueTask();

    [DisplayName("LPOP")]
    private ValueTask<RedisValue> LPop(IDatabaseAsync client) => client.ListLeftPopAsync(ListKey).AsValueTask();

    [DisplayName("RPOP")]
    private ValueTask<RedisValue> RPop(IDatabaseAsync client) => client.ListRightPopAsync(ListKey).AsValueTask();

    private ValueTask LPopInit(IDatabaseAsync client) =>
        client.ListLeftPushAsync(ListKey, Payload).AsUntypedValueTask();

    [DisplayName("SPOP")]
    private ValueTask<RedisValue> SPop(IDatabaseAsync client) => client.SetPopAsync(SetKey).AsValueTask();

    private ValueTask SPopInit(IDatabaseAsync client) =>
        client.SetAddAsync(SetKey, "element:__rand_int__").AsUntypedValueTask();

    [DisplayName("ZADD")]
    private ValueTask<bool> ZAdd(IDatabaseAsync client) =>
        client.SortedSetAddAsync(SortedSetKey, "element:__rand_int__", 0).AsValueTask();

    [DisplayName("ZPOPMIN")]
    private ValueTask<int> ZPopMin(IDatabaseAsync client) => CountAsync(client.SortedSetPopAsync(SortedSetKey, 1));

    private async ValueTask ZPopMinInit(IDatabaseAsync client)
    {
        int ops = TotalOperations;
        var rand = new Random();
        for (int i = 0; i < ops; i++)
        {
            await client.SortedSetAddAsync(SortedSetKey, "element:__rand_int__", (rand.NextDouble() * 2000) - 1000)
                .ConfigureAwait(false);
        }
    }

    [DisplayName("MSET")]
    private ValueTask<bool> MSet(IDatabaseAsync client) => client.StringSetAsync(_pairs).AsValueTask();

    [DisplayName("XADD")]
    private ValueTask<RedisValue> XAdd(IDatabaseAsync client) =>
        client.StreamAddAsync(StreamKey, "myfield", Payload).AsValueTask();

    [DisplayName("LRANGE_100")]
    private ValueTask<int> LRange100(IDatabaseAsync client) => CountAsync(client.ListRangeAsync(ListKey, 0, 99));

    [DisplayName("LRANGE_300")]
    private ValueTask<int> LRange300(IDatabaseAsync client) => CountAsync(client.ListRangeAsync(ListKey, 0, 299));

    [DisplayName("LRANGE_500")]
    private ValueTask<int> LRange500(IDatabaseAsync client) => CountAsync(client.ListRangeAsync(ListKey, 0, 499));

    [DisplayName("LRANGE_600")]
    private ValueTask<int> LRange600(IDatabaseAsync client) =>
        CountAsync(client.ListRangeAsync(ListKey, 0, 599));

    private static ValueTask<int> CountAsync<T>(Task<T[]> task) => task.ContinueWith(
        t => t.Result.Length, TaskContinuationOptions.ExecuteSynchronously).AsValueTask();

    private async ValueTask LRangeInit(IDatabaseAsync client)
    {
        var ops = TotalOperations;
        for (int i = 0; i < ops; i++)
        {
            await client.ListLeftPushAsync(ListKey, Payload);
        }
    }
}

internal static class TaskExtensions
{
    public static ValueTask<T> AsValueTask<T>(this Task<T> task) => new(task);
    public static ValueTask AsUntypedValueTask(this Task task) => new(task);
    public static ValueTask AsValueTask<T>(this Task task) => new(task);

    public static ValueTask AsUntypedValueTask<T>(this ValueTask<T> task)
    {
        if (!task.IsCompleted) return Awaited(task);
        task.GetAwaiter().GetResult();
        return default;

        static async ValueTask Awaited(ValueTask<T> task)
        {
            await task.ConfigureAwait(false);
        }
    }
}
