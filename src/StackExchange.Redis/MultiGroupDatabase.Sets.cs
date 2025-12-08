namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Set operations
    public bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetAdd(key, value, flags);

    public long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetAdd(key, values, flags);

    public RedisValue[] SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetCombine(operation, first, second, flags);

    public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetCombine(operation, keys, flags);

    public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetCombineAndStore(operation, destination, first, second, flags);

    public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetCombineAndStore(operation, destination, keys, flags);

    public bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetContains(key, value, flags);

    public bool[] SetContains(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetContains(key, values, flags);

    public long SetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetIntersectionLength(keys, limit, flags);

    public long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetLength(key, flags);

    public RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetMembers(key, flags);

    public bool SetMove(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetMove(source, destination, value, flags);

    public RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetPop(key, flags);

    public RedisValue[] SetPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetPop(key, count, flags);

    public RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetRandomMember(key, flags);

    public RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetRandomMembers(key, count, flags);

    public bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetRemove(key, value, flags);

    public long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetRemove(key, values, flags);

    public System.Collections.Generic.IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetDatabase().SetScan(key, pattern, pageSize, flags);

    public System.Collections.Generic.IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetScan(key, pattern, pageSize, cursor, pageOffset, flags);
}
