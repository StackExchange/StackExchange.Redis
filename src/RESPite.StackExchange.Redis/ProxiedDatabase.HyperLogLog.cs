using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed partial class ProxiedDatabase
{
    // Async HyperLogLog methods
    public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task HyperLogLogMergeAsync(
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task HyperLogLogMergeAsync(
        RedisKey destination,
        RedisKey[] sourceKeys,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous HyperLogLog methods
    public bool HyperLogLogAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long HyperLogLogLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public void HyperLogLogMerge(
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public void HyperLogLogMerge(
        RedisKey destination,
        RedisKey[] sourceKeys,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
