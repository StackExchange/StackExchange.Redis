namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // HyperLogLog operations
    public bool HyperLogLogAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogAdd(key, value, flags);

    public bool HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogAdd(key, values, flags);

    public long HyperLogLogLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogLength(key, flags);

    public long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogLength(keys, flags);

    public void HyperLogLogMerge(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogMerge(destination, first, second, flags);

    public void HyperLogLogMerge(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HyperLogLogMerge(destination, sourceKeys, flags);
}
