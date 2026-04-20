using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // String Async operations
    public Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringAppendAsync(key, value, flags);

    public Task<long> StringBitCountAsync(RedisKey key, long start, long end, CommandFlags flags)
        => GetActiveDatabase().StringBitCountAsync(key, start, end, flags);

    public Task<long> StringBitCountAsync(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringBitCountAsync(key, start, end, indexType, flags);

    public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringBitOperationAsync(operation, destination, first, second, flags);

    public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringBitOperationAsync(operation, destination, keys, flags);

    public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start, long end, CommandFlags flags)
        => GetActiveDatabase().StringBitPositionAsync(key, bit, start, end, flags);

    public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringBitPositionAsync(key, bit, start, end, indexType, flags);

    public Task<long> StringDecrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringDecrementAsync(key, value, flags);

    public Task<double> StringDecrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringDecrementAsync(key, value, flags);

    public Task<bool> StringDeleteAsync(RedisKey key, ValueCondition when, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringDeleteAsync(key, when, flags);

    public Task<ValueCondition?> StringDigestAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringDigestAsync(key, flags);

    public Task<GcraRateLimitResult> StringGcraRateLimitAsync(RedisKey key, int maxBurst, int requestsPerPeriod, double periodSeconds = 1.0, int count = 1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGcraRateLimitAsync(key, maxBurst, requestsPerPeriod, periodSeconds, count, flags);

    public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetAsync(key, flags);

    public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetAsync(keys, flags);

    public Task<Lease<byte>?> StringGetLeaseAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetLeaseAsync(key, flags);

    public Task<bool> StringGetBitAsync(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetBitAsync(key, offset, flags);

    public Task<RedisValue> StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetRangeAsync(key, start, end, flags);

    public Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetSetAsync(key, value, flags);

    public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetSetExpiryAsync(key, expiry, flags);

    public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetSetExpiryAsync(key, expiry, flags);

    public Task<RedisValue> StringGetDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetDeleteAsync(key, flags);

    public Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringGetWithExpiryAsync(key, flags);

    public Task<long> StringIncrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringIncrementAsync(key, value, flags);

    public Task<double> StringIncrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringIncrementAsync(key, value, flags);

    public Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringLengthAsync(key, flags);

    public Task<string?> StringLongestCommonSubsequenceAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringLongestCommonSubsequenceAsync(first, second, flags);

    public Task<long> StringLongestCommonSubsequenceLengthAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringLongestCommonSubsequenceLengthAsync(first, second, flags);

    public Task<LCSMatchResult> StringLongestCommonSubsequenceWithMatchesAsync(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringLongestCommonSubsequenceWithMatchesAsync(first, second, minLength, flags);

    public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when)
        => GetActiveDatabase().StringSetAsync(key, value, expiry, when);

    public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
        => GetActiveDatabase().StringSetAsync(key, value, expiry, when, flags);

    public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, bool keepTtl, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringSetAsync(key, value, expiry, keepTtl, when, flags);

    public Task<bool> StringSetAsync(RedisKey key, RedisValue value, Expiration expiry = default, ValueCondition when = default, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringSetAsync(key, value, expiry, when, flags);

    public Task<bool> StringSetAsync(System.Collections.Generic.KeyValuePair<RedisKey, RedisValue>[] values, When when, CommandFlags flags)
        => GetActiveDatabase().StringSetAsync(values, when, flags);

    public Task<bool> StringSetAsync(System.Collections.Generic.KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, Expiration expiry = default, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringSetAsync(values, when, expiry, flags);

    public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
        => GetActiveDatabase().StringSetAndGetAsync(key, value, expiry, when, flags);

    public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringSetAndGetAsync(key, value, expiry, keepTtl, when, flags);

    public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringSetBitAsync(key, offset, bit, flags);

    public Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().StringSetRangeAsync(key, offset, value, flags);
}
