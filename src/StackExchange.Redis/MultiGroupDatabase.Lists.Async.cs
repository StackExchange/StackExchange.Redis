using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // List Async operations
    public Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListGetByIndexAsync(key, index, flags);

    public Task<long> ListInsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListInsertAfterAsync(key, pivot, value, flags);

    public Task<long> ListInsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListInsertBeforeAsync(key, pivot, value, flags);

    public Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPopAsync(key, flags);

    public Task<RedisValue[]> ListLeftPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPopAsync(key, count, flags);

    public Task<ListPopResult> ListLeftPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPopAsync(keys, count, flags);

    public Task<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPushAsync(key, value, when, flags);

    public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPushAsync(key, values, when, flags);

    public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
        => GetActiveDatabase().ListLeftPushAsync(key, values, flags);

    public Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLengthAsync(key, flags);

    public Task<RedisValue> ListMoveAsync(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListMoveAsync(sourceKey, destinationKey, sourceSide, destinationSide, flags);

    public Task<long> ListPositionAsync(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListPositionAsync(key, element, rank, maxLength, flags);

    public Task<long[]> ListPositionsAsync(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListPositionsAsync(key, element, count, rank, maxLength, flags);

    public Task<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRangeAsync(key, start, stop, flags);

    public Task<long> ListRemoveAsync(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRemoveAsync(key, value, count, flags);

    public Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPopAsync(key, flags);

    public Task<RedisValue[]> ListRightPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPopAsync(key, count, flags);

    public Task<ListPopResult> ListRightPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPopAsync(keys, count, flags);

    public Task<RedisValue> ListRightPopLeftPushAsync(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPopLeftPushAsync(source, destination, flags);

    public Task<long> ListRightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPushAsync(key, value, when, flags);

    public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPushAsync(key, values, when, flags);

    public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
        => GetActiveDatabase().ListRightPushAsync(key, values, flags);

    public Task ListSetByIndexAsync(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListSetByIndexAsync(key, index, value, flags);

    public Task ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListTrimAsync(key, start, stop, flags);
}
