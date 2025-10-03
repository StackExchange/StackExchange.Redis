using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    // Synchronous Set methods
    public bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SAdd(key, value).Wait(SyncTimeout);

    public long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SAdd(key, values).Wait(SyncTimeout);

    public Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SAdd(key, value).AsTask();

    public Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SAdd(key, values).AsTask();

    public RedisValue[] SetCombine(
        SetOperation operation,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().Combine(operation, first, second).Wait(SyncTimeout);

    public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().Combine(operation, keys).Wait(SyncTimeout);

    public long SetCombineAndStore(
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().CombineStore(operation, destination, first, second).Wait(SyncTimeout);

    public long SetCombineAndStore(
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().CombineStore(operation, destination, keys).Wait(SyncTimeout);

    public Task<long> SetCombineAndStoreAsync(
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().CombineStore(operation, destination, first, second).AsTask();

    public Task<long> SetCombineAndStoreAsync(
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().CombineStore(operation, destination, keys).AsTask();

    public Task<RedisValue[]> SetCombineAsync(
        SetOperation operation,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().Combine(operation, first, second).AsTask();

    public Task<RedisValue[]> SetCombineAsync(
        SetOperation operation,
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().Combine(operation, keys).AsTask();

    public bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SIsMember(key, value).Wait(SyncTimeout);

    public bool[] SetContains(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SMIsMember(key, values).Wait(SyncTimeout);

    public Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SIsMember(key, value).AsTask();

    public Task<bool[]> SetContainsAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SMIsMember(key, values).AsTask();

    public long SetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SInterCard(keys, limit).Wait(SyncTimeout);

    public Task<long> SetIntersectionLengthAsync(
        RedisKey[] keys,
        long limit = 0,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SInterCard(keys, limit).AsTask();

    public long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SCard(key).Wait(SyncTimeout);

    public Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SCard(key).AsTask();

    public RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SMembers(key).Wait(SyncTimeout);

    public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SMembers(key).AsTask();

    public bool SetMove(
        RedisKey source,
        RedisKey destination,
        RedisValue value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SMove(source, destination, value).Wait(SyncTimeout);

    public Task<bool> SetMoveAsync(
        RedisKey source,
        RedisKey destination,
        RedisValue value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SMove(source, destination, value).AsTask();

    public RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SPop(key).Wait(SyncTimeout);

    public RedisValue[] SetPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SPop(key, count).Wait(SyncTimeout);

    public Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SPop(key).AsTask();

    public Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SPop(key, count).AsTask();

    public RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SRandMember(key).Wait(SyncTimeout);

    public RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SRandMember(key, count).Wait(SyncTimeout);

    public Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SRandMember(key).AsTask();

    public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SRandMember(key, count).AsTask();

    public bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SRem(key, value).Wait(SyncTimeout);

    public long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SRem(key, values).Wait(SyncTimeout);

    public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SRem(key, value).AsTask();

    public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).Sets().SRem(key, values).AsTask();

    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags) =>
        throw new NotImplementedException();

    public IEnumerable<RedisValue> SetScan(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<RedisValue> SetScanAsync(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
