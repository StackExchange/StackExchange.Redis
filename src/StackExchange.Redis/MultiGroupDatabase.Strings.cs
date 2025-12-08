using System;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // String operations
    public long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringAppend(key, value, flags);

    public long StringBitCount(RedisKey key, long start, long end, CommandFlags flags)
        => GetDatabase().StringBitCount(key, start, end, flags);

    public long StringBitCount(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringBitCount(key, start, end, indexType, flags);

    public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringBitOperation(operation, destination, first, second, flags);

    public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringBitOperation(operation, destination, keys, flags);

    public long StringBitPosition(RedisKey key, bool bit, long start, long end, CommandFlags flags)
        => GetDatabase().StringBitPosition(key, bit, start, end, flags);

    public long StringBitPosition(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringBitPosition(key, bit, start, end, indexType, flags);

    public long StringDecrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringDecrement(key, value, flags);

    public double StringDecrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringDecrement(key, value, flags);

    public bool StringDelete(RedisKey key, ValueCondition when, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringDelete(key, when, flags);

    public ValueCondition? StringDigest(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringDigest(key, flags);

    public RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGet(key, flags);

    public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGet(keys, flags);

    public Lease<byte>? StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGetLease(key, flags);

    public bool StringGetBit(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGetBit(key, offset, flags);

    public RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGetRange(key, start, end, flags);

    public RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGetSet(key, value, flags);

    public RedisValue StringGetSetExpiry(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGetSetExpiry(key, expiry, flags);

    public RedisValue StringGetSetExpiry(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGetSetExpiry(key, expiry, flags);

    public RedisValue StringGetDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGetDelete(key, flags);

    public RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringGetWithExpiry(key, flags);

    public long StringIncrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringIncrement(key, value, flags);

    public double StringIncrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringIncrement(key, value, flags);

    public long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringLength(key, flags);

    public string? StringLongestCommonSubsequence(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringLongestCommonSubsequence(first, second, flags);

    public long StringLongestCommonSubsequenceLength(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringLongestCommonSubsequenceLength(first, second, flags);

    public LCSMatchResult StringLongestCommonSubsequenceWithMatches(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringLongestCommonSubsequenceWithMatches(first, second, minLength, flags);

    public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when)
        => GetDatabase().StringSet(key, value, expiry, when);

    public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
        => GetDatabase().StringSet(key, value, expiry, when, flags);

    public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, bool keepTtl, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringSet(key, value, expiry, keepTtl, when, flags);

    public bool StringSet(RedisKey key, RedisValue value, Expiration expiry = default, ValueCondition when = default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringSet(key, value, expiry, when, flags);

    public bool StringSet(System.Collections.Generic.KeyValuePair<RedisKey, RedisValue>[] values, When when, CommandFlags flags)
        => GetDatabase().StringSet(values, when, flags);

    public bool StringSet(System.Collections.Generic.KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, Expiration expiry = default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringSet(values, when, expiry, flags);

    public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
        => GetDatabase().StringSetAndGet(key, value, expiry, when, flags);

    public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringSetAndGet(key, value, expiry, keepTtl, when, flags);

    public bool StringSetBit(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringSetBit(key, offset, bit, flags);

    public RedisValue StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().StringSetRange(key, offset, value, flags);
}
