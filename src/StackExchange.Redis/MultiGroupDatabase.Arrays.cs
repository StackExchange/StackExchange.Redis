namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Array operations
    public bool ArraySet(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArraySet(key, index, value, flags);

    public int ArraySet(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArraySet(key, index, values, flags);

    public int ArraySet(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArraySet(key, values, flags);

    public RedisValue ArrayGet(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayGet(key, index, flags);

    public RedisValue[] ArrayGet(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayGet(key, indices, flags);

    public RedisValue[] ArrayGetRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayGetRange(key, start, end, flags);

    public RedisArrayIndex ArrayLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayLength(key, flags);

    public RedisArrayIndex ArrayCount(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayCount(key, flags);

    public bool ArrayDelete(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayDelete(key, index, flags);

    public int ArrayDelete(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayDelete(key, indices, flags);

    public RedisArrayIndex ArrayDeleteRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayDeleteRange(key, start, end, flags);

    public RedisArrayIndex ArrayDeleteRange(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayDeleteRange(key, ranges, flags);

    public RedisArrayEntry[] ArrayScan(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int limit = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayScan(key, start, end, limit, flags);

    public RedisArrayEntry[] ArrayGrep(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayGrep(key, request, flags);

    public RedisValue ArrayOperation(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayOperation(key, start, end, operation, operand, flags);

    public RedisArrayIndex ArrayRing(RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayRing(key, maxLength, value, flags);

    public RedisArrayIndex ArrayRing(RedisKey key, RedisArrayIndex maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayRing(key, maxLength, values, flags);

    public RedisArrayIndex? ArrayNext(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayNext(key, flags);

    public RedisArrayIndex ArrayInsert(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayInsert(key, value, flags);

    public RedisArrayIndex ArrayInsert(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayInsert(key, values, flags);

    public bool ArraySeek(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArraySeek(key, index, flags);

    public RedisValue[] ArrayLastItems(RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayLastItems(key, count, reverse, flags);

    public ArrayInfo ArrayInfo(RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ArrayInfo(key, full, flags);
}
