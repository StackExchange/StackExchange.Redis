namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // List operations
    public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListGetByIndex(key, index, flags);

    public long ListInsertAfter(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListInsertAfter(key, pivot, value, flags);

    public long ListInsertBefore(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListInsertBefore(key, pivot, value, flags);

    public RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPop(key, flags);

    public RedisValue[] ListLeftPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPop(key, count, flags);

    public ListPopResult ListLeftPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPop(keys, count, flags);

    public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPush(key, value, when, flags);

    public long ListLeftPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLeftPush(key, values, when, flags);

    public long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags)
        => GetActiveDatabase().ListLeftPush(key, values, flags);

    public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListLength(key, flags);

    public RedisValue ListMove(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListMove(sourceKey, destinationKey, sourceSide, destinationSide, flags);

    public long ListPosition(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListPosition(key, element, rank, maxLength, flags);

    public long[] ListPositions(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListPositions(key, element, count, rank, maxLength, flags);

    public RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRange(key, start, stop, flags);

    public long ListRemove(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRemove(key, value, count, flags);

    public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPop(key, flags);

    public RedisValue[] ListRightPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPop(key, count, flags);

    public ListPopResult ListRightPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPop(keys, count, flags);

    public RedisValue ListRightPopLeftPush(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPopLeftPush(source, destination, flags);

    public long ListRightPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPush(key, value, when, flags);

    public long ListRightPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListRightPush(key, values, when, flags);

    public long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags)
        => GetActiveDatabase().ListRightPush(key, values, flags);

    public void ListSetByIndex(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListSetByIndex(key, index, value, flags);

    public void ListTrim(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ListTrim(key, start, stop, flags);
}
