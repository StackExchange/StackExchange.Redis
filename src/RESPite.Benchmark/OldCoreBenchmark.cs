using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RESPite.Benchmark;

public class OldCoreBenchmark : BenchmarkBase<IDatabaseAsync>
{
    public override string ToString() => "legacy SE.Redis";

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _client;
    private readonly KeyValuePair<RedisKey, RedisValue>[] _pairs;

    public OldCoreBenchmark(string[] args) : base(args)
    {
        _connectionMultiplexer = ConnectionMultiplexer.Connect($"127.0.0.1:{Port}");
        _client = _connectionMultiplexer.GetDatabase();
        _pairs = new KeyValuePair<RedisKey, RedisValue>[10];

        for (var i = 0; i < 10; i++)
        {
            _pairs[i] = new($"{"key:__rand_int__"}{i}", _payload);
        }
    }

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
    protected override Task Delete(IDatabaseAsync client, string key) => client.KeyDeleteAsync(key);

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

    protected override IDatabaseAsync CreateBatch(IDatabaseAsync client) => ((IDatabase)client).CreateBatch();

    protected override ValueTask Flush(IDatabaseAsync client)
    {
        if (client is IBatch batch)
        {
            batch.Execute();
        }

        return default;
    }

    [DisplayName("GET")]
    private ValueTask<int> Get(IDatabaseAsync client) => GetAndMeasureString(client);

    private async ValueTask<int> GetAndMeasureString(IDatabaseAsync client)
    {
        using var lease = await client.StringGetLeaseAsync(_getSetKey).ConfigureAwait(false);
        return lease?.Length ?? -1;
    }

    [DisplayName("SET")]
    private ValueTask<bool> Set(IDatabaseAsync client) => client.StringSetAsync(_getSetKey, _payload).AsValueTask();

    private ValueTask GetInit(IDatabaseAsync client) =>
        client.StringSetAsync(_getSetKey, _payload).AsUntypedValueTask();

    private ValueTask<TimeSpan> PingInline(IDatabaseAsync client) => client.PingAsync().AsValueTask();

    [DisplayName("PING_BULK")]
    private ValueTask<TimeSpan> PingBulk(IDatabaseAsync client) => client.PingAsync().AsValueTask();

    [DisplayName("INCR")]
    private ValueTask<long> Incr(IDatabaseAsync client) => client.StringIncrementAsync(_counterKey).AsValueTask();

    [DisplayName("HSET")]
    private ValueTask<bool> HSet(IDatabaseAsync client) =>
        client.HashSetAsync(_hashKey, "element:__rand_int__", _payload).AsValueTask();

    [DisplayName("SADD")]
    private ValueTask<bool> SAdd(IDatabaseAsync client) =>
        client.SetAddAsync(_setKey, "element:__rand_int__").AsValueTask();

    [DisplayName("LPUSH")]
    private ValueTask<long> LPush(IDatabaseAsync client) => client.ListLeftPushAsync(_listKey, _payload).AsValueTask();

    [DisplayName("RPUSH")]
    private ValueTask<long> RPush(IDatabaseAsync client) => client.ListRightPushAsync(_listKey, _payload).AsValueTask();

    [DisplayName("LPOP")]
    private ValueTask<RedisValue> LPop(IDatabaseAsync client) => client.ListLeftPopAsync(_listKey).AsValueTask();

    [DisplayName("RPOP")]
    private ValueTask<RedisValue> RPop(IDatabaseAsync client) => client.ListRightPopAsync(_listKey).AsValueTask();

    private ValueTask LPopInit(IDatabaseAsync client) =>
        client.ListLeftPushAsync(_listKey, _payload).AsUntypedValueTask();

    [DisplayName("SPOP")]
    private ValueTask<RedisValue> SPop(IDatabaseAsync client) => client.SetPopAsync(_setKey).AsValueTask();

    private ValueTask SPopInit(IDatabaseAsync client) =>
        client.SetAddAsync(_setKey, "element:__rand_int__").AsUntypedValueTask();

    [DisplayName("ZADD")]
    private ValueTask<bool> ZAdd(IDatabaseAsync client) =>
        client.SortedSetAddAsync(_sortedSetKey, "element:__rand_int__", 0).AsValueTask();

    [DisplayName("ZPOPMIN")]
    private ValueTask<int> ZPopMin(IDatabaseAsync client) => CountAsync(client.SortedSetPopAsync(_sortedSetKey, 1));

    private async ValueTask ZPopMinInit(IDatabaseAsync client)
    {
        int ops = TotalOperations;
        var rand = new Random();
        for (int i = 0; i < ops; i++)
        {
            await client.SortedSetAddAsync(_sortedSetKey, "element:__rand_int__", (rand.NextDouble() * 2000) - 1000)
                .ConfigureAwait(false);
        }
    }

    [DisplayName("MSET")]
    private ValueTask<bool> MSet(IDatabaseAsync client) => client.StringSetAsync(_pairs).AsValueTask();

    [DisplayName("XADD")]
    private ValueTask<RedisValue> XAdd(IDatabaseAsync client) =>
        client.StreamAddAsync(_streamKey, "myfield", _payload).AsValueTask();

    [DisplayName("LRANGE_100")]
    private ValueTask<int> LRange100(IDatabaseAsync client) => CountAsync(client.ListRangeAsync(_listKey, 0, 99));

    [DisplayName("LRANGE_300")]
    private ValueTask<int> LRange300(IDatabaseAsync client) => CountAsync(client.ListRangeAsync(_listKey, 0, 299));

    [DisplayName("LRANGE_500")]
    private ValueTask<int> LRange500(IDatabaseAsync client) => CountAsync(client.ListRangeAsync(_listKey, 0, 499));

    [DisplayName("LRANGE_600")]
    private ValueTask<int> LRange600(IDatabaseAsync client) =>
        CountAsync(client.ListRangeAsync(_listKey, 0, 599));

    private static ValueTask<int> CountAsync<T>(Task<T[]> task) => task.ContinueWith(
        t => t.Result.Length, TaskContinuationOptions.ExecuteSynchronously).AsValueTask();

    private async ValueTask LRangeInit(IDatabaseAsync client)
    {
        var ops = TotalOperations;
        for (int i = 0; i < ops; i++)
        {
            await client.ListLeftPushAsync(_listKey, _payload);
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
