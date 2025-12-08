using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Set Async operations
    public Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetAddAsync(key, value, flags);

    public Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetAddAsync(key, values, flags);

    public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetCombineAsync(operation, first, second, flags);

    public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetCombineAsync(operation, keys, flags);

    public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetCombineAndStoreAsync(operation, destination, first, second, flags);

    public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetCombineAndStoreAsync(operation, destination, keys, flags);

    public Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetContainsAsync(key, value, flags);

    public Task<bool[]> SetContainsAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetContainsAsync(key, values, flags);

    public Task<long> SetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetIntersectionLengthAsync(keys, limit, flags);

    public Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetLengthAsync(key, flags);

    public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetMembersAsync(key, flags);

    public Task<bool> SetMoveAsync(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetMoveAsync(source, destination, value, flags);

    public Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetPopAsync(key, flags);

    public Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetPopAsync(key, count, flags);

    public Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetRandomMemberAsync(key, flags);

    public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetRandomMembersAsync(key, count, flags);

    public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetRemoveAsync(key, value, flags);

    public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetRemoveAsync(key, values, flags);

    public System.Collections.Generic.IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);
}
