using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // HyperLogLog Async
    public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogAddAsync(key, value, flags);

    public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogAddAsync(key, values, flags);

    public Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogLengthAsync(key, flags);

    public Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogLengthAsync(keys, flags);

    public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogMergeAsync(destination, first, second, flags);

    public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogMergeAsync(destination, sourceKeys, flags);
}
