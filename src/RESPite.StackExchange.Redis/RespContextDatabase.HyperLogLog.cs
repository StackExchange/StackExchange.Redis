using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    // Async HyperLogLog methods
    public Task<bool> HyperLogLogAddAsync(
        RedisKey key,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfAdd(key, value).AsTask();

    public Task<bool> HyperLogLogAddAsync(
        RedisKey key,
        RedisValue[] values,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfAdd(key, values).AsTask();

    public Task<long> HyperLogLogLengthAsync(
        RedisKey key,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfCount(key).AsTask();

    public Task<long> HyperLogLogLengthAsync(
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfCount(keys).AsTask();

    public Task HyperLogLogMergeAsync(
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfMerge(destination, first, second).AsTask();

    public Task HyperLogLogMergeAsync(
        RedisKey destination,
        RedisKey[] sourceKeys,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfMerge(destination, sourceKeys).AsTask();

    // Synchronous HyperLogLog methods
    public bool HyperLogLogAdd(
        RedisKey key,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfAdd(key, value).Wait(SyncTimeout);

    public bool HyperLogLogAdd(
        RedisKey key,
        RedisValue[] values,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfAdd(key, values).Wait(SyncTimeout);

    public long HyperLogLogLength(
        RedisKey key,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfCount(key).Wait(SyncTimeout);

    public long HyperLogLogLength(
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfCount(keys).Wait(SyncTimeout);

    public void HyperLogLogMerge(
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfMerge(destination, first, second).Wait(SyncTimeout);

    public void HyperLogLogMerge(
        RedisKey destination,
        RedisKey[] sourceKeys,
        CommandFlags flags = CommandFlags.None) =>
        Context(flags).HyperLogLogs().PfMerge(destination, sourceKeys).Wait(SyncTimeout);
}
