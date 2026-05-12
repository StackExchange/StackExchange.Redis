// ReSharper disable once CheckNamespace
namespace StackExchange.Redis.KeyspaceIsolation;

internal sealed partial class KeyPrefixedDatabase
{
    public bool ArraySet(RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        Inner.ArraySet(ToInner(key), index, value, flags);

    public int ArraySet(RedisKey key, RedisArrayIndex index, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        Inner.ArraySet(ToInner(key), index, values, flags);

    public int ArraySet(RedisKey key, RedisArrayEntry[] values, CommandFlags flags = CommandFlags.None) =>
        Inner.ArraySet(ToInner(key), values, flags);

    public RedisValue ArrayGet(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayGet(ToInner(key), index, flags);

    public RedisValue[] ArrayGet(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayGet(ToInner(key), indices, flags);

    public RedisValue[] ArrayGetRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayGetRange(ToInner(key), start, end, flags);

    public RedisArrayIndex ArrayLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayLength(ToInner(key), flags);

    public RedisArrayIndex ArrayCount(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayCount(ToInner(key), flags);

    public bool ArrayDelete(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayDelete(ToInner(key), index, flags);

    public int ArrayDelete(RedisKey key, RedisArrayIndex[] indices, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayDelete(ToInner(key), indices, flags);

    public RedisArrayIndex ArrayDeleteRange(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayDeleteRange(ToInner(key), start, end, flags);

    public RedisArrayIndex ArrayDeleteRange(RedisKey key, RedisArrayRange[] ranges, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayDeleteRange(ToInner(key), ranges, flags);

    public RedisArrayEntry[] ArrayScan(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int limit = 0, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayScan(ToInner(key), start, end, limit, flags);

    public RedisArrayEntry[] ArrayGrep(RedisKey key, ArrayGrepRequest request, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayGrep(ToInner(key), request, flags);

    public RedisValue ArrayOperation(RedisKey key, RedisArrayIndex start, RedisArrayIndex end, ArrayOperation operation, RedisValue operand = default, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayOperation(ToInner(key), start, end, operation, operand, flags);

    public RedisArrayIndex ArrayRing(RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayRing(ToInner(key), maxLength, value, flags);

    public RedisArrayIndex ArrayRing(RedisKey key, RedisArrayIndex maxLength, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayRing(ToInner(key), maxLength, values, flags);

    public RedisArrayIndex? ArrayNext(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayNext(ToInner(key), flags);

    public RedisArrayIndex ArrayInsert(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayInsert(ToInner(key), value, flags);

    public RedisArrayIndex ArrayInsert(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayInsert(ToInner(key), values, flags);

    public bool ArraySeek(RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None) =>
        Inner.ArraySeek(ToInner(key), index, flags);

    public RedisValue[] ArrayLastItems(RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayLastItems(ToInner(key), count, reverse, flags);

    public ArrayInfo ArrayInfo(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.ArrayInfo(ToInner(key), flags);
}
