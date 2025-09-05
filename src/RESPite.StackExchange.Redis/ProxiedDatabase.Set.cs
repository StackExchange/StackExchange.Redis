using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed partial class ProxiedDatabase
{
    // Async Set methods
    public Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SetCombineAsync(
        SetOperation operation,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SetCombineAsync(
        SetOperation operation,
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SetCombineAndStoreAsync(
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SetCombineAndStoreAsync(
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool[]> SetContainsAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SetIntersectionLengthAsync(
        RedisKey[] keys,
        long limit = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> SetMoveAsync(
        RedisKey source,
        RedisKey destination,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<RedisValue> SetScanAsync(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous Set methods
    public bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SetCombine(
        SetOperation operation,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SetCombineAndStore(
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SetCombineAndStore(
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool[] SetContains(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool SetMove(
        RedisKey source,
        RedisKey destination,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SetPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

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
}
