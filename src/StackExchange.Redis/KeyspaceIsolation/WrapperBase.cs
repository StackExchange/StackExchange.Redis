using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis.KeyspaceIsolation
{
    internal class WrapperBase<TInner> : IDatabaseAsync where TInner : IDatabaseAsync
    {
        internal WrapperBase(TInner inner, byte[] keyPrefix)
        {
            Inner = inner;
            Prefix = keyPrefix;
        }

        public IConnectionMultiplexer Multiplexer => Inner.Multiplexer;

        internal TInner Inner { get; }

        internal byte[] Prefix { get; }

        public Task<RedisValue> DebugObjectAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.DebugObjectAsync(ToInner(key), flags);
        }

        public Task<bool> GeoAddAsync(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Inner.GeoAddAsync(ToInner(key), longitude, latitude, member, flags);

        public Task<bool> GeoAddAsync(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None)
            => Inner.GeoAddAsync(ToInner(key), value, flags);

        public Task<long> GeoAddAsync(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None)
            => Inner.GeoAddAsync(ToInner(key), values, flags);

        public Task<bool> GeoRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Inner.GeoRemoveAsync(ToInner(key), member, flags);

        public Task<double?> GeoDistanceAsync(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None)
            => Inner.GeoDistanceAsync(ToInner(key), member1, member2, unit, flags);

        public Task<string[]> GeoHashAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => Inner.GeoHashAsync(ToInner(key), members, flags);

        public Task<string> GeoHashAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Inner.GeoHashAsync(ToInner(key), member, flags);

        public Task<GeoPosition?[]> GeoPositionAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => Inner.GeoPositionAsync(ToInner(key), members, flags);

        public Task<GeoPosition?> GeoPositionAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Inner.GeoPositionAsync(ToInner(key), member, flags);

        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
            => Inner.GeoRadiusAsync(ToInner(key), member, radius, unit, count, order, options, flags);

        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
            => Inner.GeoRadiusAsync(ToInner(key), longitude, latitude, radius, unit, count, order, options, flags);

        public Task<double> HashDecrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashDecrementAsync(ToInner(key), hashField, value, flags);
        }

        public Task<long> HashDecrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashDecrementAsync(ToInner(key), hashField, value, flags);
        }

        public Task<long> HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashDeleteAsync(ToInner(key), hashFields, flags);
        }

        public Task<bool> HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashDeleteAsync(ToInner(key), hashField, flags);
        }

        public Task<bool> HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashExistsAsync(ToInner(key), hashField, flags);
        }

        public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashGetAllAsync(ToInner(key), flags);
        }

        public Task<RedisValue[]> HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashGetAsync(ToInner(key), hashFields, flags);
        }

        public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashGetAsync(ToInner(key), hashField, flags);
        }

        public Task<Lease<byte>> HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashGetLeaseAsync(ToInner(key), hashField, flags);
        }

        public Task<double> HashIncrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashIncrementAsync(ToInner(key), hashField, value, flags);
        }

        public Task<long> HashIncrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashIncrementAsync(ToInner(key), hashField, value, flags);
        }

        public Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashKeysAsync(ToInner(key), flags);
        }

        public Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashLengthAsync(ToInner(key), flags);
        }

        public Task<bool> HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashSetAsync(ToInner(key), hashField, value, when, flags);
        }

        public Task<long> HashStringLengthAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashStringLengthAsync(ToInner(key), hashField, flags);
        }

        public Task HashSetAsync(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashSetAsync(ToInner(key), hashFields, flags);
        }

        public Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HashValuesAsync(ToInner(key), flags);
        }

        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HyperLogLogAddAsync(ToInner(key), values, flags);
        }

        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HyperLogLogAddAsync(ToInner(key), value, flags);
        }

        public Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HyperLogLogLengthAsync(ToInner(key), flags);
        }

        public Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HyperLogLogLengthAsync(ToInner(keys), flags);
        }

        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HyperLogLogMergeAsync(ToInner(destination), ToInner(sourceKeys), flags);
        }

        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            return Inner.HyperLogLogMergeAsync(ToInner(destination), ToInner(first), ToInner(second), flags);
        }

        public Task<EndPoint> IdentifyEndpointAsync(RedisKey key = default(RedisKey), CommandFlags flags = CommandFlags.None)
        {
            return Inner.IdentifyEndpointAsync(ToInner(key), flags);
        }

        public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.IsConnected(ToInner(key), flags);
        }

        public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyDeleteAsync(ToInner(keys), flags);
        }

        public Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyDeleteAsync(ToInner(key), flags);
        }

        public Task<byte[]> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyDumpAsync(ToInner(key), flags);
        }

        public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyExistsAsync(ToInner(key), flags);
        }

        public Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyExistsAsync(ToInner(keys), flags);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyExpireAsync(ToInner(key), expiry, flags);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyExpireAsync(ToInner(key), expiry, flags);
        }

        public Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key,CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyIdleTimeAsync(ToInner(key), flags);
        }

        public Task KeyMigrateAsync(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyMigrateAsync(ToInner(key), toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags);
        }

        public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyMoveAsync(ToInner(key), database, flags);
        }

        public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyPersistAsync(ToInner(key), flags);
        }

        public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None)
        {
            throw new NotSupportedException("RANDOMKEY is not supported when a key-prefix is specified");
        }

        public Task<bool> KeyRenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyRenameAsync(ToInner(key), ToInner(newKey), when, flags);
        }

        public Task KeyRestoreAsync(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyRestoreAsync(ToInner(key), value, expiry, flags);
        }

        public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyTimeToLiveAsync(ToInner(key), flags);
        }

        public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.KeyTypeAsync(ToInner(key), flags);
        }

        public Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListGetByIndexAsync(ToInner(key), index, flags);
        }

        public Task<long> ListInsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListInsertAfterAsync(ToInner(key), pivot, value, flags);
        }

        public Task<long> ListInsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListInsertBeforeAsync(ToInner(key), pivot, value, flags);
        }

        public Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListLeftPopAsync(ToInner(key), flags);
        }

        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListLeftPushAsync(ToInner(key), values, flags);
        }

        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListLeftPushAsync(ToInner(key), value, when, flags);
        }

        public Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListLengthAsync(ToInner(key), flags);
        }

        public Task<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListRangeAsync(ToInner(key), start, stop, flags);
        }

        public Task<long> ListRemoveAsync(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListRemoveAsync(ToInner(key), value, count, flags);
        }

        public Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListRightPopAsync(ToInner(key), flags);
        }

        public Task<RedisValue> ListRightPopLeftPushAsync(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListRightPopLeftPushAsync(ToInner(source), ToInner(destination), flags);
        }

        public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListRightPushAsync(ToInner(key), values, flags);
        }

        public Task<long> ListRightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListRightPushAsync(ToInner(key), value, when, flags);
        }

        public Task ListSetByIndexAsync(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListSetByIndexAsync(ToInner(key), index, value, flags);
        }

        public Task ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ListTrimAsync(ToInner(key), start, stop, flags);
        }

        public Task<bool> LockExtendAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            return Inner.LockExtendAsync(ToInner(key), value, expiry, flags);
        }

        public Task<RedisValue> LockQueryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.LockQueryAsync(ToInner(key), flags);
        }

        public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.LockReleaseAsync(ToInner(key), value, flags);
        }

        public Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            return Inner.LockTakeAsync(ToInner(key), value, expiry, flags);
        }

        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            return Inner.PublishAsync(ToInner(channel), message, flags);
        }

        public Task<RedisResult> ExecuteAsync(string command, params object[] args)
            => Inner.ExecuteAsync(command, ToInner(args), CommandFlags.None);

        public Task<RedisResult> ExecuteAsync(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
            => Inner.ExecuteAsync(command, ToInner(args), flags);

        public Task<RedisResult> ScriptEvaluateAsync(byte[] hash, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None)
        {
            // TODO: The return value could contain prefixed keys. It might make sense to 'unprefix' those?
            return Inner.ScriptEvaluateAsync(hash, ToInner(keys), values, flags);
        }

        public Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None)
        {
            // TODO: The return value could contain prefixed keys. It might make sense to 'unprefix' those?
            return Inner.ScriptEvaluateAsync(script, ToInner(keys), values, flags);
        }

        public Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ScriptEvaluateAsync(script, parameters, flags);
        }

        public Task<RedisResult> ScriptEvaluateAsync(LoadedLuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.ScriptEvaluateAsync(script, parameters, flags);
        }

        public Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetAddAsync(ToInner(key), values, flags);
        }

        public Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetAddAsync(ToInner(key), value, flags);
        }

        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetCombineAndStoreAsync(operation, ToInner(destination), ToInner(keys), flags);
        }

        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetCombineAndStoreAsync(operation, ToInner(destination), ToInner(first), ToInner(second), flags);
        }

        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetCombineAsync(operation, ToInner(keys), flags);
        }

        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetCombineAsync(operation, ToInner(first), ToInner(second), flags);
        }

        public Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetContainsAsync(ToInner(key), value, flags);
        }

        public Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetLengthAsync(ToInner(key), flags);
        }

        public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetMembersAsync(ToInner(key), flags);
        }

        public Task<bool> SetMoveAsync(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetMoveAsync(ToInner(source), ToInner(destination), value, flags);
        }

        public Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetPopAsync(ToInner(key), flags);
        }

        public Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetPopAsync(ToInner(key), count, flags);
        }

        public Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetRandomMemberAsync(ToInner(key), flags);
        }

        public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetRandomMembersAsync(ToInner(key), count, flags);
        }

        public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetRemoveAsync(ToInner(key), values, flags);
        }

        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SetRemoveAsync(ToInner(key), value, flags);
        }

        public Task<long> SortAndStoreAsync(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default(RedisValue), RedisValue[] get = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortAndStoreAsync(ToInner(destination), ToInner(key), skip, take, order, sortType, SortByToInner(by), SortGetToInner(get), flags);
        }

        public Task<RedisValue[]> SortAsync(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default(RedisValue), RedisValue[] get = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortAsync(ToInner(key), skip, take, order, sortType, SortByToInner(by), SortGetToInner(get), flags);
        }

        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, CommandFlags flags)
        {
            return Inner.SortedSetAddAsync(ToInner(key), values, flags);
        }

        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetAddAsync(ToInner(key), values, when, flags);
        }

        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, CommandFlags flags)
        {
            return Inner.SortedSetAddAsync(ToInner(key), member, score, flags);
        }

        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetAddAsync(ToInner(key), member, score, when, flags);
        }

        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, double[] weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetCombineAndStoreAsync(operation, ToInner(destination), ToInner(keys), weights, aggregate, flags);
        }

        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetCombineAndStoreAsync(operation, ToInner(destination), ToInner(first), ToInner(second), aggregate, flags);
        }

        public Task<double> SortedSetDecrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetDecrementAsync(ToInner(key), member, value, flags);
        }

        public Task<double> SortedSetIncrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetIncrementAsync(ToInner(key), member, value, flags);
        }

        public Task<long> SortedSetLengthAsync(RedisKey key, double min = -1.0 / 0.0, double max = 1.0 / 0.0, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetLengthAsync(ToInner(key), min, max, exclude, flags);
        }

        public Task<long> SortedSetLengthByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetLengthByValueAsync(ToInner(key), min, max, exclude, flags);
        }

        public Task<RedisValue[]> SortedSetRangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRangeByRankAsync(ToInner(key), start, stop, order, flags);
        }

        public Task<SortedSetEntry[]> SortedSetRangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRangeByRankWithScoresAsync(ToInner(key), start, stop, order, flags);
        }

        public Task<RedisValue[]> SortedSetRangeByScoreAsync(RedisKey key, double start = -1.0 / 0.0, double stop = 1.0 / 0.0, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRangeByScoreAsync(ToInner(key), start, stop, exclude, order, skip, take, flags);
        }

        public Task<SortedSetEntry[]> SortedSetRangeByScoreWithScoresAsync(RedisKey key, double start = -1.0 / 0.0, double stop = 1.0 / 0.0, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRangeByScoreWithScoresAsync(ToInner(key), start, stop, exclude, order, skip, take, flags);
        }

        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take, CommandFlags flags)
        {
            return Inner.SortedSetRangeByValueAsync(ToInner(key), min, max, exclude, Order.Ascending, skip, take, flags);
        }

        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min = default(RedisValue), RedisValue max = default(RedisValue), Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRangeByValueAsync(ToInner(key), min, max, exclude, order, skip, take, flags);
        }

        public Task<long?> SortedSetRankAsync(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRankAsync(ToInner(key), member, order, flags);
        }

        public Task<long> SortedSetRemoveAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRemoveAsync(ToInner(key), members, flags);
        }

        public Task<bool> SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRemoveAsync(ToInner(key), member, flags);
        }

        public Task<long> SortedSetRemoveRangeByRankAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRemoveRangeByRankAsync(ToInner(key), start, stop, flags);
        }

        public Task<long> SortedSetRemoveRangeByScoreAsync(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRemoveRangeByScoreAsync(ToInner(key), start, stop, exclude, flags);
        }

        public Task<long> SortedSetRemoveRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetRemoveRangeByValueAsync(ToInner(key), min, max, exclude, flags);
        }

        public Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetScoreAsync(ToInner(key), member, flags);
        }

        public Task<SortedSetEntry?> SortedSetPopAsync(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetPopAsync(ToInner(key), order, flags);
        }

        public Task<SortedSetEntry[]> SortedSetPopAsync(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            return Inner.SortedSetPopAsync(ToInner(key), count, order, flags);
        }

        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamAcknowledgeAsync(ToInner(key), groupName, messageId, flags);
        }

        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamAcknowledgeAsync(ToInner(key), groupName, messageIds, flags);
        }

        public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamAddAsync(ToInner(key), streamField, streamValue, messageId, maxLength, useApproximateMaxLength, flags);
        }

        public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamAddAsync(ToInner(key), streamPairs, messageId, maxLength, useApproximateMaxLength, flags);
        }

        public Task<StreamEntry[]> StreamClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamClaimAsync(ToInner(key), consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags);
        }

        public Task<RedisValue[]> StreamClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamClaimIdsOnlyAsync(ToInner(key), consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags);
        }

        public Task<bool> StreamConsumerGroupSetPositionAsync(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamConsumerGroupSetPositionAsync(ToInner(key), groupName, position, flags);
        }

        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamCreateConsumerGroupAsync(ToInner(key), groupName, position, flags);
        }

        public Task<StreamInfo> StreamInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamInfoAsync(ToInner(key), flags);
        }

        public Task<StreamGroupInfo[]> StreamGroupInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamGroupInfoAsync(ToInner(key), flags);
        }

        public Task<StreamConsumerInfo[]> StreamConsumerInfoAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamConsumerInfoAsync(ToInner(key), groupName, flags);
        }

        public Task<long> StreamLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamLengthAsync(ToInner(key), flags);
        }

        public Task<long> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamDeleteAsync(ToInner(key), messageIds, flags);
        }

        public Task<long> StreamDeleteConsumerAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamDeleteConsumerAsync(ToInner(key), groupName, consumerName, flags);
        }

        public Task<bool> StreamDeleteConsumerGroupAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamDeleteConsumerGroupAsync(ToInner(key), groupName, flags);
        }

        public Task<StreamPendingInfo> StreamPendingAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamPendingAsync(ToInner(key), groupName, flags);
        }

        public Task<StreamPendingMessageInfo[]> StreamPendingMessagesAsync(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamPendingMessagesAsync(ToInner(key), groupName, count, consumerName, minId, maxId, flags);
        }

        public Task<StreamEntry[]> StreamRangeAsync(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamRangeAsync(ToInner(key), minId, maxId, count, messageOrder, flags);
        }

        public Task<StreamEntry[]> StreamReadAsync(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamReadAsync(ToInner(key), position, count, flags);
        }

        public Task<RedisStream[]> StreamReadAsync(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamReadAsync(streamPositions, countPerStream, flags);
        }

        public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamReadGroupAsync(ToInner(key), groupName, consumerName, position, count, flags);
        }

        public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamReadGroupAsync(streamPositions, groupName, consumerName, countPerStream, flags);
        }

        public Task<long> StreamTrimAsync(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StreamTrimAsync(ToInner(key), maxLength, useApproximateMaxLength, flags);
        }

        public Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringAppendAsync(ToInner(key), value, flags);
        }

        public Task<long> StringBitCountAsync(RedisKey key, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringBitCountAsync(ToInner(key), start, end, flags);
        }

        public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringBitOperationAsync(operation, ToInner(destination), ToInner(keys), flags);
        }

        public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default(RedisKey), CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringBitOperationAsync(operation, ToInner(destination), ToInner(first), ToInnerOrDefault(second), flags);
        }

        public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringBitPositionAsync(ToInner(key), bit, start, end, flags);
        }

        public Task<double> StringDecrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringDecrementAsync(ToInner(key), value, flags);
        }

        public Task<long> StringDecrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringDecrementAsync(ToInner(key), value, flags);
        }

        public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringGetAsync(ToInner(keys), flags);
        }

        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringGetAsync(ToInner(key), flags);
        }

        public Task<Lease<byte>> StringGetLeaseAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringGetLeaseAsync(ToInner(key), flags);
        }

        public Task<bool> StringGetBitAsync(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringGetBitAsync(ToInner(key), offset, flags);
        }

        public Task<RedisValue> StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringGetRangeAsync(ToInner(key), start, end, flags);
        }

        public Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringGetSetAsync(ToInner(key), value, flags);
        }

        public Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringGetWithExpiryAsync(ToInner(key), flags);
        }

        public Task<double> StringIncrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringIncrementAsync(ToInner(key), value, flags);
        }

        public Task<long> StringIncrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringIncrementAsync(ToInner(key), value, flags);
        }

        public Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringLengthAsync(ToInner(key), flags);
        }

        public Task<bool> StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringSetAsync(ToInner(values), when, flags);
        }

        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringSetAsync(ToInner(key), value, expiry, when, flags);
        }

        public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringSetBitAsync(ToInner(key), offset, bit, flags);
        }

        public Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            return Inner.StringSetRangeAsync(ToInner(key), offset, value, flags);
        }

        public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
        {
            return Inner.PingAsync(flags);
        }

        public bool TryWait(Task task)
        {
            return Inner.TryWait(task);
        }

        public TResult Wait<TResult>(Task<TResult> task)
        {
            return Inner.Wait(task);
        }

        public void Wait(Task task)
        {
            Inner.Wait(task);
        }

        public void WaitAll(params Task[] tasks)
        {
            Inner.WaitAll(tasks);
        }
        protected internal RedisKey ToInner(RedisKey outer)
        {
            return RedisKey.WithPrefix(Prefix, outer);
        }

        protected RedisKey ToInnerOrDefault(RedisKey outer)
        {
            if (outer == default(RedisKey))
            {
                return outer;
            }
            else
            {
                return ToInner(outer);
            }
        }

        protected ICollection<object> ToInner(ICollection<object> args)
        {
            if (args?.Any(x => x is RedisKey || x is RedisChannel) == true)
            {
                var withPrefix = new object[args.Count];
                int i = 0;
                foreach(var oldArg in args)
                {
                    object newArg;
                    if (oldArg is RedisKey key)
                    {
                        newArg    = ToInner(key);
                    }
                    else if (oldArg is RedisChannel channel)
                    {
                        newArg = ToInner(channel);
                    }
                    else
                    {
                        newArg = oldArg;
                    }
                    withPrefix[i++] = newArg;
                }
                args = withPrefix;
            }
            return args;
        }

        protected RedisKey[] ToInner(RedisKey[] outer)
        {
            if (outer == null || outer.Length == 0)
            {
                return outer;
            }
            else
            {
                RedisKey[] inner = new RedisKey[outer.Length];

                for (int i = 0; i < outer.Length; ++i)
                {
                    inner[i] = ToInner(outer[i]);
                }

                return inner;
            }
        }

        protected KeyValuePair<RedisKey, RedisValue> ToInner(KeyValuePair<RedisKey, RedisValue> outer)
        {
            return new KeyValuePair<RedisKey, RedisValue>(ToInner(outer.Key), outer.Value);
        }

        protected KeyValuePair<RedisKey, RedisValue>[] ToInner(KeyValuePair<RedisKey, RedisValue>[] outer)
        {
            if (outer == null || outer.Length == 0)
            {
                return outer;
            }
            else
            {
                KeyValuePair<RedisKey, RedisValue>[] inner = new KeyValuePair<RedisKey, RedisValue>[outer.Length];

                for (int i = 0; i < outer.Length; ++i)
                {
                    inner[i] = ToInner(outer[i]);
                }

                return inner;
            }
        }

        protected RedisValue ToInner(RedisValue outer)
        {
            return RedisKey.ConcatenateBytes(Prefix, null, (byte[])outer);
        }

        protected RedisValue SortByToInner(RedisValue outer)
        {
            if (outer == "nosort")
            {
                return outer;
            }
            else
            {
                return ToInner(outer);
            }
        }

        protected RedisValue SortGetToInner(RedisValue outer)
        {
            if (outer == "#")
            {
                return outer;
            }
            else
            {
                return ToInner(outer);
            }
        }

        protected RedisValue[] SortGetToInner(RedisValue[] outer)
        {
            if (outer == null || outer.Length == 0)
            {
                return outer;
            }
            else
            {
                RedisValue[] inner = new RedisValue[outer.Length];

                for (int i = 0; i < outer.Length; ++i)
                {
                    inner[i] = SortGetToInner(outer[i]);
                }

                return inner;
            }
        }

        protected RedisChannel ToInner(RedisChannel outer)
        {
            return RedisKey.ConcatenateBytes(Prefix, null, (byte[])outer);
        }

        private Func<RedisKey, RedisKey> mapFunction;
        protected Func<RedisKey, RedisKey> GetMapFunction()
        {
            // create as a delegate when first required, then re-use
            return mapFunction ?? (mapFunction = new Func<RedisKey, RedisKey>(ToInner));
        }
    }
}
