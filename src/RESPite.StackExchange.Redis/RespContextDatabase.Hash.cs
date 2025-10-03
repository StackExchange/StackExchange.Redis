using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    public long HashDecrement(
        RedisKey key,
        RedisValue hashField,
        long value = 1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HIncrBy(key, hashField, -value).Wait(SyncTimeout);

    public double HashDecrement(
        RedisKey key,
        RedisValue hashField,
        double value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HIncrByFloat(key, hashField, -value).Wait(SyncTimeout);

    public Task<long> HashDecrementAsync(
        RedisKey key,
        RedisValue hashField,
        long value = 1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HIncrBy(key, hashField, -value).AsTask();

    public Task<double> HashDecrementAsync(
        RedisKey key,
        RedisValue hashField,
        double value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HIncrByFloat(key, hashField, -value).AsTask();

    public bool HashDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HDel(key, hashField).Wait(SyncTimeout);

    public long HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HDel(key, hashFields).Wait(SyncTimeout);

    public Task<bool> HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HDel(key, hashField).AsTask();

    public Task<long> HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HDel(key, hashFields).AsTask();

    public bool HashExists(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HExists(key, hashField).Wait(SyncTimeout);

    public Task<bool> HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HExists(key, hashField).AsTask();

    public ExpireResult[] HashFieldExpire(RedisKey key, RedisValue[] hashFields, TimeSpan expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public ExpireResult[] HashFieldExpire(RedisKey key, RedisValue[] hashFields, DateTime expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<ExpireResult[]> HashFieldExpireAsync(RedisKey key, RedisValue[] hashFields, TimeSpan expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<ExpireResult[]> HashFieldExpireAsync(RedisKey key, RedisValue[] hashFields, DateTime expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue HashFieldGetAndDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue[] HashFieldGetAndDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue> HashFieldGetAndDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue[]> HashFieldGetAndDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue HashFieldGetAndSetExpiry(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue HashFieldGetAndSetExpiry(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue[] HashFieldGetAndSetExpiry(RedisKey key, RedisValue[] hashFields, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue[] HashFieldGetAndSetExpiry(RedisKey key, RedisValue[] hashFields, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue[]> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue[] hashFields, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue[]> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue[] hashFields, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public long[] HashFieldGetExpireDateTime(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<long[]> HashFieldGetExpireDateTimeAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Lease<byte>? HashFieldGetLeaseAndDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<Lease<byte>?> HashFieldGetLeaseAndDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Lease<byte>? HashFieldGetLeaseAndSetExpiry(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Lease<byte>? HashFieldGetLeaseAndSetExpiry(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<Lease<byte>?> HashFieldGetLeaseAndSetExpiryAsync(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<Lease<byte>?> HashFieldGetLeaseAndSetExpiryAsync(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public long[] HashFieldGetTimeToLive(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<long[]> HashFieldGetTimeToLiveAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public PersistResult[] HashFieldPersist(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<PersistResult[]> HashFieldPersistAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue HashFieldSetAndSetExpiry(RedisKey key, RedisValue hashField, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue HashFieldSetAndSetExpiry(RedisKey key, RedisValue hashField, RedisValue value, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue HashFieldSetAndSetExpiry(RedisKey key, HashEntry[] hashFields, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue HashFieldSetAndSetExpiry(RedisKey key, HashEntry[] hashFields, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, RedisValue hashField, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, RedisValue hashField, RedisValue value, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, HashEntry[] hashFields, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, HashEntry[] hashFields, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public RedisValue HashGet(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HGet(key, hashField).Wait(SyncTimeout);

    public RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HMGet(key, hashFields).Wait(SyncTimeout);

    public HashEntry[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HGetAll(key).Wait(SyncTimeout);

    public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HGet(key, hashField).AsTask();

    public Task<RedisValue[]> HashGetAsync(
        RedisKey key,
        RedisValue[] hashFields,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HMGet(key, hashFields).AsTask();

    public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HGetAll(key).AsTask();

    public Lease<byte>? HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HGetLease(key, hashField).Wait(SyncTimeout);

    public Task<Lease<byte>?> HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HGetLease(key, hashField).AsTask();

    public long HashIncrement(
        RedisKey key,
        RedisValue hashField,
        long value = 1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HIncrBy(key, hashField, value).Wait(SyncTimeout);

    public double HashIncrement(
        RedisKey key,
        RedisValue hashField,
        double value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HIncrByFloat(key, hashField, value).Wait(SyncTimeout);

    public Task<long> HashIncrementAsync(
        RedisKey key,
        RedisValue hashField,
        long value = 1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HIncrBy(key, hashField, value).AsTask();

    public Task<double> HashIncrementAsync(
        RedisKey key,
        RedisValue hashField,
        double value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HIncrByFloat(key, hashField, value).AsTask();

    public RedisValue[] HashKeys(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HKeys(key).Wait(SyncTimeout);

    public Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HKeys(key).AsTask();

    public long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HLen(key).Wait(SyncTimeout);

    public Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HLen(key).AsTask();

    public RedisValue HashRandomField(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HRandField(key).Wait(SyncTimeout);

    public RedisValue[] HashRandomFields(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HRandField(key, count).Wait(SyncTimeout);

    public HashEntry[] HashRandomFieldsWithValues(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HRandFieldWithValues(key, count).Wait(SyncTimeout);

    public Task<RedisValue> HashRandomFieldAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HRandField(key).AsTask();

    public Task<RedisValue[]> HashRandomFieldsAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HRandField(key, count).AsTask();

    public Task<HashEntry[]> HashRandomFieldsWithValuesAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HRandFieldWithValues(key, count).AsTask();

    public IEnumerable<HashEntry> HashScan(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => throw new NotImplementedException();

    public IAsyncEnumerable<HashEntry> HashScanAsync(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public IEnumerable<RedisValue> HashScanNoValues(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public bool HashSet(
        RedisKey key,
        RedisValue hashField,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HSet(key, hashField, value, when).Wait(SyncTimeout);

    public Task<bool> HashSetAsync(
        RedisKey key,
        RedisValue hashField,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HSet(key, hashField, value, when).AsTask();

    public void HashSet(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HSet(key, hashFields).Wait(SyncTimeout);

    public Task HashSetAsync(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HSet(key, hashFields).AsTask();

    public long HashStringLength(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HStrLen(key, hashField).Wait(SyncTimeout);

    public Task<long> HashStringLengthAsync(
        RedisKey key,
        RedisValue hashField,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HStrLen(key, hashField).AsTask();

    public RedisValue[] HashValues(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HVals(key).Wait(SyncTimeout);

    public Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Hashes().HVals(key).AsTask();
}
