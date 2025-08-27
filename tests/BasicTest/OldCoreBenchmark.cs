using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace BasicTest;

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

    protected override Func<ValueTask> GetFlush(IDatabaseAsync client)
    {
        if (client is IBatch batch)
        {
            return () =>
            {
                batch.Execute();
                return default;
            };
        }
        return GetFlush(client);
    }

    private Task<int> Get(IDatabaseAsync client, Func<ValueTask> flush) => Pipeline(() => GetAndMeasureString(client), flush);

    private async Task<int> GetAndMeasureString(IDatabaseAsync client)
    {
        using var lease = await client.StringGetLeaseAsync(_getSetKey).ConfigureAwait(false);
        return lease?.Length ?? -1;
    }

    private Task<bool> Set(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.StringSetAsync(_getSetKey, _payload), flush);

    private Task GetInit(IDatabaseAsync client) => client.StringSetAsync(_getSetKey, _payload);

    private Task PingInline(IDatabaseAsync client, Func<ValueTask> flush) => Pipeline(() => client.PingAsync(), flush);

    private Task<TimeSpan> PingBulk(IDatabaseAsync client, Func<ValueTask> flush) => Pipeline(() => client.PingAsync(), flush);

    private Task<long> Incr(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.StringIncrementAsync(_counterKey), flush);

    private Task<bool> HSet(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.HashSetAsync(_hashKey, "element:__rand_int__", _payload), flush);

    private Task<bool> SAdd(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.SetAddAsync(_setKey, "element:__rand_int__"), flush);

    private Task<long> LPush(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.ListLeftPushAsync(_listKey, _payload), flush);

    private Task<long> RPush(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.ListRightPushAsync(_listKey, _payload), flush);

    private Task<RedisValue> LPop(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.ListLeftPopAsync(_listKey), flush);

    private Task<RedisValue> RPop(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.ListRightPopAsync(_listKey), flush);

    private Task LPopInit(IDatabaseAsync client) => client.ListLeftPushAsync(_listKey, _payload);
    private Task<RedisValue> SPop(IDatabaseAsync client, Func<ValueTask> flush) => Pipeline(() => client.SetPopAsync(_setKey), flush);
    private Task SPopInit(IDatabaseAsync client) => client.SetAddAsync(_setKey, "element:__rand_int__");

    private Task<bool> ZAdd(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.SortedSetAddAsync(_sortedSetKey, "element:__rand_int__", 0), flush);

    private Task<int> ZPopMin(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => CountAsync(client.SortedSetPopAsync(_sortedSetKey, 1)), flush);

    private Task ZPopMinInit(IDatabaseAsync client) => client.SortedSetAddAsync(_sortedSetKey, "element:__rand_int__", 0);

    private Task<bool> MSet(IDatabaseAsync client, Func<ValueTask> flush) => Pipeline(() => client.StringSetAsync(_pairs), flush);

    private Task<RedisValue> XAdd(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => client.StreamAddAsync(_streamKey, "myfield", _payload), flush);

    [DisplayName("LRANGE_100")]
    private Task<int> LRange100(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => CountAsync(client.ListRangeAsync(_listKey, 0, 99)), flush);

    [DisplayName("LRANGE_300")]
    private Task<int> LRange300(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => CountAsync(client.ListRangeAsync(_listKey, 0, 299)), flush);

    [DisplayName("LRANGE_500")]
    private Task<int> LRange500(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => CountAsync(client.ListRangeAsync(_listKey, 0, 499)), flush);

    [DisplayName("LRANGE_600")]
    private Task<int> LRange600(IDatabaseAsync client, Func<ValueTask> flush) =>
        Pipeline(() => CountAsync(client.ListRangeAsync(_listKey, 0, 599)), flush);

    private static Task<int> CountAsync<T>(Task<T[]> task) =>
        task.ContinueWith(t => t.Result.Length, TaskContinuationOptions.ExecuteSynchronously);

    private async Task LRangeInit(IDatabaseAsync client)
    {
        var ops = TotalOperations;
        for (int i = 0; i < ops; i++)
        {
            await client.ListLeftPushAsync(_listKey, _payload);
        }
    }
}
