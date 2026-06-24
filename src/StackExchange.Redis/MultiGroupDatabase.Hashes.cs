using System;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Hash operations
    public long HashDecrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashDecrement(key, hashField, value, flags);

    public double HashDecrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashDecrement(key, hashField, value, flags);

    public bool HashDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashDelete(key, hashField, flags);

    public long HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashDelete(key, hashFields, flags);

    public bool HashExists(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashExists(key, hashField, flags);

    public ExpireResult[] HashFieldExpire(RedisKey key, RedisValue[] hashFields, TimeSpan expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldExpire(key, hashFields, expiry, when, flags);

    public ExpireResult[] HashFieldExpire(RedisKey key, RedisValue[] hashFields, DateTime expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldExpire(key, hashFields, expiry, when, flags);

    public long[] HashFieldGetExpireDateTime(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetExpireDateTime(key, hashFields, flags);

    public PersistResult[] HashFieldPersist(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldPersist(key, hashFields, flags);

    public long[] HashFieldGetTimeToLive(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetTimeToLive(key, hashFields, flags);

    public RedisValue HashGet(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashGet(key, hashField, flags);

    public Lease<byte>? HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashGetLease(key, hashField, flags);

    public RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashGet(key, hashFields, flags);

    public RedisValue HashFieldGetAndDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetAndDelete(key, hashField, flags);

    public Lease<byte>? HashFieldGetLeaseAndDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetLeaseAndDelete(key, hashField, flags);

    public RedisValue[] HashFieldGetAndDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetAndDelete(key, hashFields, flags);

    public RedisValue HashFieldGetAndSetExpiry(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetAndSetExpiry(key, hashField, expiry, persist, flags);

    public RedisValue HashFieldGetAndSetExpiry(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetAndSetExpiry(key, hashField, expiry, flags);

    public Lease<byte>? HashFieldGetLeaseAndSetExpiry(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetLeaseAndSetExpiry(key, hashField, expiry, persist, flags);

    public Lease<byte>? HashFieldGetLeaseAndSetExpiry(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetLeaseAndSetExpiry(key, hashField, expiry, flags);

    public RedisValue[] HashFieldGetAndSetExpiry(RedisKey key, RedisValue[] hashFields, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetAndSetExpiry(key, hashFields, expiry, persist, flags);

    public RedisValue[] HashFieldGetAndSetExpiry(RedisKey key, RedisValue[] hashFields, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldGetAndSetExpiry(key, hashFields, expiry, flags);

    public RedisValue HashFieldSetAndSetExpiry(RedisKey key, RedisValue field, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldSetAndSetExpiry(key, field, value, expiry, keepTtl, when, flags);

    public RedisValue HashFieldSetAndSetExpiry(RedisKey key, RedisValue field, RedisValue value, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldSetAndSetExpiry(key, field, value, expiry, when, flags);

    public RedisValue HashFieldSetAndSetExpiry(RedisKey key, HashEntry[] hashFields, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldSetAndSetExpiry(key, hashFields, expiry, keepTtl, when, flags);

    public RedisValue HashFieldSetAndSetExpiry(RedisKey key, HashEntry[] hashFields, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashFieldSetAndSetExpiry(key, hashFields, expiry, when, flags);

    public HashEntry[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashGetAll(key, flags);

    public long HashIncrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashIncrement(key, hashField, value, flags);

    public double HashIncrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashIncrement(key, hashField, value, flags);

    public RedisValue[] HashKeys(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashKeys(key, flags);

    public long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashLength(key, flags);

    public RedisValue HashRandomField(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashRandomField(key, flags);

    public RedisValue[] HashRandomFields(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashRandomFields(key, count, flags);

    public HashEntry[] HashRandomFieldsWithValues(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashRandomFieldsWithValues(key, count, flags);

    public System.Collections.Generic.IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetActiveDatabase().HashScan(key, pattern, pageSize, flags);

    public System.Collections.Generic.IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public System.Collections.Generic.IEnumerable<RedisValue> HashScanNoValues(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScanNoValues(key, pattern, pageSize, cursor, pageOffset, flags);

    public bool HashSet(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashSet(key, hashField, value, when, flags);

    public void HashSet(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashSet(key, hashFields, flags);

    public long HashStringLength(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashStringLength(key, hashField, flags);

    public RedisValue[] HashValues(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashValues(key, flags);
}
