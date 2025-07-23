using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes functionality that is common to both standalone redis servers and redis clusters.
    /// </summary>
    public interface IDatabaseAsync : IRedisAsync
    {
        /// <summary>
        /// Indicates whether the instance can communicate with the server (resolved using the supplied key and optional flags).
        /// </summary>
        /// <param name="key">The key to check for.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyMigrate(RedisKey, EndPoint, int, int, MigrateOptions, CommandFlags)"/>
        Task KeyMigrateAsync(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.DebugObject(RedisKey, CommandFlags)"/>"
        Task<RedisValue> DebugObjectAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoAdd(RedisKey, double, double, RedisValue, CommandFlags)"/>
        Task<bool> GeoAddAsync(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoAdd(RedisKey, GeoEntry, CommandFlags)"/>
        Task<bool> GeoAddAsync(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoAdd(RedisKey, GeoEntry[], CommandFlags)"/>
        Task<long> GeoAddAsync(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoRemove(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> GeoRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoDistance(RedisKey, RedisValue, RedisValue, GeoUnit, CommandFlags)"/>
        Task<double?> GeoDistanceAsync(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoHash(RedisKey, RedisValue[], CommandFlags)"/>
        Task<string?[]> GeoHashAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoHash(RedisKey, RedisValue, CommandFlags)"/>
        Task<string?> GeoHashAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoPosition(RedisKey, RedisValue[], CommandFlags)"/>
        Task<GeoPosition?[]> GeoPositionAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoPosition(RedisKey, RedisValue, CommandFlags)"/>
        Task<GeoPosition?> GeoPositionAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoRadius(RedisKey, RedisValue, double, GeoUnit, int, Order?, GeoRadiusOptions, CommandFlags)"/>
        Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoRadius(RedisKey, double, double, double, GeoUnit, int, Order?, GeoRadiusOptions, CommandFlags)"/>
        Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoSearch(RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)"/>
        Task<GeoRadiusResult[]> GeoSearchAsync(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoSearch(RedisKey, double, double, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)"/>
        Task<GeoRadiusResult[]> GeoSearchAsync(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoSearchAndStore(RedisKey, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, bool, CommandFlags)"/>
        Task<long> GeoSearchAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.GeoSearchAndStore(RedisKey, RedisKey, double, double, GeoSearchShape, int, bool, Order?, bool, CommandFlags)"/>
        Task<long> GeoSearchAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashDecrement(RedisKey, RedisValue, long, CommandFlags)"/>
        Task<long> HashDecrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashDecrement(RedisKey, RedisValue, double, CommandFlags)"/>
        Task<double> HashDecrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashDelete(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashDelete(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long> HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashExists(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetAndDelete(RedisKey, RedisValue, CommandFlags)"/>
        Task<RedisValue> HashFieldGetAndDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetLeaseAndDelete(RedisKey, RedisValue, CommandFlags)"/>
        Task<Lease<byte>?> HashFieldGetLeaseAndDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetAndDelete(RedisKey, RedisValue[], CommandFlags)"/>
        Task<RedisValue[]> HashFieldGetAndDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetAndSetExpiry(RedisKey, RedisValue, TimeSpan?, bool, CommandFlags)"/>
        Task<RedisValue> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetAndSetExpiry(RedisKey, RedisValue, DateTime, CommandFlags)"/>
        Task<RedisValue> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetLeaseAndSetExpiry(RedisKey, RedisValue, TimeSpan?, bool, CommandFlags)"/>
        Task<Lease<byte>?> HashFieldGetLeaseAndSetExpiryAsync(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetLeaseAndSetExpiry(RedisKey, RedisValue, DateTime, CommandFlags)"/>
        Task<Lease<byte>?> HashFieldGetLeaseAndSetExpiryAsync(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetAndSetExpiry(RedisKey, RedisValue[], TimeSpan?, bool, CommandFlags)"/>
        Task<RedisValue[]> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue[] hashFields, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetAndSetExpiry(RedisKey, RedisValue[], DateTime, CommandFlags)"/>
        Task<RedisValue[]> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue[] hashFields, DateTime expiry, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldSetAndSetExpiry(RedisKey, RedisValue, RedisValue, TimeSpan?, bool, When, CommandFlags)"/>
        Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, RedisValue field, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldSetAndSetExpiry(RedisKey, RedisValue, RedisValue, DateTime, When, CommandFlags)"/>
        Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, RedisValue field, RedisValue value, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldSetAndSetExpiry(RedisKey, HashEntry[], TimeSpan?, bool, When, CommandFlags)"/>
        Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, HashEntry[] hashFields, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldSetAndSetExpiry(RedisKey, HashEntry[], DateTime, When, CommandFlags)"/>
        Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, HashEntry[] hashFields, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldExpire(RedisKey, RedisValue[], TimeSpan, ExpireWhen, CommandFlags)"/>
        Task<ExpireResult[]> HashFieldExpireAsync(RedisKey key, RedisValue[] hashFields, TimeSpan expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldExpire(RedisKey, RedisValue[], DateTime, ExpireWhen, CommandFlags)"/>
        Task<ExpireResult[]> HashFieldExpireAsync(RedisKey key, RedisValue[] hashFields, DateTime expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetExpireDateTime(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long[]> HashFieldGetExpireDateTimeAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="HashFieldPersistAsync(RedisKey, RedisValue[], CommandFlags)"/>
        Task<PersistResult[]> HashFieldPersistAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashFieldGetTimeToLive(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long[]> HashFieldGetTimeToLiveAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashGet(RedisKey, RedisValue, CommandFlags)"/>
        Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashGetLease(RedisKey, RedisValue, CommandFlags)"/>
        Task<Lease<byte>?> HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashGet(RedisKey, RedisValue[], CommandFlags)"/>
        Task<RedisValue[]> HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashGetAll(RedisKey, CommandFlags)"/>
        Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashIncrement(RedisKey, RedisValue, long, CommandFlags)"/>
        Task<long> HashIncrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashIncrement(RedisKey, RedisValue, double, CommandFlags)"/>
        Task<double> HashIncrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashKeys(RedisKey, CommandFlags)"/>
        Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashLength(RedisKey, CommandFlags)"/>
        Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashRandomField(RedisKey, CommandFlags)"/>
        Task<RedisValue> HashRandomFieldAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashRandomFields(RedisKey, long, CommandFlags)"/>
        Task<RedisValue[]> HashRandomFieldsAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashRandomFieldsWithValues(RedisKey, long, CommandFlags)"/>
        Task<HashEntry[]> HashRandomFieldsWithValuesAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashScan(RedisKey, RedisValue, int, long, int, CommandFlags)"/>
        IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashScanNoValues(RedisKey, RedisValue, int, long, int, CommandFlags)"/>
        IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashSet(RedisKey, HashEntry[], CommandFlags)"/>
        Task HashSetAsync(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashSet(RedisKey, RedisValue, RedisValue, When, CommandFlags)"/>
        Task<bool> HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashStringLength(RedisKey, RedisValue, CommandFlags)"/>
        Task<long> HashStringLengthAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HashValues(RedisKey, CommandFlags)"/>
        Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HyperLogLogAdd(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HyperLogLogAdd(RedisKey, RedisValue[], CommandFlags)"/>
        Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HyperLogLogLength(RedisKey, CommandFlags)"/>
        Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HyperLogLogLength(RedisKey[], CommandFlags)"/>
        Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HyperLogLogMerge(RedisKey, RedisKey, RedisKey, CommandFlags)"/>
        Task HyperLogLogMergeAsync(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.HyperLogLogMerge(RedisKey, RedisKey[], CommandFlags)"/>
        Task HyperLogLogMergeAsync(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.IdentifyEndpoint(RedisKey, CommandFlags)"/>
        Task<EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyCopy(RedisKey, RedisKey, int, bool, CommandFlags)"/>
        Task<bool> KeyCopyAsync(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyDelete(RedisKey, CommandFlags)"/>
        Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyDelete(RedisKey[], CommandFlags)"/>
        Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyDump(RedisKey, CommandFlags)"/>
        Task<byte[]?> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyEncoding(RedisKey, CommandFlags)"/>
        Task<string?> KeyEncodingAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyExists(RedisKey, CommandFlags)"/>
        Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyExists(RedisKey[], CommandFlags)"/>
        Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyExpire(RedisKey, TimeSpan?, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.KeyExpire(RedisKey, TimeSpan?, ExpireWhen, CommandFlags)"/>
        Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyExpire(RedisKey, DateTime?, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.KeyExpire(RedisKey, DateTime?, ExpireWhen, CommandFlags)"/>
        Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyExpireTime(RedisKey, CommandFlags)"/>
        Task<DateTime?> KeyExpireTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyFrequency(RedisKey, CommandFlags)"/>
        Task<long?> KeyFrequencyAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyIdleTime(RedisKey, CommandFlags)"/>
        Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyMove(RedisKey, int, CommandFlags)"/>
        Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyPersist(RedisKey, CommandFlags)"/>
        Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyRandom(CommandFlags)"/>
        Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyRefCount(RedisKey, CommandFlags)"/>
        Task<long?> KeyRefCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyRename(RedisKey, RedisKey, When, CommandFlags)"/>
        Task<bool> KeyRenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyRestore(RedisKey, byte[], TimeSpan?, CommandFlags)"/>
        Task KeyRestoreAsync(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyTimeToLive(RedisKey, CommandFlags)"/>
        Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyTouch(RedisKey, CommandFlags)"/>
        Task<bool> KeyTouchAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyTouch(RedisKey[], CommandFlags)"/>
        Task<long> KeyTouchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.KeyType(RedisKey, CommandFlags)"/>
        Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListGetByIndex(RedisKey, long, CommandFlags)"/>
        Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListInsertAfter(RedisKey, RedisValue, RedisValue, CommandFlags)"/>
        Task<long> ListInsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListInsertBefore(RedisKey, RedisValue, RedisValue, CommandFlags)"/>
        Task<long> ListInsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListLeftPop(RedisKey, CommandFlags)"/>
        Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListLeftPop(RedisKey, long, CommandFlags)"/>
        Task<RedisValue[]> ListLeftPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListLeftPop(RedisKey[], long, CommandFlags)"/>
        Task<ListPopResult> ListLeftPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListPosition(RedisKey, RedisValue, long, long, CommandFlags)"/>
        Task<long> ListPositionAsync(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListPositions(RedisKey, RedisValue, long, long, long, CommandFlags)"/>
        Task<long[]> ListPositionsAsync(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListLeftPush(RedisKey, RedisValue, When, CommandFlags)"/>
        Task<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListLeftPush(RedisKey, RedisValue[], When, CommandFlags)"/>
        Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListLeftPush(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.ListLength(RedisKey, CommandFlags)"/>
        Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListMove(RedisKey, RedisKey, ListSide, ListSide, CommandFlags)"/>
        Task<RedisValue> ListMoveAsync(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRange(RedisKey, long, long, CommandFlags)"/>
        Task<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRemove(RedisKey, RedisValue, long, CommandFlags)"/>
        Task<long> ListRemoveAsync(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRightPop(RedisKey, CommandFlags)"/>
        Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRightPop(RedisKey, long, CommandFlags)"/>
        Task<RedisValue[]> ListRightPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRightPop(RedisKey[], long, CommandFlags)"/>
        Task<ListPopResult> ListRightPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRightPopLeftPush(RedisKey, RedisKey, CommandFlags)"/>
        Task<RedisValue> ListRightPopLeftPushAsync(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRightPush(RedisKey, RedisValue, When, CommandFlags)"/>
        Task<long> ListRightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRightPush(RedisKey, RedisValue[], When, CommandFlags)"/>
        Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListRightPush(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.ListSetByIndex(RedisKey, long, RedisValue, CommandFlags)"/>
        Task ListSetByIndexAsync(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ListTrim(RedisKey, long, long, CommandFlags)"/>
        Task ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.LockExtend(RedisKey, RedisValue, TimeSpan, CommandFlags)"/>
        Task<bool> LockExtendAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.LockQuery(RedisKey, CommandFlags)"/>
        Task<RedisValue> LockQueryAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.LockRelease(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.LockTake(RedisKey, RedisValue, TimeSpan, CommandFlags)"/>
        Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.Publish(RedisChannel, RedisValue, CommandFlags)"/>
        Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.Execute(string, object[])"/>
        Task<RedisResult> ExecuteAsync(string command, params object[] args);

        /// <inheritdoc cref="IDatabase.Execute(string, ICollection{object}, CommandFlags)"/>
        Task<RedisResult> ExecuteAsync(string command, ICollection<object>? args, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ScriptEvaluate(string, RedisKey[], RedisValue[], CommandFlags)"/>
        Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ScriptEvaluate(byte[], RedisKey[], RedisValue[], CommandFlags)"/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        Task<RedisResult> ScriptEvaluateAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ScriptEvaluate(LuaScript, object?, CommandFlags)"/>
        Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ScriptEvaluate(LoadedLuaScript, object?, CommandFlags)"/>
        Task<RedisResult> ScriptEvaluateAsync(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ScriptEvaluateReadOnly(string, RedisKey[], RedisValue[], CommandFlags)"/>
        Task<RedisResult> ScriptEvaluateReadOnlyAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.ScriptEvaluateReadOnly(byte[], RedisKey[], RedisValue[], CommandFlags)"/>
        Task<RedisResult> ScriptEvaluateReadOnlyAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetAdd(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetAdd(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetCombine(SetOperation, RedisKey, RedisKey, CommandFlags)"/>
        Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetCombine(SetOperation, RedisKey[], CommandFlags)"/>
        Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetCombineAndStore(SetOperation, RedisKey, RedisKey, RedisKey, CommandFlags)"/>
        Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetCombineAndStore(SetOperation, RedisKey, RedisKey[], CommandFlags)"/>
        Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetContains(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetContains(RedisKey, RedisValue[], CommandFlags)"/>
        Task<bool[]> SetContainsAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetIntersectionLength(RedisKey[], long, CommandFlags)"/>
        Task<long> SetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetLength(RedisKey, CommandFlags)"/>
        Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetMembers(RedisKey, CommandFlags)"/>
        Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetMove(RedisKey, RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> SetMoveAsync(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetPop(RedisKey, CommandFlags)"/>
        Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetPop(RedisKey, long, CommandFlags)"/>
        Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetRandomMember(RedisKey, CommandFlags)"/>
        Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetRandomMembers(RedisKey, long, CommandFlags)"/>
        Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetRemove(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetRemove(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SetScan(RedisKey, RedisValue, int, long, int, CommandFlags)"/>
        IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.Sort(RedisKey, long, long, Order, SortType, RedisValue, RedisValue[], CommandFlags)"/>
        Task<RedisValue[]> SortAsync(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortAndStore(RedisKey, RedisKey, long, long, Order, SortType, RedisValue, RedisValue[], CommandFlags)"/>
        Task<long> SortAndStoreAsync(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="SortedSetAddAsync(RedisKey, RedisValue, double, SortedSetWhen, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, CommandFlags flags);

        /// <inheritdoc cref="SortedSetAddAsync(RedisKey, RedisValue, double, SortedSetWhen, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, When when, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetAdd(RedisKey, RedisValue, double, SortedSetWhen, CommandFlags)"/>
        Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="SortedSetAddAsync(RedisKey, RedisValue, double, SortedSetWhen, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, CommandFlags flags);

        /// <inheritdoc cref="SortedSetAddAsync(RedisKey, RedisValue, double, SortedSetWhen, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetAdd(RedisKey, SortedSetEntry[], SortedSetWhen, CommandFlags)"/>
        Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetCombine(SetOperation, RedisKey[], double[], Aggregate, CommandFlags)"/>
        Task<RedisValue[]> SortedSetCombineAsync(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetCombineWithScores(SetOperation, RedisKey[], double[], Aggregate, CommandFlags)"/>
        Task<SortedSetEntry[]> SortedSetCombineWithScoresAsync(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetCombineAndStore(SetOperation, RedisKey, RedisKey, RedisKey, Aggregate, CommandFlags)"/>
        Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetCombineAndStore(SetOperation, RedisKey, RedisKey[], double[], Aggregate, CommandFlags)"/>
        Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetDecrement(RedisKey, RedisValue, double, CommandFlags)"/>
        Task<double> SortedSetDecrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetIncrement(RedisKey, RedisValue, double, CommandFlags)"/>
        Task<double> SortedSetIncrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetIntersectionLength(RedisKey[], long, CommandFlags)"/>
        Task<long> SortedSetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetLength(RedisKey, double, double, Exclude, CommandFlags)"/>
        Task<long> SortedSetLengthAsync(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetLengthByValue(RedisKey, RedisValue, RedisValue, Exclude, CommandFlags)"/>
        Task<long> SortedSetLengthByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRandomMember(RedisKey, CommandFlags)"/>
        Task<RedisValue> SortedSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRandomMembers(RedisKey, long, CommandFlags)"/>
        Task<RedisValue[]> SortedSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRandomMembersWithScores(RedisKey, long, CommandFlags)"/>
        Task<SortedSetEntry[]> SortedSetRandomMembersWithScoresAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRangeByRank(RedisKey, long, long, Order, CommandFlags)"/>
        Task<RedisValue[]> SortedSetRangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRangeAndStore(RedisKey, RedisKey, RedisValue, RedisValue, SortedSetOrder, Exclude, Order, long, long?, CommandFlags)"/>
        Task<long> SortedSetRangeAndStoreAsync(
            RedisKey sourceKey,
            RedisKey destinationKey,
            RedisValue start,
            RedisValue stop,
            SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long? take = null,
            CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRangeByRankWithScores(RedisKey, long, long, Order, CommandFlags)"/>
        Task<SortedSetEntry[]> SortedSetRangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRangeByScore(RedisKey, double, double, Exclude, Order, long, long, CommandFlags)"/>
        Task<RedisValue[]> SortedSetRangeByScoreAsync(
            RedisKey key,
            double start = double.NegativeInfinity,
            double stop = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRangeByScoreWithScores(RedisKey, double, double, Exclude, Order, long, long, CommandFlags)"/>
        Task<SortedSetEntry[]> SortedSetRangeByScoreWithScoresAsync(
            RedisKey key,
            double start = double.NegativeInfinity,
            double stop = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRangeByValue(RedisKey, RedisValue, RedisValue, Exclude, long, long, CommandFlags)"/>
        Task<RedisValue[]> SortedSetRangeByValueAsync(
            RedisKey key,
            RedisValue min,
            RedisValue max,
            Exclude exclude,
            long skip,
            long take = -1,
            CommandFlags flags = CommandFlags.None); // defaults removed to avoid ambiguity with overload with order

        /// <inheritdoc cref="IDatabase.SortedSetRangeByValue(RedisKey, RedisValue, RedisValue, Exclude, Order, long, long, CommandFlags)"/>
        Task<RedisValue[]> SortedSetRangeByValueAsync(
            RedisKey key,
            RedisValue min = default,
            RedisValue max = default,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRank(RedisKey, RedisValue, Order, CommandFlags)"/>
        Task<long?> SortedSetRankAsync(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRemove(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRemove(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long> SortedSetRemoveAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRemoveRangeByRank(RedisKey, long, long, CommandFlags)"/>
        Task<long> SortedSetRemoveRangeByRankAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRemoveRangeByScore(RedisKey, double, double, Exclude, CommandFlags)"/>
        Task<long> SortedSetRemoveRangeByScoreAsync(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetRemoveRangeByValue(RedisKey, RedisValue, RedisValue, Exclude, CommandFlags)"/>
        Task<long> SortedSetRemoveRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetScan(RedisKey, RedisValue, int, long, int, CommandFlags)"/>
        IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(
            RedisKey key,
            RedisValue pattern = default,
            int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
            long cursor = RedisBase.CursorUtils.Origin,
            int pageOffset = 0,
            CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetScore(RedisKey, RedisValue, CommandFlags)"/>
        Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetScores(RedisKey, RedisValue[], CommandFlags)"/>
        Task<double?[]> SortedSetScoresAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetUpdate(RedisKey, RedisValue, double, SortedSetWhen, CommandFlags)"/>
        Task<bool> SortedSetUpdateAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetUpdate(RedisKey, SortedSetEntry[], SortedSetWhen, CommandFlags)"/>
        Task<long> SortedSetUpdateAsync(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetPop(RedisKey, Order, CommandFlags)"/>
        Task<SortedSetEntry?> SortedSetPopAsync(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetPop(RedisKey, long, Order, CommandFlags)"/>
        Task<SortedSetEntry[]> SortedSetPopAsync(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.SortedSetPop(RedisKey[], long, Order, CommandFlags)"/>
        Task<SortedSetPopResult> SortedSetPopAsync(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamAcknowledge(RedisKey, RedisValue, RedisValue, CommandFlags)"/>
        Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamAcknowledge(RedisKey, RedisValue, RedisValue[], CommandFlags)"/>
        Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);

#pragma warning disable RS0026 // similar overloads
        /// <inheritdoc cref="IDatabase.StreamAcknowledgeAndDelete(RedisKey, RedisValue, StreamTrimMode, RedisValue, CommandFlags)"/>
        Task<StreamTrimResult> StreamAcknowledgeAndDeleteAsync(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamAcknowledgeAndDelete(RedisKey, RedisValue, StreamTrimMode, RedisValue[], CommandFlags)"/>
        Task<StreamTrimResult[]> StreamAcknowledgeAndDeleteAsync(RedisKey key, RedisValue groupName, StreamTrimMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);
#pragma warning restore RS0026

        /// <inheritdoc cref="IDatabase.StreamAdd(RedisKey, RedisValue, RedisValue, RedisValue?, int?, bool, CommandFlags)"/>
        Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StreamAdd(RedisKey, NameValueEntry[], RedisValue?, int?, bool, CommandFlags)"/>
        Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags);

#pragma warning disable RS0026 // similar overloads
        /// <inheritdoc cref="IDatabase.StreamAdd(RedisKey, RedisValue, RedisValue, RedisValue?, long?, bool, long?, StreamTrimMode, CommandFlags)"/>
        Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamAdd(RedisKey, NameValueEntry[], RedisValue?, long?, bool, long?, StreamTrimMode, CommandFlags)"/>
        Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, long? maxLength = null, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode trimMode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None);
#pragma warning restore RS0026

        /// <inheritdoc cref="IDatabase.StreamAutoClaim(RedisKey, RedisValue, RedisValue, long, RedisValue, int?, CommandFlags)"/>
        Task<StreamAutoClaimResult> StreamAutoClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamAutoClaimIdsOnly(RedisKey, RedisValue, RedisValue, long, RedisValue, int?, CommandFlags)"/>
        Task<StreamAutoClaimIdsOnlyResult> StreamAutoClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamClaim(RedisKey, RedisValue, RedisValue, long, RedisValue[], CommandFlags)"/>
        Task<StreamEntry[]> StreamClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamClaimIdsOnly(RedisKey, RedisValue, RedisValue, long, RedisValue[], CommandFlags)"/>
        Task<RedisValue[]> StreamClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamConsumerGroupSetPosition(RedisKey, RedisValue, RedisValue, CommandFlags)"/>
        Task<bool> StreamConsumerGroupSetPositionAsync(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamConsumerInfo(RedisKey, RedisValue, CommandFlags)"/>
        Task<StreamConsumerInfo[]> StreamConsumerInfoAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamCreateConsumerGroup(RedisKey, RedisValue, RedisValue?, CommandFlags)"/>
        Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StreamCreateConsumerGroup(RedisKey, RedisValue, RedisValue?, bool, CommandFlags)"/>
        Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None);

#pragma warning disable RS0026
        /// <inheritdoc cref="IDatabase.StreamDelete(RedisKey, RedisValue[], CommandFlags)"/>
        Task<long> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamDelete(RedisKey, RedisValue[], StreamTrimMode, CommandFlags)"/>
        Task<StreamTrimResult[]> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, StreamTrimMode mode, CommandFlags flags = CommandFlags.None);
#pragma warning restore RS0026

        /// <inheritdoc cref="IDatabase.StreamDeleteConsumer(RedisKey, RedisValue, RedisValue, CommandFlags)"/>
        Task<long> StreamDeleteConsumerAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamDeleteConsumerGroup(RedisKey, RedisValue, CommandFlags)"/>
        Task<bool> StreamDeleteConsumerGroupAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamGroupInfo(RedisKey, CommandFlags)"/>
        Task<StreamGroupInfo[]> StreamGroupInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamInfo(RedisKey, CommandFlags)"/>
        Task<StreamInfo> StreamInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamLength(RedisKey, CommandFlags)"/>
        Task<long> StreamLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamPending(RedisKey, RedisValue, CommandFlags)"/>
        Task<StreamPendingInfo> StreamPendingAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamPendingMessages(RedisKey, RedisValue, int, RedisValue, RedisValue?, RedisValue?, CommandFlags)"/>
        Task<StreamPendingMessageInfo[]> StreamPendingMessagesAsync(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamRange(RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags)"/>
        Task<StreamEntry[]> StreamRangeAsync(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamRead(RedisKey, RedisValue, int?, CommandFlags)"/>
        Task<StreamEntry[]> StreamReadAsync(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamRead(StreamPosition[], int?, CommandFlags)"/>
        Task<RedisStream[]> StreamReadAsync(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamReadGroup(RedisKey, RedisValue, RedisValue, RedisValue?, int?, CommandFlags)"/>
        Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StreamReadGroup(RedisKey, RedisValue, RedisValue, RedisValue?, int?, bool, CommandFlags)"/>
        Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamReadGroup(StreamPosition[], RedisValue, RedisValue, int?, CommandFlags)"/>
        Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StreamReadGroup(StreamPosition[], RedisValue, RedisValue, int?, bool, CommandFlags)"/>
        Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamTrim(RedisKey, int, bool, CommandFlags)"/>
        Task<long> StreamTrimAsync(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StreamTrim(RedisKey, long, bool, long?, StreamTrimMode, CommandFlags)"/>
        Task<long> StreamTrimAsync(RedisKey key, long maxLength, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StreamTrimByMinId(RedisKey, RedisValue, bool, long?, StreamTrimMode, CommandFlags)"/>
        Task<long> StreamTrimByMinIdAsync(RedisKey key, RedisValue minId, bool useApproximateMaxLength = false, long? limit = null, StreamTrimMode mode = StreamTrimMode.KeepReferences, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringAppend(RedisKey, RedisValue, CommandFlags)"/>
        Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="StringBitCountAsync(RedisKey, long, long, StringIndexType, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<long> StringBitCountAsync(RedisKey key, long start, long end, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StringBitCount(RedisKey, long, long, StringIndexType, CommandFlags)"/>
        Task<long> StringBitCountAsync(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringBitOperation(Bitwise, RedisKey, RedisKey, RedisKey, CommandFlags)"/>
        Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringBitOperation(Bitwise, RedisKey, RedisKey[], CommandFlags)"/>
        Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="StringBitPositionAsync(RedisKey, bool, long, long, StringIndexType, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start, long end, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StringBitPosition(RedisKey, bool, long, long, StringIndexType, CommandFlags)"/>
        Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringDecrement(RedisKey, long, CommandFlags)"/>
        Task<long> StringDecrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringDecrement(RedisKey, double, CommandFlags)"/>
        Task<double> StringDecrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGet(RedisKey, CommandFlags)"/>
        Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGet(RedisKey[], CommandFlags)"/>
        Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGetLease(RedisKey, CommandFlags)"/>
        Task<Lease<byte>?> StringGetLeaseAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGetBit(RedisKey, long, CommandFlags)"/>
        Task<bool> StringGetBitAsync(RedisKey key, long offset, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGetRange(RedisKey, long, long, CommandFlags)"/>
        Task<RedisValue> StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGetSet(RedisKey, RedisValue, CommandFlags)"/>
        Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGetSetExpiry(RedisKey, TimeSpan?, CommandFlags)"/>
        Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGetSetExpiry(RedisKey, DateTime, CommandFlags)"/>
        Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGetDelete(RedisKey, CommandFlags)"/>
        Task<RedisValue> StringGetDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringGetWithExpiry(RedisKey, CommandFlags)"/>
        Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringIncrement(RedisKey, long, CommandFlags)"/>
        Task<long> StringIncrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringIncrement(RedisKey, double, CommandFlags)"/>
        Task<double> StringIncrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringLength(RedisKey, CommandFlags)"/>
        Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringLongestCommonSubsequence(RedisKey, RedisKey, CommandFlags)"/>
        Task<string?> StringLongestCommonSubsequenceAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None);

        /// t<inheritdoc cref="IDatabase.StringLongestCommonSubsequenceLength(RedisKey, RedisKey, CommandFlags)"/>
        Task<long> StringLongestCommonSubsequenceLengthAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringLongestCommonSubsequenceWithMatches(RedisKey, RedisKey, long, CommandFlags)"/>
        Task<LCSMatchResult> StringLongestCommonSubsequenceWithMatchesAsync(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="StringSetAsync(RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when);

        /// <inheritdoc cref="StringSetAsync(RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags)"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StringSet(RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags)"/>
        Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringSet(KeyValuePair{RedisKey, RedisValue}[], When, CommandFlags)"/>
        Task<bool> StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringSetAndGet(RedisKey, RedisValue, TimeSpan?, When, CommandFlags)"/>
        Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags);

        /// <inheritdoc cref="IDatabase.StringSetAndGet(RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags)"/>
        Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringSetBit(RedisKey, long, bool, CommandFlags)"/>
        Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IDatabase.StringSetRange(RedisKey, long, RedisValue, CommandFlags)"/>
        Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None);
    }
}
