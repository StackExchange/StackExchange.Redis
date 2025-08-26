using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace BasicTest;

public class OldCoreBenchmark : BenchmarkBase<IDatabase>
{
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

    protected override async Task OnCleanupAsync(IDatabase client)
    {
        foreach (var pair in _pairs)
        {
            await client.KeyDeleteAsync(pair.Key);
        }
    }

    protected override Task InitAsync(IDatabase client) => client.PingAsync();

    public override void Dispose()
    {
        _connectionMultiplexer.Dispose();
    }

    protected override IDatabase GetClient(int index) => _client;
    protected override Task Delete(IDatabase client, string key) => client.KeyDeleteAsync(key);

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

    private Task<int> Get(IDatabase client) => Pipeline(() => GetAndMeasureString(client));

    private async Task<int> GetAndMeasureString(IDatabase client)
    {
        using var lease = await client.StringGetLeaseAsync(_getSetKey).ConfigureAwait(false);
        return lease?.Length ?? -1;
    }

    private Task<bool> Set(IDatabase client) => Pipeline(() => client.StringSetAsync(_getSetKey, _payload));
    private Task GetInit(IDatabase client) => client.StringSetAsync(_getSetKey, _payload);

    private Task PingInline(IDatabase client) => client.PingAsync();

    private Task<TimeSpan> PingBulk(IDatabase client) => Pipeline(() => client.PingAsync());

    private Task<long> Incr(IDatabase client) => Pipeline(() => client.StringIncrementAsync(_counterKey));

    private Task<bool> HSet(IDatabase client) =>
        Pipeline(() => client.HashSetAsync(_hashKey, "element:__rand_int__", _payload));
    private Task<bool> SAdd(IDatabase client) =>
        Pipeline(() => client.SetAddAsync(_setKey, "element:__rand_int__"));
    private Task<long> LPush(IDatabase client) => Pipeline(() => client.ListLeftPushAsync(_listKey, _payload));
    private Task<long> RPush(IDatabase client) => Pipeline(() => client.ListRightPushAsync(_listKey, _payload));
    private Task<RedisValue> LPop(IDatabase client) => Pipeline(() => client.ListLeftPopAsync(_listKey));
    private Task<RedisValue> RPop(IDatabase client) => Pipeline(() => client.ListRightPopAsync(_listKey));
    private Task LPopInit(IDatabase client) => client.ListLeftPushAsync(_listKey, _payload);
    private Task<RedisValue> SPop(IDatabase client) => Pipeline(() => client.SetPopAsync(_setKey));
    private Task SPopInit(IDatabase client) => client.SetAddAsync(_setKey, "element:__rand_int__");
    private Task<bool> ZAdd(IDatabase client) =>
        Pipeline(() => client.SortedSetAddAsync(_sortedSetKey, "element:__rand_int__", 0));
    private Task<int> ZPopMin(IDatabase client) => Pipeline(() => CountAsync(client.SortedSetPopAsync(_sortedSetKey, 1)));
    private Task ZPopMinInit(IDatabase client) => client.SortedSetAddAsync(_sortedSetKey, "element:__rand_int__", 0);

    private Task<bool> MSet(IDatabase client) => Pipeline(() => client.StringSetAsync(_pairs));
    private Task<RedisValue> XAdd(IDatabase client) =>
        Pipeline(() => client.StreamAddAsync(_streamKey, "myfield", _payload));
    [DisplayName("LRANGE_100")]
    private Task<int> LRange100(IDatabase client) => Pipeline(() => CountAsync(client.ListRangeAsync(_listKey, 0, 99)));
    [DisplayName("LRANGE_300")]
    private Task<int> LRange300(IDatabase client) => Pipeline(() => CountAsync(client.ListRangeAsync(_listKey, 0, 299)));
    [DisplayName("LRANGE_500")]
    private Task<int> LRange500(IDatabase client) => Pipeline(() => CountAsync(client.ListRangeAsync(_listKey, 0, 499)));
    [DisplayName("LRANGE_600")]
    private Task<int> LRange600(IDatabase client) => Pipeline(() => CountAsync(client.ListRangeAsync(_listKey, 0, 599)));

    private static Task<int> CountAsync<T>(Task<T[]> task) => task.ContinueWith(t => t.Result.Length, TaskContinuationOptions.ExecuteSynchronously);
    private async Task LRangeInit(IDatabase client)
    {
        var ops = TotalOperations;
        for (int i = 0; i < ops; i++)
        {
            await client.ListLeftPushAsync(_listKey, _payload);
        }
    }
}
