using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class ProxiedDatabase
{
    // Async Lock methods
    public Task<bool> LockExtendAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan expiry,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> LockQueryAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> LockTakeAsync(
        RedisKey key,
        RedisValue value,
        TimeSpan expiry,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous Lock methods
    public bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue LockQuery(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool LockRelease(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
