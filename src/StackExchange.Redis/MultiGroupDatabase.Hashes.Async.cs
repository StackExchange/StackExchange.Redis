using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Hash Async
    public Task<long> HashDecrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashDecrementAsync(key, hashField, value, flags);

    public Task<double> HashDecrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashDecrementAsync(key, hashField, value, flags);

    public Task<bool> HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashDeleteAsync(key, hashField, flags);

    public Task<long> HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashDeleteAsync(key, hashFields, flags);

    public Task<bool> HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashExistsAsync(key, hashField, flags);

    public Task<ExpireResult[]> HashFieldExpireAsync(RedisKey key, RedisValue[] hashFields, TimeSpan expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldExpireAsync(key, hashFields, expiry, when, flags);

    public Task<ExpireResult[]> HashFieldExpireAsync(RedisKey key, RedisValue[] hashFields, DateTime expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldExpireAsync(key, hashFields, expiry, when, flags);

    public Task<long[]> HashFieldGetExpireDateTimeAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetExpireDateTimeAsync(key, hashFields, flags);

    public Task<PersistResult[]> HashFieldPersistAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldPersistAsync(key, hashFields, flags);

    public Task<long[]> HashFieldGetTimeToLiveAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetTimeToLiveAsync(key, hashFields, flags);

    public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashGetAsync(key, hashField, flags);

    public Task<Lease<byte>?> HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashGetLeaseAsync(key, hashField, flags);

    public Task<RedisValue[]> HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashGetAsync(key, hashFields, flags);

    public Task<RedisValue> HashFieldGetAndDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetAndDeleteAsync(key, hashField, flags);

    public Task<Lease<byte>?> HashFieldGetLeaseAndDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetLeaseAndDeleteAsync(key, hashField, flags);

    public Task<RedisValue[]> HashFieldGetAndDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetAndDeleteAsync(key, hashFields, flags);

    public Task<RedisValue> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetAndSetExpiryAsync(key, hashField, expiry, persist, flags);

    public Task<RedisValue> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetAndSetExpiryAsync(key, hashField, expiry, flags);

    public Task<Lease<byte>?> HashFieldGetLeaseAndSetExpiryAsync(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetLeaseAndSetExpiryAsync(key, hashField, expiry, persist, flags);

    public Task<Lease<byte>?> HashFieldGetLeaseAndSetExpiryAsync(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetLeaseAndSetExpiryAsync(key, hashField, expiry, flags);

    public Task<RedisValue[]> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue[] hashFields, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetAndSetExpiryAsync(key, hashFields, expiry, persist, flags);

    public Task<RedisValue[]> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue[] hashFields, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldGetAndSetExpiryAsync(key, hashFields, expiry, flags);

    public Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, RedisValue field, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldSetAndSetExpiryAsync(key, field, value, expiry, keepTtl, when, flags);

    public Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, RedisValue field, RedisValue value, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldSetAndSetExpiryAsync(key, field, value, expiry, when, flags);

    public Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, HashEntry[] hashFields, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldSetAndSetExpiryAsync(key, hashFields, expiry, keepTtl, when, flags);

    public Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, HashEntry[] hashFields, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashFieldSetAndSetExpiryAsync(key, hashFields, expiry, when, flags);

    public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashGetAllAsync(key, flags);

    public Task<long> HashIncrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashIncrementAsync(key, hashField, value, flags);

    public Task<double> HashIncrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashIncrementAsync(key, hashField, value, flags);

    public Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashKeysAsync(key, flags);

    public Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashLengthAsync(key, flags);

    public Task<RedisValue> HashRandomFieldAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashRandomFieldAsync(key, flags);

    public Task<RedisValue[]> HashRandomFieldsAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashRandomFieldsAsync(key, count, flags);

    public Task<HashEntry[]> HashRandomFieldsWithValuesAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashRandomFieldsWithValuesAsync(key, count, flags);

    public System.Collections.Generic.IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public System.Collections.Generic.IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashScanNoValuesAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public Task<bool> HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashSetAsync(key, hashField, value, when, flags);

    public Task HashSetAsync(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashSetAsync(key, hashFields, flags);

    public Task<long> HashStringLengthAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashStringLengthAsync(key, hashField, flags);

    public Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().HashValuesAsync(key, flags);
}
