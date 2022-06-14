using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial.Arenas;

namespace StackExchange.Redis
{
    internal class RedisDatabase : RedisBase, IDatabase
    {
        internal RedisDatabase(ConnectionMultiplexer multiplexer, int db, object? asyncState)
            : base(multiplexer, asyncState)
        {
            Database = db;
        }

        public object? AsyncState => asyncState;

        public int Database { get; }

        public IBatch CreateBatch(object? asyncState)
        {
            if (this is IBatch) throw new NotSupportedException("Nested batches are not supported");
            return new RedisBatch(this, asyncState);
        }

        public ITransaction CreateTransaction(object? asyncState)
        {
            if (this is IBatch) throw new NotSupportedException("Nested transactions are not supported");
            return new RedisTransaction(this, asyncState);
        }

        private ITransaction? CreateTransactionIfAvailable(object? asyncState)
        {
            var map = multiplexer.CommandMap;
            if (!map.IsAvailable(RedisCommand.MULTI) || !map.IsAvailable(RedisCommand.EXEC))
            {
                return null;
            }
            return CreateTransaction(asyncState);
        }

        public RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.DEBUG, RedisLiterals.OBJECT, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> DebugObjectAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.DEBUG, RedisLiterals.OBJECT, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public bool GeoAdd(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            return GeoAdd(key, new GeoEntry(longitude, latitude, member), flags);
        }

        public Task<bool> GeoAddAsync(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            return GeoAddAsync(key, new GeoEntry(longitude, latitude, member), flags);
        }

        public bool GeoAdd(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOADD, key, value.Longitude, value.Latitude, value.Member);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> GeoAddAsync(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOADD, key, value.Longitude, value.Latitude, value.Member);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public long GeoAdd(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOADD, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> GeoAddAsync(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOADD, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public bool GeoRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            return SortedSetRemove(key, member, flags);
        }

        public Task<bool> GeoRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            return SortedSetRemoveAsync(key, member, flags);
        }

        public double? GeoDistance(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEODIST, key, member1, member2, StackExchange.Redis.GeoPosition.GetRedisUnit(unit));
            return ExecuteSync(msg, ResultProcessor.NullableDouble);
        }

        public Task<double?> GeoDistanceAsync(RedisKey key, RedisValue value0, RedisValue value1, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEODIST, key, value0, value1, StackExchange.Redis.GeoPosition.GetRedisUnit(unit));
            return ExecuteAsync(msg, ResultProcessor.NullableDouble);
        }

        public string?[] GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var redisValues = new RedisValue[members.Length];
            for (var i = 0; i < members.Length; i++) redisValues[i] = members[i];
            var msg = Message.Create(Database, flags, RedisCommand.GEOHASH, key, redisValues);
            return ExecuteSync(msg, ResultProcessor.NullableStringArray, defaultValue: Array.Empty<string?>());
        }

        public Task<string?[]> GeoHashAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var redisValues = new RedisValue[members.Length];
            for (var i = 0; i < members.Length; i++) redisValues[i] = members[i];
            var msg = Message.Create(Database, flags, RedisCommand.GEOHASH, key, redisValues);
            return ExecuteAsync(msg, ResultProcessor.NullableStringArray, defaultValue: Array.Empty<string?>());
        }

        public string? GeoHash(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOHASH, key, member);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        public Task<string?> GeoHashAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOHASH, key, member);
            return ExecuteAsync(msg, ResultProcessor.String);
        }

        public GeoPosition?[] GeoPosition(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var redisValues = new RedisValue[members.Length];
            for (var i = 0; i < members.Length; i++) redisValues[i] = members[i];
            var msg = Message.Create(Database, flags, RedisCommand.GEOPOS, key, redisValues);
            return ExecuteSync(msg, ResultProcessor.RedisGeoPositionArray, defaultValue: Array.Empty<GeoPosition?>());
        }

        public Task<GeoPosition?[]> GeoPositionAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var redisValues = new RedisValue[members.Length];
            for (var i = 0; i < members.Length; i++) redisValues[i] = members[i];
            var msg = Message.Create(Database, flags, RedisCommand.GEOPOS, key, redisValues);
            return ExecuteAsync(msg, ResultProcessor.RedisGeoPositionArray, defaultValue: Array.Empty<GeoPosition?>());
        }

        public GeoPosition? GeoPosition(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOPOS, key, member);
            return ExecuteSync(msg, ResultProcessor.RedisGeoPosition);
        }

        public Task<GeoPosition?> GeoPositionAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOPOS, key, member);
            return ExecuteAsync(msg, ResultProcessor.RedisGeoPosition);
        }

        private Message GetGeoSearchMessage(in RedisKey sourceKey, in RedisKey destinationKey, RedisValue? member, double longitude, double latitude, GeoSearchShape shape, int count, bool demandClosest, bool storeDistances, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            var redisValues = new List<RedisValue>(15);
            if (member != null)
            {
                redisValues.Add(RedisLiterals.FROMMEMBER);
                redisValues.Add(member.Value);
            }
            else
            {
                redisValues.Add(RedisLiterals.FROMLONLAT);
                redisValues.Add(longitude);
                redisValues.Add(latitude);
            }

            shape.AddArgs(redisValues);

            if (order != null)
            {
                redisValues.Add(order.Value.ToLiteral());
            }
            if (count >= 0)
            {
                redisValues.Add(RedisLiterals.COUNT);
                redisValues.Add(count);
            }

            if (!demandClosest)
            {
                if (count < 0)
                {
                    throw new ArgumentException($"{nameof(demandClosest)} must be true if you are not limiting the count for a GEOSEARCH");
                }
                redisValues.Add(RedisLiterals.ANY);
            }

            options.AddArgs(redisValues);

            if (storeDistances)
            {
                redisValues.Add(RedisLiterals.STOREDIST);
            }

            return destinationKey.IsNull
                ? Message.Create(Database, flags, RedisCommand.GEOSEARCH, sourceKey, redisValues.ToArray())
                : Message.Create(Database, flags, RedisCommand.GEOSEARCHSTORE, destinationKey, sourceKey, redisValues.ToArray());
        }

        private Message GetGeoRadiusMessage(in RedisKey key, RedisValue? member, double longitude, double latitude, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            var redisValues = new List<RedisValue>(10);
            RedisCommand command;
            if (member == null)
            {
                redisValues.Add(longitude);
                redisValues.Add(latitude);
                command = RedisCommand.GEORADIUS;
            }
            else
            {
                redisValues.Add(member.Value);
                command = RedisCommand.GEORADIUSBYMEMBER;
            }

            redisValues.Add(radius);
            redisValues.Add(Redis.GeoPosition.GetRedisUnit(unit));
            options.AddArgs(redisValues);

            if (count > 0)
            {
                redisValues.Add(RedisLiterals.COUNT);
                redisValues.Add(count);
            }
            if (order != null)
            {
                redisValues.Add(order.Value.ToLiteral());
            }

            return Message.Create(Database, flags, command, key, redisValues.ToArray());
        }

        public GeoRadiusResult[] GeoRadius(RedisKey key, RedisValue member, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            // This gets confused with the double overload below sometimes...throwing when this occurs.
            if (member.Type == RedisValue.StorageType.Double)
            {
                throw new ArgumentException("Member should not be a double, you likely want the GeoRadius(RedisKey, double, double, ...) overload.", nameof(member));
            }
            return ExecuteSync(GetGeoRadiusMessage(key, member, double.NaN, double.NaN, radius, unit, count, order, options, flags), ResultProcessor.GeoRadiusArray(options), defaultValue: Array.Empty<GeoRadiusResult>());
        }

        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, RedisValue member, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            // This gets confused with the double overload below sometimes...throwing when this occurs.
            if (member.Type == RedisValue.StorageType.Double)
            {
                throw new ArgumentException("Member should not be a double, you likely want the GeoRadius(RedisKey, double, double, ...) overload.", nameof(member));
            }
            return ExecuteAsync(GetGeoRadiusMessage(key, member, double.NaN, double.NaN, radius, unit, count, order, options, flags), ResultProcessor.GeoRadiusArray(options), defaultValue: Array.Empty<GeoRadiusResult>());
        }

        public GeoRadiusResult[] GeoRadius(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            return ExecuteSync(GetGeoRadiusMessage(key, null, longitude, latitude, radius, unit, count, order, options, flags), ResultProcessor.GeoRadiusArray(options), defaultValue: Array.Empty<GeoRadiusResult>());
        }

        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            return ExecuteAsync(GetGeoRadiusMessage(key, null, longitude, latitude, radius, unit, count, order, options, flags), ResultProcessor.GeoRadiusArray(options), defaultValue: Array.Empty<GeoRadiusResult>());
        }

        public GeoRadiusResult[] GeoSearch(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetGeoSearchMessage(key, RedisKey.Null, member, double.NaN, double.NaN, shape, count, demandClosest, false, order, options, flags);
            return ExecuteSync(msg, ResultProcessor.GeoRadiusArray(options), defaultValue: Array.Empty<GeoRadiusResult>());
        }

        public GeoRadiusResult[] GeoSearch(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetGeoSearchMessage(key, RedisKey.Null, null, longitude, latitude, shape, count, demandClosest, false, order, options, flags);
            return ExecuteSync(msg, ResultProcessor.GeoRadiusArray(options), defaultValue: Array.Empty<GeoRadiusResult>());
        }

        public Task<GeoRadiusResult[]> GeoSearchAsync(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetGeoSearchMessage(key, RedisKey.Null, member, double.NaN, double.NaN, shape, count, demandClosest, false, order, options, flags);
            return ExecuteAsync(msg, ResultProcessor.GeoRadiusArray(options), defaultValue: Array.Empty<GeoRadiusResult>());
        }

        public Task<GeoRadiusResult[]> GeoSearchAsync(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetGeoSearchMessage(key, RedisKey.Null, null, longitude, latitude, shape, count, demandClosest, false, order, options, flags);
            return ExecuteAsync(msg, ResultProcessor.GeoRadiusArray(options), defaultValue: Array.Empty<GeoRadiusResult>());
        }

        public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetGeoSearchMessage(sourceKey, destinationKey, member, double.NaN, double.NaN, shape, count, demandClosest, storeDistances, order, GeoRadiusOptions.None, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetGeoSearchMessage(sourceKey, destinationKey, null, longitude, latitude, shape, count, demandClosest, storeDistances, order, GeoRadiusOptions.None, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> GeoSearchAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetGeoSearchMessage(sourceKey, destinationKey, member, double.NaN, double.NaN, shape, count, demandClosest, storeDistances, order, GeoRadiusOptions.None, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> GeoSearchAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetGeoSearchMessage(sourceKey, destinationKey, null, longitude, latitude, shape, count, demandClosest, storeDistances, order, GeoRadiusOptions.None, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long HashDecrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            return HashIncrement(key, hashField, -value, flags);
        }

        public double HashDecrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        {
            return HashIncrement(key, hashField, -value, flags);
        }

        public Task<long> HashDecrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            return HashIncrementAsync(key, hashField, -value, flags);
        }

        public Task<double> HashDecrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        {
            return HashIncrementAsync(key, hashField, -value, flags);
        }

        public bool HashDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HDEL, key, hashField);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException(nameof(hashFields));
            var msg = hashFields.Length == 0 ? null : Message.Create(Database, flags, RedisCommand.HDEL, key, hashFields);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HDEL, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException(nameof(hashFields));

            var msg = hashFields.Length == 0 ? null : Message.Create(Database, flags, RedisCommand.HDEL, key, hashFields);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public bool HashExists(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HEXISTS, key, hashField);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HEXISTS, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue HashGet(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGET, key, hashField);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Lease<byte>? HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGET, key, hashField);
            return ExecuteSync(msg, ResultProcessor.Lease);
        }

        public RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException(nameof(hashFields));
            if (hashFields.Length == 0) return Array.Empty<RedisValue>();
            var msg = Message.Create(Database, flags, RedisCommand.HMGET, key, hashFields);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public HashEntry[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGETALL, key);
            return ExecuteSync(msg, ResultProcessor.HashEntryArray, defaultValue: Array.Empty<HashEntry>());
        }

        public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGETALL, key);
            return ExecuteAsync(msg, ResultProcessor.HashEntryArray, defaultValue: Array.Empty<HashEntry>());
        }

        public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGET, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<Lease<byte>?> HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGET, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.Lease);
        }

        public Task<RedisValue[]> HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException(nameof(hashFields));
            if (hashFields.Length == 0) return CompletedTask<RedisValue[]>.FromDefault(Array.Empty<RedisValue>(), asyncState);
            var msg = Message.Create(Database, flags, RedisCommand.HMGET, key, hashFields);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public long HashIncrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Database, flags, RedisCommand.HINCRBY, key, hashField, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public double HashIncrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Database, flags, RedisCommand.HINCRBYFLOAT, key, hashField, value);
            return ExecuteSync(msg, ResultProcessor.Double);
        }

        public Task<long> HashIncrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Database, flags, RedisCommand.HINCRBY, key, hashField, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<double> HashIncrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Database, flags, RedisCommand.HINCRBYFLOAT, key, hashField, value);
            return ExecuteAsync(msg, ResultProcessor.Double);
        }

        public RedisValue[] HashKeys(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HKEYS, key);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HKEYS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HLEN, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public RedisValue HashRandomField(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HRANDFIELD, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] HashRandomFields(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HRANDFIELD, key, count);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public HashEntry[] HashRandomFieldsWithValues(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HRANDFIELD, key, count, RedisLiterals.WITHVALUES);
            return ExecuteSync(msg, ResultProcessor.HashEntryArray, defaultValue: Array.Empty<HashEntry>());
        }

        public Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HLEN, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<RedisValue> HashRandomFieldAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HRANDFIELD, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue[]> HashRandomFieldsAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HRANDFIELD, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<HashEntry[]> HashRandomFieldsWithValuesAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HRANDFIELD, key, count, RedisLiterals.WITHVALUES);
            return ExecuteAsync(msg, ResultProcessor.HashEntryArray, defaultValue: Array.Empty<HashEntry>());
        }

        IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            => HashScanAsync(key, pattern, pageSize, CursorUtils.Origin, 0, flags);

        IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

        IAsyncEnumerable<HashEntry> IDatabaseAsync.HashScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

        private CursorEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            var scan = TryScan<HashEntry>(key, pattern, pageSize, cursor, pageOffset, flags, RedisCommand.HSCAN, HashScanResultProcessor.Default, out var server);
            if (scan != null) return scan;

            if (cursor != 0) throw ExceptionFactory.NoCursor(RedisCommand.HGETALL);

            if (pattern.IsNull) return CursorEnumerable<HashEntry>.From(this, server, HashGetAllAsync(key, flags), pageOffset);
            throw ExceptionFactory.NotSupported(true, RedisCommand.HSCAN);
        }

        public bool HashSet(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrNotExists(when);
            var msg = value.IsNull
                ? Message.Create(Database, flags, RedisCommand.HDEL, key, hashField)
                : Message.Create(Database, flags, when == When.Always ? RedisCommand.HSET : RedisCommand.HSETNX, key, hashField, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public void HashSet(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetHashSetMessage(key, hashFields, flags);
            if (msg == null) return;
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public long HashStringLength(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HSTRLEN, key, hashField);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrNotExists(when);
            var msg = value.IsNull
                ? Message.Create(Database, flags, RedisCommand.HDEL, key, hashField)
                : Message.Create(Database, flags, when == When.Always ? RedisCommand.HSET : RedisCommand.HSETNX, key, hashField, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> HashStringLengthAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HSTRLEN, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task HashSetAsync(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetHashSetMessage(key, hashFields, flags);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public Task<bool> HashSetIfNotExistsAsync(RedisKey key, RedisValue hashField, RedisValue value, CommandFlags flags)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HSETNX, key, hashField, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue[] HashValues(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HVALS, key);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HVALS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public bool HyperLogLogAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var cmd = Message.Create(Database, flags, RedisCommand.PFADD, key, value);
            return ExecuteSync(cmd, ResultProcessor.Boolean);
        }

        public bool HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            var cmd = Message.Create(Database, flags, RedisCommand.PFADD, key, values);
            return ExecuteSync(cmd, ResultProcessor.Boolean);
        }

        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var cmd = Message.Create(Database, flags, RedisCommand.PFADD, key, value);
            return ExecuteAsync(cmd, ResultProcessor.Boolean);
        }

        public Task<bool> HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            var cmd = Message.Create(Database, flags, RedisCommand.PFADD, key, values);
            return ExecuteAsync(cmd, ResultProcessor.Boolean);
        }

        public long HyperLogLogLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var features = GetFeatures(key, flags, out ServerEndPoint? server);
            var cmd = Message.Create(Database, flags, RedisCommand.PFCOUNT, key);
            // technically a write / primary-only command until 2.8.18
            if (server != null && !features.HyperLogLogCountReplicaSafe) cmd.SetPrimaryOnly();
            return ExecuteSync(cmd, ResultProcessor.Int64, server);
        }

        public long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            ServerEndPoint? server = null;
            var cmd = Message.Create(Database, flags, RedisCommand.PFCOUNT, keys);
            if (keys.Length != 0)
            {
                var features = GetFeatures(keys[0], flags, out server);
                // technically a write / primary-only command until 2.8.18
                if (server != null && !features.HyperLogLogCountReplicaSafe) cmd.SetPrimaryOnly();
            }
            return ExecuteSync(cmd, ResultProcessor.Int64, server);
        }

        public Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var features = GetFeatures(key, flags, out ServerEndPoint? server);
            var cmd = Message.Create(Database, flags, RedisCommand.PFCOUNT, key);
            // technically a write / primary-only command until 2.8.18
            if (server != null && !features.HyperLogLogCountReplicaSafe) cmd.SetPrimaryOnly();
            return ExecuteAsync(cmd, ResultProcessor.Int64, server);
        }

        public Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            ServerEndPoint? server = null;
            var cmd = Message.Create(Database, flags, RedisCommand.PFCOUNT, keys);
            if (keys.Length != 0)
            {
                var features = GetFeatures(keys[0], flags, out server);
                // technically a write / primary-only command until 2.8.18
                if (server != null && !features.HyperLogLogCountReplicaSafe) cmd.SetPrimaryOnly();
            }
            return ExecuteAsync(cmd, ResultProcessor.Int64, server);
        }

        public void HyperLogLogMerge(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var cmd = Message.Create(Database, flags, RedisCommand.PFMERGE, destination, first, second);
            ExecuteSync(cmd, ResultProcessor.DemandOK);
        }

        public void HyperLogLogMerge(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
        {
            var cmd = Message.Create(Database, flags, RedisCommand.PFMERGE, destination, sourceKeys);
            ExecuteSync(cmd, ResultProcessor.DemandOK);
        }

        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var cmd = Message.Create(Database, flags, RedisCommand.PFMERGE, destination, first, second);
            return ExecuteAsync(cmd, ResultProcessor.DemandOK);
        }

        public Task HyperLogLogMergeAsync(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None)
        {
            var cmd = Message.Create(Database, flags, RedisCommand.PFMERGE, destination, sourceKeys);
            return ExecuteAsync(cmd, ResultProcessor.DemandOK);
        }

        public EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None)
        {
            var msg = key.IsNull ? Message.Create(-1, flags, RedisCommand.PING) : Message.Create(Database, flags, RedisCommand.EXISTS, key);
            return ExecuteSync(msg, ResultProcessor.ConnectionIdentity);
        }

        public Task<EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None)
        {
            var msg = key.IsNull ? Message.Create(-1, flags, RedisCommand.PING) : Message.Create(Database, flags, RedisCommand.EXISTS, key);
            return ExecuteAsync(msg, ResultProcessor.ConnectionIdentity);
        }

        public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var server = multiplexer.SelectServer(RedisCommand.PING, flags, key);
            return server?.IsConnected == true;
        }

        public bool KeyCopy(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetCopyMessage(sourceKey, destinationKey, destinationDatabase, replace, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> KeyCopyAsync(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetCopyMessage(sourceKey, destinationKey, destinationDatabase, replace, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var cmd = GetDeleteCommand(key, flags, out var server);
            var msg = Message.Create(Database, flags, cmd, key);
            return ExecuteSync(msg, ResultProcessor.DemandZeroOrOne, server);
        }

        public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length > 0)
            {
                var cmd = GetDeleteCommand(keys[0], flags, out var server);
                var msg = keys.Length == 0 ? null : Message.Create(Database, flags, cmd, keys);
                return ExecuteSync(msg, ResultProcessor.Int64, server);
            }
            return 0;
        }

        public Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var cmd = GetDeleteCommand(key, flags, out var server);
            var msg = Message.Create(Database, flags, cmd, key);
            return ExecuteAsync(msg, ResultProcessor.DemandZeroOrOne, server);
        }

        public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length > 0)
            {
                var cmd = GetDeleteCommand(keys[0], flags, out var server);
                var msg = keys.Length == 0 ? null : Message.Create(Database, flags, cmd, keys);
                return ExecuteAsync(msg, ResultProcessor.Int64, server);
            }
            return CompletedTask<long>.Default(0);
        }

        private RedisCommand GetDeleteCommand(RedisKey key, CommandFlags flags, out ServerEndPoint? server)
        {
            var features = GetFeatures(key, flags, out server);
            if (server != null && features.Unlink && multiplexer.CommandMap.IsAvailable(RedisCommand.UNLINK))
            {
                return RedisCommand.UNLINK;
            }
            return RedisCommand.DEL;
        }

        public byte[]? KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.DUMP, key);
            return ExecuteSync(msg, ResultProcessor.ByteArray);
        }

        public Task<byte[]?> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.DUMP, key);
            return ExecuteAsync(msg, ResultProcessor.ByteArray);
        }

        public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.OBJECT, RedisLiterals.ENCODING, key);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        public Task<string?> KeyEncodingAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.OBJECT, RedisLiterals.ENCODING, key);
            return ExecuteAsync(msg, ResultProcessor.String);
        }

        public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.EXISTS, key);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.EXISTS, keys);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.EXISTS, key);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.EXISTS, keys);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None) =>
            KeyExpire(key, expiry, ExpireWhen.Always, flags);

        public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags = CommandFlags.None) =>
            KeyExpire(key, expiry, ExpireWhen.Always, flags);

        public bool KeyExpire(RedisKey key, TimeSpan? expiry, ExpireWhen when, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetExpiryMessage(key, flags, expiry, when, out ServerEndPoint? server);
            return ExecuteSync(msg, ResultProcessor.Boolean, server: server);
        }

        public bool KeyExpire(RedisKey key, DateTime? expiry, ExpireWhen when, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetExpiryMessage(key, flags, expiry, when, out ServerEndPoint? server);
            return ExecuteSync(msg, ResultProcessor.Boolean, server: server);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None) =>
            KeyExpireAsync(key, expiry, ExpireWhen.Always, flags);

        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags = CommandFlags.None) =>
            KeyExpireAsync(key, expiry, ExpireWhen.Always, flags);

        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, ExpireWhen when, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetExpiryMessage(key, flags, expiry, when, out ServerEndPoint? server);
            return ExecuteAsync(msg, ResultProcessor.Boolean, server: server);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expire, ExpireWhen when, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetExpiryMessage(key, flags, expire, when, out ServerEndPoint? server);
            return ExecuteAsync(msg, ResultProcessor.Boolean, server: server);
        }

        public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.PEXPIRETIME, key);
            return ExecuteSync(msg, ResultProcessor.NullableDateTimeFromMilliseconds);
        }

        public Task<DateTime?> KeyExpireTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.PEXPIRETIME, key);
            return ExecuteAsync(msg, ResultProcessor.NullableDateTimeFromMilliseconds);
        }

        public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.OBJECT, RedisLiterals.FREQ, key);
            return ExecuteSync(msg, ResultProcessor.NullableInt64);
        }

        public Task<long?> KeyFrequencyAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.OBJECT, RedisLiterals.FREQ, key);
            return ExecuteAsync(msg, ResultProcessor.NullableInt64);
        }

        public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.OBJECT, RedisLiterals.IDLETIME, key);
            return ExecuteSync(msg, ResultProcessor.TimeSpanFromSeconds);
        }

        public Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.OBJECT, RedisLiterals.IDLETIME, key);
            return ExecuteAsync(msg, ResultProcessor.TimeSpanFromSeconds);
        }

        public void KeyMigrate(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None)
        {
            if (timeoutMilliseconds <= 0) timeoutMilliseconds = multiplexer.TimeoutMilliseconds;
            var msg = new KeyMigrateCommandMessage(Database, key, toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task KeyMigrateAsync(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None)
        {
            if (timeoutMilliseconds <= 0) timeoutMilliseconds = multiplexer.TimeoutMilliseconds;
            var msg = new KeyMigrateCommandMessage(Database, key, toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        private sealed class KeyMigrateCommandMessage : Message.CommandKeyBase // MIGRATE is atypical
        {
            private readonly MigrateOptions migrateOptions;
            private readonly int timeoutMilliseconds;
            private readonly int toDatabase;
            private readonly RedisValue toHost, toPort;

            public KeyMigrateCommandMessage(int db, RedisKey key, EndPoint toServer, int toDatabase, int timeoutMilliseconds, MigrateOptions migrateOptions, CommandFlags flags)
                : base(db, flags, RedisCommand.MIGRATE, key)
            {
                if (toServer == null) throw new ArgumentNullException(nameof(toServer));
                if (!Format.TryGetHostPort(toServer, out string? toHost, out int? toPort)) throw new ArgumentException($"Couldn't get host and port from {toServer}", nameof(toServer));
                this.toHost = toHost;
                this.toPort = toPort;
                if (toDatabase < 0) throw new ArgumentOutOfRangeException(nameof(toDatabase));
                this.toDatabase = toDatabase;
                this.timeoutMilliseconds = timeoutMilliseconds;
                this.migrateOptions = migrateOptions;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                bool isCopy = (migrateOptions & MigrateOptions.Copy) != 0;
                bool isReplace = (migrateOptions & MigrateOptions.Replace) != 0;
                physical.WriteHeader(Command, 5 + (isCopy ? 1 : 0) + (isReplace ? 1 : 0));
                physical.WriteBulkString(toHost);
                physical.WriteBulkString(toPort);
                physical.Write(Key);
                physical.WriteBulkString(toDatabase);
                physical.WriteBulkString(timeoutMilliseconds);
                if (isCopy) physical.WriteBulkString(RedisLiterals.COPY);
                if (isReplace) physical.WriteBulkString(RedisLiterals.REPLACE);
            }

            public override int ArgCount
            {
                get
                {
                    bool isCopy = (migrateOptions & MigrateOptions.Copy) != 0;
                    bool isReplace = (migrateOptions & MigrateOptions.Replace) != 0;
                    return 5 + (isCopy ? 1 : 0) + (isReplace ? 1 : 0);
                }
            }
        }

        public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.MOVE, key, database);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.MOVE, key, database);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.PERSIST, key);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.PERSIST, key);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RANDOMKEY);
            return ExecuteSync(msg, ResultProcessor.RedisKey);
        }

        public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RANDOMKEY);
            return ExecuteAsync(msg, ResultProcessor.RedisKey);
        }

        public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.OBJECT, RedisLiterals.REFCOUNT, key);
            return ExecuteSync(msg, ResultProcessor.NullableInt64);
        }

        public Task<long?> KeyRefCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.OBJECT, RedisLiterals.REFCOUNT, key);
            return ExecuteAsync(msg, ResultProcessor.NullableInt64);
        }

        public bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrNotExists(when);
            var msg = Message.Create(Database, flags, when == When.Always ? RedisCommand.RENAME : RedisCommand.RENAMENX, key, newKey);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> KeyRenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrNotExists(when);
            var msg = Message.Create(Database, flags, when == When.Always ? RedisCommand.RENAME : RedisCommand.RENAMENX, key, newKey);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public void KeyRestore(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetRestoreMessage(key, value, expiry, flags);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task KeyRestoreAsync(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetRestoreMessage(key, value, expiry, flags);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var features = GetFeatures(key, flags, out ServerEndPoint? server);
            Message msg;
            if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PTTL))
            {
                msg = Message.Create(Database, flags, RedisCommand.PTTL, key);
                return ExecuteSync(msg, ResultProcessor.TimeSpanFromMilliseconds, server);
            }
            msg = Message.Create(Database, flags, RedisCommand.TTL, key);
            return ExecuteSync(msg, ResultProcessor.TimeSpanFromSeconds);
        }

        public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var features = GetFeatures(key, flags, out ServerEndPoint? server);
            Message msg;
            if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PTTL))
            {
                msg = Message.Create(Database, flags, RedisCommand.PTTL, key);
                return ExecuteAsync(msg, ResultProcessor.TimeSpanFromMilliseconds, server);
            }
            msg = Message.Create(Database, flags, RedisCommand.TTL, key);
            return ExecuteAsync(msg, ResultProcessor.TimeSpanFromSeconds);
        }

        public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.TYPE, key);
            return ExecuteSync(msg, ResultProcessor.RedisType);
        }

        public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags)
        {
            var msg = Message.Create(Database, flags, RedisCommand.TYPE, key);
            return ExecuteAsync(msg, ResultProcessor.RedisType);
        }

        public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LINDEX, key, index);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LINDEX, key, index);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public long ListInsertAfter(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LINSERT, key, RedisLiterals.AFTER, pivot, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListInsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LINSERT, key, RedisLiterals.AFTER, pivot, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long ListInsertBefore(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LINSERT, key, RedisLiterals.BEFORE, pivot, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListInsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LINSERT, key, RedisLiterals.BEFORE, pivot, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LPOP, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] ListLeftPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LPOP, key, count);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public ListPopResult ListLeftPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetListMultiPopMessage(keys, RedisLiterals.LEFT, count, flags);
            return ExecuteSync(msg, ResultProcessor.ListPopResult, defaultValue: ListPopResult.Null);
        }

        public long ListPosition(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = CreateListPositionMessage(Database, flags, key, element, rank, maxLength);
            return ExecuteSync(msg, ResultProcessor.Int64DefaultNegativeOne, defaultValue: -1);
        }

        public long[] ListPositions(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = CreateListPositionMessage(Database, flags, key, element, rank, maxLength, count);
            return ExecuteSync(msg, ResultProcessor.Int64Array, defaultValue: Array.Empty<long>());
        }

        public Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LPOP, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue[]> ListLeftPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LPOP, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<ListPopResult> ListLeftPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetListMultiPopMessage(keys, RedisLiterals.LEFT, count, flags);
            return ExecuteAsync(msg, ResultProcessor.ListPopResult, defaultValue: ListPopResult.Null);
        }

        public Task<long> ListPositionAsync(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = CreateListPositionMessage(Database, flags, key, element, rank, maxLength);
            return ExecuteAsync(msg, ResultProcessor.Int64DefaultNegativeOne, defaultValue: -1);
        }

        public Task<long[]> ListPositionsAsync(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = CreateListPositionMessage(Database, flags, key, element, rank, maxLength, count);
            return ExecuteAsync(msg, ResultProcessor.Int64Array, defaultValue: Array.Empty<long>());
        }

        public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Database, flags, when == When.Always ? RedisCommand.LPUSH : RedisCommand.LPUSHX, key, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long ListLeftPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            if (values == null) throw new ArgumentNullException(nameof(values));
            var command = when == When.Always ? RedisCommand.LPUSH : RedisCommand.LPUSHX;
            var msg = values.Length == 0 ? Message.Create(Database, flags, RedisCommand.LLEN, key) : Message.Create(Database, flags, command, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var msg = values.Length == 0 ? Message.Create(Database, flags, RedisCommand.LLEN, key) : Message.Create(Database, flags, RedisCommand.LPUSH, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Database, flags, when == When.Always ? RedisCommand.LPUSH : RedisCommand.LPUSHX, key, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            if (values == null) throw new ArgumentNullException(nameof(values));
            var command = when == When.Always ? RedisCommand.LPUSH : RedisCommand.LPUSHX;
            var msg = values.Length == 0 ? Message.Create(Database, flags, RedisCommand.LLEN, key) : Message.Create(Database, flags, command, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var msg = values.Length == 0 ? Message.Create(Database, flags, RedisCommand.LLEN, key) : Message.Create(Database, flags, RedisCommand.LPUSH, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LLEN, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LLEN, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue ListMove(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LMOVE, sourceKey, destinationKey, sourceSide.ToLiteral(), destinationSide.ToLiteral());
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> ListMoveAsync(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LMOVE, sourceKey, destinationKey, sourceSide.ToLiteral(), destinationSide.ToLiteral());
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LRANGE, key, start, stop);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LRANGE, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public long ListRemove(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LREM, key, count, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListRemoveAsync(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LREM, key, count, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RPOP, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] ListRightPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RPOP, key, count);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public ListPopResult ListRightPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetListMultiPopMessage(keys, RedisLiterals.RIGHT, count, flags);
            return ExecuteSync(msg, ResultProcessor.ListPopResult, defaultValue: ListPopResult.Null);
        }

        public Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RPOP, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue[]> ListRightPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RPOP, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<ListPopResult> ListRightPopAsync(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetListMultiPopMessage(keys, RedisLiterals.RIGHT, count, flags);
            return ExecuteAsync(msg, ResultProcessor.ListPopResult, defaultValue: ListPopResult.Null);
        }

        public RedisValue ListRightPopLeftPush(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RPOPLPUSH, source, destination);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> ListRightPopLeftPushAsync(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RPOPLPUSH, source, destination);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public long ListRightPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Database, flags, when == When.Always ? RedisCommand.RPUSH : RedisCommand.RPUSHX, key, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long ListRightPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            if (values == null) throw new ArgumentNullException(nameof(values));
            var command = when == When.Always ? RedisCommand.RPUSH : RedisCommand.RPUSHX;
            var msg = values.Length == 0 ? Message.Create(Database, flags, RedisCommand.LLEN, key) : Message.Create(Database, flags, command, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var msg = values.Length == 0 ? Message.Create(Database, flags, RedisCommand.LLEN, key) : Message.Create(Database, flags, RedisCommand.RPUSH, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListRightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Database, flags, when == When.Always ? RedisCommand.RPUSH : RedisCommand.RPUSHX, key, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            if (values == null) throw new ArgumentNullException(nameof(values));
            var command = when == When.Always ? RedisCommand.RPUSH : RedisCommand.RPUSHX;
            var msg = values.Length == 0 ? Message.Create(Database, flags, RedisCommand.LLEN, key) : Message.Create(Database, flags, command, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var msg = values.Length == 0 ? Message.Create(Database, flags, RedisCommand.LLEN, key) : Message.Create(Database, flags, RedisCommand.RPUSH, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public void ListSetByIndex(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LSET, key, index, value);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ListSetByIndexAsync(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LSET, key, index, value);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public void ListTrim(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LTRIM, key, start, stop);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LTRIM, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            if (value.IsNull) throw new ArgumentNullException(nameof(value));
            var tran = GetLockExtendTransaction(key, value, expiry);

            if (tran != null) return tran.Execute(flags);

            // without transactions (twemproxy etc), we can't enforce the "value" part
            return KeyExpire(key, expiry, flags);
        }

        public Task<bool> LockExtendAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            if (value.IsNull) throw new ArgumentNullException(nameof(value));
            var tran = GetLockExtendTransaction(key, value, expiry);
            if (tran != null) return tran.ExecuteAsync(flags);

            // without transactions (twemproxy etc), we can't enforce the "value" part
            return KeyExpireAsync(key, expiry, flags);
        }

        public RedisValue LockQuery(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return StringGet(key, flags);
        }

        public Task<RedisValue> LockQueryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            return StringGetAsync(key, flags);
        }

        public bool LockRelease(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            if (value.IsNull) throw new ArgumentNullException(nameof(value));
            var tran = GetLockReleaseTransaction(key, value);
            if (tran != null) return tran.Execute(flags);

            // without transactions (twemproxy etc), we can't enforce the "value" part
            return KeyDelete(key, flags);
        }

        public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            if (value.IsNull) throw new ArgumentNullException(nameof(value));
            var tran = GetLockReleaseTransaction(key, value);
            if (tran != null) return tran.ExecuteAsync(flags);

            // without transactions (twemproxy etc), we can't enforce the "value" part
            return KeyDeleteAsync(key, flags);
        }

        public bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            if (value.IsNull) throw new ArgumentNullException(nameof(value));
            return StringSet(key, value, expiry, When.NotExists, flags);
        }

        public Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            if (value.IsNull) throw new ArgumentNullException(nameof(value));
            return StringSetAsync(key, value, expiry, When.NotExists, flags);
        }

        public string? StringLongestCommonSubsequence(RedisKey key1, RedisKey key2, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LCS, key1, key2);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        public Task<string?> StringLongestCommonSubsequenceAsync(RedisKey key1, RedisKey key2, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LCS, key1, key2);
            return ExecuteAsync(msg, ResultProcessor.String);
        }

        public long StringLongestCommonSubsequenceLength(RedisKey key1, RedisKey key2, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LCS, key1, key2, RedisLiterals.LEN);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringLongestCommonSubsequenceLengthAsync(RedisKey key1, RedisKey key2, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LCS, key1, key2, RedisLiterals.LEN);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public LCSMatchResult StringLongestCommonSubsequenceWithMatches(RedisKey key1, RedisKey key2, long minSubMatchLength = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LCS, key1, key2, RedisLiterals.IDX, RedisLiterals.MINMATCHLEN, minSubMatchLength, RedisLiterals.WITHMATCHLEN);
            return ExecuteSync(msg, ResultProcessor.LCSMatchResult);
        }

        public Task<LCSMatchResult> StringLongestCommonSubsequenceWithMatchesAsync(RedisKey key1, RedisKey key2, long minSubMatchLength = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LCS, key1, key2, RedisLiterals.IDX, RedisLiterals.MINMATCHLEN, minSubMatchLength, RedisLiterals.WITHMATCHLEN);
            return ExecuteAsync(msg, ResultProcessor.LCSMatchResult);
        }

        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            if (channel.IsNullOrEmpty) throw new ArgumentNullException(nameof(channel));
            var msg = Message.Create(-1, flags, RedisCommand.PUBLISH, channel, message);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            if (channel.IsNullOrEmpty) throw new ArgumentNullException(nameof(channel));
            var msg = Message.Create(-1, flags, RedisCommand.PUBLISH, channel, message);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisResult ScriptEvaluate(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Database, flags, script, keys, values);
            try
            {
                return ExecuteSync(msg, ResultProcessor.ScriptResult, defaultValue: RedisResult.NullSingle);
            }
            catch (RedisServerException) when (msg.IsScriptUnavailable)
            {
                // could be a NOSCRIPT; for a sync call, we can re-issue that without problem
                return ExecuteSync(msg, ResultProcessor.ScriptResult, defaultValue: RedisResult.NullSingle);
            }
        }

        public RedisResult Execute(string command, params object[] args)
            => Execute(command, args, CommandFlags.None);

        public RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ExecuteMessage(multiplexer?.CommandMap, Database, flags, command, args);
            return ExecuteSync(msg, ResultProcessor.ScriptResult)!;
        }

        public Task<RedisResult> ExecuteAsync(string command, params object[] args)
            => ExecuteAsync(command, args, CommandFlags.None);

        public Task<RedisResult> ExecuteAsync(string command, ICollection<object>? args, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ExecuteMessage(multiplexer?.CommandMap, Database, flags, command, args);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult, defaultValue: RedisResult.NullSingle);
        }

        public RedisResult ScriptEvaluate(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Database, flags, hash, keys, values);
            return ExecuteSync(msg, ResultProcessor.ScriptResult, defaultValue: RedisResult.NullSingle);
        }

        public RedisResult ScriptEvaluate(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return script.Evaluate(this, parameters, null, flags);
        }

        public RedisResult ScriptEvaluate(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return script.Evaluate(this, parameters, null, flags);
        }

        public Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Database, flags, script, keys, values);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult, defaultValue: RedisResult.NullSingle);
        }

        public Task<RedisResult> ScriptEvaluateAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Database, flags, hash, keys, values);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult, defaultValue: RedisResult.NullSingle);
        }

        public Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return script.EvaluateAsync(this, parameters, null, flags);
        }

        public Task<RedisResult> ScriptEvaluateAsync(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return script.EvaluateAsync(this, parameters, null, flags);
        }

        public bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SADD, key, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values.Length == 0) return 0;
            var msg = Message.Create(Database, flags, RedisCommand.SADD, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SADD, key, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values.Length == 0) return Task.FromResult<long>(0);
            var msg = Message.Create(Database, flags, RedisCommand.SADD, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue[] SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, false), first, second);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, false), keys);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, true), destination, first, second);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, true), destination, keys);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, true), destination, first, second);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, true), destination, keys);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, false), first, second);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, false), keys);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SISMEMBER, key, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SISMEMBER, key, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public bool[] SetContains(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SMISMEMBER, key, values);
            return ExecuteSync(msg, ResultProcessor.BooleanArray, defaultValue: Array.Empty<bool>());
        }

        public Task<bool[]> SetContainsAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SMISMEMBER, key, values);
            return ExecuteAsync(msg, ResultProcessor.BooleanArray, defaultValue: Array.Empty<bool>());
        }

        public long SetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSetIntersectionLengthMessage(keys, limit, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSetIntersectionLengthMessage(keys, limit, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SCARD, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SCARD, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SMEMBERS, key);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SMEMBERS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public bool SetMove(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SMOVE, source, destination, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> SetMoveAsync(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SMOVE, source, destination, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SPOP, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SPOP, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] SetPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            if (count == 0) return Array.Empty<RedisValue>();
            var msg = count == 1
                    ? Message.Create(Database, flags, RedisCommand.SPOP, key)
                    : Message.Create(Database, flags, RedisCommand.SPOP, key, count);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            if(count == 0) return CompletedTask<RedisValue[]>.FromDefault(Array.Empty<RedisValue>(), asyncState);
            var msg = count == 1
                    ? Message.Create(Database, flags, RedisCommand.SPOP, key)
                    : Message.Create(Database, flags, RedisCommand.SPOP, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SRANDMEMBER, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SRANDMEMBER, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SRANDMEMBER, key, count);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SRANDMEMBER, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SREM, key, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0) return 0;
            var msg = Message.Create(Database, flags, RedisCommand.SREM, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SREM, key, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0) return CompletedTask<long>.FromResult(0, asyncState);
            var msg = Message.Create(Database, flags, RedisCommand.SREM, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        IEnumerable<RedisValue> IDatabase.SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            => SetScanAsync(key, pattern, pageSize, CursorUtils.Origin, 0, flags);

        IEnumerable<RedisValue> IDatabase.SetScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

        IAsyncEnumerable<RedisValue> IDatabaseAsync.SetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

        private CursorEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            var scan = TryScan<RedisValue>(key, pattern, pageSize, cursor, pageOffset, flags, RedisCommand.SSCAN, SetScanResultProcessor.Default, out var server);
            if (scan != null) return scan;

            if (cursor != 0) throw ExceptionFactory.NoCursor(RedisCommand.SMEMBERS);
            if (pattern.IsNull) return CursorEnumerable<RedisValue>.From(this, server, SetMembersAsync(key, flags), pageOffset);
            throw ExceptionFactory.NotSupported(true, RedisCommand.SSCAN);
        }

        public RedisValue[] Sort(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortMessage(RedisKey.Null, key, skip, take, order, sortType, by, get, flags, out var server);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, server: server, defaultValue: Array.Empty<RedisValue>());
        }

        public long SortAndStore(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortMessage(destination, key, skip, take, order, sortType, by, get, flags, out var server);
            return ExecuteSync(msg, ResultProcessor.Int64, server);
        }

        public Task<long> SortAndStoreAsync(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortMessage(destination, key, skip, take, order, sortType, by, get, flags, out var server);
            return ExecuteAsync(msg, ResultProcessor.Int64, server);
        }

        public Task<RedisValue[]> SortAsync(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortMessage(RedisKey.Null, key, skip, take, order, sortType, by, get, flags, out var server);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>(), server: server);
        }

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags) =>
            SortedSetAdd(key, member, score, SortedSetWhen.Always, flags);

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            SortedSetAdd(key, member, score, SortedSetWhenExtensions.Parse(when),  flags);

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, member, score, when, false, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public bool SortedSetUpdate(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, member, score, when, true, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags) =>
            SortedSetAdd(key, values, SortedSetWhen.Always, flags);

        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            SortedSetAdd(key, values, SortedSetWhenExtensions.Parse(when), flags);

        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, values, when, false, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long SortedSetUpdate(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, values, when, true, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, CommandFlags flags) =>
            SortedSetAddAsync(key, member, score, SortedSetWhen.Always, flags);

        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            SortedSetAddAsync(key, member, score, SortedSetWhenExtensions.Parse(when), flags);

        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, member, score, when, false, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> SortedSetUpdateAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, member, score, when, true, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, CommandFlags flags) =>
            SortedSetAddAsync(key, values, SortedSetWhen.Always, flags);

        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            SortedSetAddAsync(key, values, SortedSetWhenExtensions.Parse(when), flags);

        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, values, when, false, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetUpdateAsync(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, values, when, true, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue[] SortedSetCombine(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineCommandMessage(operation, keys, weights, aggregate, withScores: false, flags);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> SortedSetCombineAsync(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineCommandMessage(operation, keys, weights, aggregate, withScores: false, flags);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public SortedSetEntry[] SortedSetCombineWithScores(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineCommandMessage(operation, keys, weights, aggregate, withScores: true, flags);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public Task<SortedSetEntry[]> SortedSetCombineWithScoresAsync(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineCommandMessage(operation, keys, weights, aggregate, withScores: true, flags);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineAndStoreCommandMessage(operation, destination, new[] { first, second }, null, aggregate, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineAndStoreCommandMessage(operation, destination, keys, weights, aggregate, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineAndStoreCommandMessage(operation, destination, new[] { first, second }, null, aggregate, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineAndStoreCommandMessage(operation, destination, keys, weights, aggregate, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public double SortedSetDecrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        {
            return SortedSetIncrement(key, member, -value, flags);
        }

        public Task<double> SortedSetDecrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        {
            return SortedSetIncrementAsync(key, member, -value, flags);
        }

        public double SortedSetIncrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZINCRBY, key, value, member);
            return ExecuteSync(msg, ResultProcessor.Double);
        }

        public Task<double> SortedSetIncrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZINCRBY, key, value, member);
            return ExecuteAsync(msg, ResultProcessor.Double);
        }

        public long SortedSetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetIntersectionLengthMessage(keys, limit, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetIntersectionLengthMessage(keys, limit, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long SortedSetLength(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetLengthMessage(key, min, max, exclude, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetLengthAsync(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetLengthMessage(key, min, max, exclude, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue SortedSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZRANDMEMBER, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] SortedSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZRANDMEMBER, key, count);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public SortedSetEntry[] SortedSetRandomMembersWithScores(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZRANDMEMBER, key, count, RedisLiterals.WITHSCORES);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public Task<RedisValue> SortedSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZRANDMEMBER, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue[]> SortedSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZRANDMEMBER, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<SortedSetEntry[]> SortedSetRandomMembersWithScoresAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZRANDMEMBER, key, count, RedisLiterals.WITHSCORES);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public RedisValue[] SortedSetRangeByRank(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public long SortedSetRangeAndStore(
            RedisKey sourceKey,
            RedisKey destinationKey,
            RedisValue start,
            RedisValue stop,
            SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long? take = null,
            CommandFlags flags = CommandFlags.None)
        {
            var msg = CreateSortedSetRangeStoreMessage(Database, flags, sourceKey, destinationKey, start, stop, sortedSetOrder, order, exclude, skip, take);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<RedisValue[]> SortedSetRangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<long> SortedSetRangeAndStoreAsync(
            RedisKey sourceKey,
            RedisKey destinationKey,
            RedisValue start,
            RedisValue stop,
            SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long? take = null,
            CommandFlags flags = CommandFlags.None)
        {
            var msg = CreateSortedSetRangeStoreMessage(Database, flags, sourceKey, destinationKey, start, stop, sortedSetOrder, order, exclude, skip, take);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public SortedSetEntry[] SortedSetRangeByRankWithScores(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop, RedisLiterals.WITHSCORES);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public Task<SortedSetEntry[]> SortedSetRangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop, RedisLiterals.WITHSCORES);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public RedisValue[] SortedSetRangeByScore(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, false);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> SortedSetRangeByScoreAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, false);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public SortedSetEntry[] SortedSetRangeByScoreWithScores(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, true);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public Task<SortedSetEntry[]> SortedSetRangeByScoreWithScoresAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, true);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public long? SortedSetRank(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANK : RedisCommand.ZRANK, key, member);
            return ExecuteSync(msg, ResultProcessor.NullableInt64);
        }

        public Task<long?> SortedSetRankAsync(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANK : RedisCommand.ZRANK, key, member);
            return ExecuteAsync(msg, ResultProcessor.NullableInt64);
        }

        public bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZREM, key, member);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZREM, key, members);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZREM, key, member);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SortedSetRemoveAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZREM, key, members);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long SortedSetRemoveRangeByRank(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZREMRANGEBYRANK, key, start, stop);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetRemoveRangeByRankAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZREMRANGEBYRANK, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long SortedSetRemoveRangeByScore(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRemoveRangeByScoreMessage(key, start, stop, exclude, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetRemoveRangeByScoreAsync(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRemoveRangeByScoreMessage(key, start, stop, exclude, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        IEnumerable<SortedSetEntry> IDatabase.SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            => SortedSetScanAsync(key, pattern, pageSize, CursorUtils.Origin, 0, flags);

        IEnumerable<SortedSetEntry> IDatabase.SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => SortedSetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

        IAsyncEnumerable<SortedSetEntry> IDatabaseAsync.SortedSetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => SortedSetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

        private CursorEnumerable<SortedSetEntry> SortedSetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            var scan = TryScan<SortedSetEntry>(key, pattern, pageSize, cursor, pageOffset, flags, RedisCommand.ZSCAN, SortedSetScanResultProcessor.Default, out var server);
            if (scan != null) return scan;

            if (cursor != 0) throw ExceptionFactory.NoCursor(RedisCommand.ZRANGE);
            if (pattern.IsNull) return CursorEnumerable<SortedSetEntry>.From(this, server, SortedSetRangeByRankWithScoresAsync(key, flags: flags), pageOffset);
            throw ExceptionFactory.NotSupported(true, RedisCommand.ZSCAN);
        }

        public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZSCORE, key, member);
            return ExecuteSync(msg, ResultProcessor.NullableDouble);
        }

        public double?[] SortedSetScores(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZMSCORE, key, members);
            return ExecuteSync(msg, ResultProcessor.NullableDoubleArray, defaultValue: Array.Empty<double?>());
        }

        public Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZSCORE, key, member);
            return ExecuteAsync(msg, ResultProcessor.NullableDouble);
        }

        public Task<double?[]> SortedSetScoresAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZMSCORE, key, members);
            return ExecuteAsync(msg, ResultProcessor.NullableDoubleArray, defaultValue: Array.Empty<double?>());
        }

        public SortedSetEntry? SortedSetPop(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN, key);
            return ExecuteSync(msg, ResultProcessor.SortedSetEntry);
        }

        public Task<SortedSetEntry?> SortedSetPopAsync(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN, key);
            return ExecuteAsync(msg, ResultProcessor.SortedSetEntry);
        }

        public SortedSetEntry[] SortedSetPop(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            if (count == 0) return Array.Empty<SortedSetEntry>();
            var msg = count == 1
                    ? Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN, key)
                    : Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN, key, count);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public SortedSetPopResult SortedSetPop(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetMultiPopMessage(keys, order, count, flags);
            return ExecuteSync(msg, ResultProcessor.SortedSetPopResult, defaultValue: SortedSetPopResult.Null);
        }

        public Task<SortedSetEntry[]> SortedSetPopAsync(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            if (count == 0) return CompletedTask<SortedSetEntry[]>.FromDefault(Array.Empty<SortedSetEntry>(), asyncState);
            var msg = count == 1
                    ? Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN, key)
                    : Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN, key, count);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores, defaultValue: Array.Empty<SortedSetEntry>());
        }

        public Task<SortedSetPopResult> SortedSetPopAsync(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetMultiPopMessage(keys, order, count, flags);
            return ExecuteAsync(msg, ResultProcessor.SortedSetPopResult, defaultValue: SortedSetPopResult.Null);
        }

        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAcknowledgeMessage(key, groupName, messageId, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAcknowledgeMessage(key, groupName, messageId, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAcknowledgeMessage(key, groupName, messageIds, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAcknowledgeMessage(key, groupName, messageIds, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAddMessage(key,
                messageId ?? StreamConstants.AutoGeneratedId,
                maxLength,
                useApproximateMaxLength,
                new NameValueEntry(streamField, streamValue),
                flags);

            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAddMessage(key,
                messageId ?? StreamConstants.AutoGeneratedId,
                maxLength,
                useApproximateMaxLength,
                new NameValueEntry(streamField, streamValue),
                flags);

            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAddMessage(key,
                messageId ?? StreamConstants.AutoGeneratedId,
                maxLength,
                useApproximateMaxLength,
                streamPairs,
                flags);

            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAddMessage(key,
                messageId ?? StreamConstants.AutoGeneratedId,
                maxLength,
                useApproximateMaxLength,
                streamPairs,
                flags);

            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public StreamAutoClaimResult StreamAutoClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAutoClaimMessage(key, consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, idsOnly: false, flags);
            return ExecuteSync(msg, ResultProcessor.StreamAutoClaim, defaultValue: StreamAutoClaimResult.Null);
        }

        public Task<StreamAutoClaimResult> StreamAutoClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAutoClaimMessage(key, consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, idsOnly: false, flags);
            return ExecuteAsync(msg, ResultProcessor.StreamAutoClaim, defaultValue: StreamAutoClaimResult.Null);
        }

        public StreamAutoClaimIdsOnlyResult StreamAutoClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAutoClaimMessage(key, consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, idsOnly: true, flags);
            return ExecuteSync(msg, ResultProcessor.StreamAutoClaimIdsOnly, defaultValue: StreamAutoClaimIdsOnlyResult.Null);
        }

        public Task<StreamAutoClaimIdsOnlyResult> StreamAutoClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamAutoClaimMessage(key, consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, idsOnly: true, flags);
            return ExecuteAsync(msg, ResultProcessor.StreamAutoClaimIdsOnly, defaultValue: StreamAutoClaimIdsOnlyResult.Null);
        }

        public StreamEntry[] StreamClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamClaimMessage(key,
                consumerGroup,
                claimingConsumer,
                minIdleTimeInMs,
                messageIds,
                returnJustIds: false,
                flags: flags);

            return ExecuteSync(msg, ResultProcessor.SingleStream, defaultValue: Array.Empty<StreamEntry>());
        }

        public Task<StreamEntry[]> StreamClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamClaimMessage(key,
                consumerGroup,
                claimingConsumer,
                minIdleTimeInMs,
                messageIds,
                returnJustIds: false,
                flags: flags);

            return ExecuteAsync(msg, ResultProcessor.SingleStream, defaultValue: Array.Empty<StreamEntry>());
        }

        public RedisValue[] StreamClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamClaimMessage(key,
                consumerGroup,
                claimingConsumer,
                minIdleTimeInMs,
                messageIds,
                returnJustIds: true,
                flags: flags);

            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<RedisValue[]> StreamClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamClaimMessage(key,
                consumerGroup,
                claimingConsumer,
                minIdleTimeInMs,
                messageIds,
                returnJustIds: true,
                flags: flags);

            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public bool StreamConsumerGroupSetPosition(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                new RedisValue[]
                {
                    StreamConstants.SetId,
                    key.AsRedisValue(),
                    groupName,
                    StreamPosition.Resolve(position, RedisCommand.XGROUP)
                });

            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StreamConsumerGroupSetPositionAsync(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                new RedisValue[]
                {
                    StreamConstants.SetId,
                    key.AsRedisValue(),
                    groupName,
                    StreamPosition.Resolve(position, RedisCommand.XGROUP)
                });

            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
        {
            return StreamCreateConsumerGroup(
                key,
                groupName,
                position,
                true,
                flags);
        }

        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamCreateConsumerGroupMessage(
                key,
                groupName,
                position,
                createStream,
                flags);

            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
        {
            return StreamCreateConsumerGroupAsync(
                key,
                groupName,
                position,
                true,
                flags);
        }

        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamCreateConsumerGroupMessage(
                key,
                groupName,
                position,
                createStream,
                flags);

            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public StreamConsumerInfo[] StreamConsumerInfo(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XINFO,
                new RedisValue[]
                {
                    StreamConstants.Consumers,
                    key.AsRedisValue(),
                    groupName
                });

            return ExecuteSync(msg, ResultProcessor.StreamConsumerInfo, defaultValue: Array.Empty<StreamConsumerInfo>());
        }

        public Task<StreamConsumerInfo[]> StreamConsumerInfoAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XINFO,
                new RedisValue[]
                {
                    StreamConstants.Consumers,
                    key.AsRedisValue(),
                    groupName
                });

            return ExecuteAsync(msg, ResultProcessor.StreamConsumerInfo, defaultValue: Array.Empty<StreamConsumerInfo>());
        }

        public StreamGroupInfo[] StreamGroupInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XINFO, StreamConstants.Groups, key);
            return ExecuteSync(msg, ResultProcessor.StreamGroupInfo, defaultValue: Array.Empty<StreamGroupInfo>());
        }

        public Task<StreamGroupInfo[]> StreamGroupInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XINFO, StreamConstants.Groups, key);
            return ExecuteAsync(msg, ResultProcessor.StreamGroupInfo, defaultValue: Array.Empty<StreamGroupInfo>());
        }

        public StreamInfo StreamInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XINFO, StreamConstants.Stream, key);
            return ExecuteSync(msg, ResultProcessor.StreamInfo);
        }

        public Task<StreamInfo> StreamInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XINFO, StreamConstants.Stream, key);
            return ExecuteAsync(msg, ResultProcessor.StreamInfo);
        }

        public long StreamLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XLEN, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StreamLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XLEN, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XDEL,
                key,
                messageIds);

            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XDEL,
                key,
                messageIds);

            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StreamDeleteConsumer(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                new RedisValue[]
                {
                    StreamConstants.DeleteConsumer,
                    key.AsRedisValue(),
                    groupName,
                    consumerName
                });

            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StreamDeleteConsumerAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                new RedisValue[]
                {
                    StreamConstants.DeleteConsumer,
                    key.AsRedisValue(),
                    groupName,
                    consumerName
                });

            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public bool StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                new RedisValue[]
                {
                    StreamConstants.Destroy,
                    key.AsRedisValue(),
                    groupName
                });

            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StreamDeleteConsumerGroupAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                new RedisValue[]
                {
                    StreamConstants.Destroy,
                    key.AsRedisValue(),
                    groupName
                });

            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public StreamPendingInfo StreamPending(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XPENDING, key, groupName);
            return ExecuteSync(msg, ResultProcessor.StreamPendingInfo);
        }

        public Task<StreamPendingInfo> StreamPendingAsync(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XPENDING, key, groupName);
            return ExecuteAsync(msg, ResultProcessor.StreamPendingInfo);
        }

        public StreamPendingMessageInfo[] StreamPendingMessages(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamPendingMessagesMessage(key,
                groupName,
                minId,
                maxId,
                count,
                consumerName,
                flags);

            return ExecuteSync(msg, ResultProcessor.StreamPendingMessages, defaultValue: Array.Empty<StreamPendingMessageInfo>());
        }

        public Task<StreamPendingMessageInfo[]> StreamPendingMessagesAsync(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamPendingMessagesMessage(key,
                groupName,
                minId,
                maxId,
                count,
                consumerName,
                flags);

            return ExecuteAsync(msg, ResultProcessor.StreamPendingMessages, defaultValue: Array.Empty<StreamPendingMessageInfo>());
        }

        public StreamEntry[] StreamRange(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamRangeMessage(key,
                minId,
                maxId,
                count,
                messageOrder,
                flags);

            return ExecuteSync(msg, ResultProcessor.SingleStream, defaultValue: Array.Empty<StreamEntry>());
        }

        public Task<StreamEntry[]> StreamRangeAsync(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamRangeMessage(key,
                minId,
                maxId,
                count,
                messageOrder,
                flags);

            return ExecuteAsync(msg, ResultProcessor.SingleStream, defaultValue: Array.Empty<StreamEntry>());
        }

        public StreamEntry[] StreamRead(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSingleStreamReadMessage(key,
                StreamPosition.Resolve(position, RedisCommand.XREAD),
                count,
                flags);

            return ExecuteSync(msg, ResultProcessor.SingleStreamWithNameSkip, defaultValue: Array.Empty<StreamEntry>());
        }

        public Task<StreamEntry[]> StreamReadAsync(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSingleStreamReadMessage(key,
                StreamPosition.Resolve(position, RedisCommand.XREAD),
                count,
                flags);

            return ExecuteAsync(msg, ResultProcessor.SingleStreamWithNameSkip, defaultValue: Array.Empty<StreamEntry>());
        }

        public RedisStream[] StreamRead(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetMultiStreamReadMessage(streamPositions, countPerStream, flags);
            return ExecuteSync(msg, ResultProcessor.MultiStream, defaultValue: Array.Empty<RedisStream>());
        }

        public Task<RedisStream[]> StreamReadAsync(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetMultiStreamReadMessage(streamPositions, countPerStream, flags);
            return ExecuteAsync(msg, ResultProcessor.MultiStream, defaultValue: Array.Empty<RedisStream>());
        }

        public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags)
        {
            return StreamReadGroup(key,
                groupName,
                consumerName,
                position,
                count,
                false,
                flags);
        }

        public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, CommandFlags flags = CommandFlags.None)
        {
            var actualPosition = position ?? StreamPosition.NewMessages;

            var msg = GetStreamReadGroupMessage(key,
                groupName,
                consumerName,
                StreamPosition.Resolve(actualPosition, RedisCommand.XREADGROUP),
                count,
                noAck,
                flags);

            return ExecuteSync(msg, ResultProcessor.SingleStreamWithNameSkip, defaultValue: Array.Empty<StreamEntry>());
        }

        public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags)
        {
            return StreamReadGroupAsync(key,
                groupName,
                consumerName,
                position,
                count,
                false,
                flags);
        }

        public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, CommandFlags flags = CommandFlags.None)
        {
            var actualPosition = position ?? StreamPosition.NewMessages;

            var msg = GetStreamReadGroupMessage(key,
                groupName,
                consumerName,
                StreamPosition.Resolve(actualPosition, RedisCommand.XREADGROUP),
                count,
                noAck,
                flags);

            return ExecuteAsync(msg, ResultProcessor.SingleStreamWithNameSkip, defaultValue: Array.Empty<StreamEntry>());
        }

        public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags)
        {
            return StreamReadGroup(streamPositions,
                groupName,
                consumerName,
                countPerStream,
                false,
                flags);
        }

        public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetMultiStreamReadGroupMessage(streamPositions,
                groupName,
                consumerName,
                countPerStream,
                noAck,
                flags);

            return ExecuteSync(msg, ResultProcessor.MultiStream, defaultValue: Array.Empty<RedisStream>());
        }

        public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags)
        {
            return StreamReadGroupAsync(streamPositions,
                groupName,
                consumerName,
                countPerStream,
                false,
                flags);
        }

        public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetMultiStreamReadGroupMessage(streamPositions,
                groupName,
                consumerName,
                countPerStream,
                noAck,
                flags);

            return ExecuteAsync(msg, ResultProcessor.MultiStream, defaultValue: Array.Empty<RedisStream>());
        }

        public long StreamTrim(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamTrimMessage(key, maxLength, useApproximateMaxLength, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StreamTrimAsync(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamTrimMessage(key, maxLength, useApproximateMaxLength, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.APPEND, key, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.APPEND, key, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StringBitCount(RedisKey key, long start, long end, CommandFlags flags) =>
            StringBitCount(key, start, end, StringIndexType.Byte, flags);

        public long StringBitCount(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
        {
            var msg = indexType switch
            {
                StringIndexType.Byte => Message.Create(Database, flags, RedisCommand.BITCOUNT, key, start, end),
                _ => Message.Create(Database, flags, RedisCommand.BITCOUNT, key, start, end, indexType.ToLiteral()),
            };
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringBitCountAsync(RedisKey key, long start, long end, CommandFlags flags) =>
            StringBitCountAsync(key, start, end, StringIndexType.Byte, flags);

        public Task<long> StringBitCountAsync(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
        {
            var msg = indexType switch
            {
                StringIndexType.Byte => Message.Create(Database, flags, RedisCommand.BITCOUNT, key, start, end),
                _ => Message.Create(Database, flags, RedisCommand.BITCOUNT, key, start, end, indexType.ToLiteral()),
            };
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringBitOperationMessage(operation, destination, first, second, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringBitOperationMessage(operation, destination, keys, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringBitOperationMessage(operation, destination, first, second, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringBitOperationMessage(operation, destination, keys, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StringBitPosition(RedisKey key, bool bit, long start, long end, CommandFlags flags) =>
            StringBitPosition(key, bit, start, end, StringIndexType.Byte, flags);

        public long StringBitPosition(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
        {
            var msg = indexType switch
            {
                StringIndexType.Byte => Message.Create(Database, flags, RedisCommand.BITPOS, key, bit, start, end),
                _ => Message.Create(Database, flags, RedisCommand.BITPOS, key, bit, start, end, indexType.ToLiteral()),
            };
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start, long end, CommandFlags flags) =>
            StringBitPositionAsync(key, bit, start, end, StringIndexType.Byte, flags);

        public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
        {
            var msg = indexType switch
            {
                StringIndexType.Byte => Message.Create(Database, flags, RedisCommand.BITPOS, key, bit, start, end),
                _ => Message.Create(Database, flags, RedisCommand.BITPOS, key, bit, start, end, indexType.ToLiteral()),
            };
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StringDecrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            return StringIncrement(key, -value, flags);
        }

        public double StringDecrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        {
            return StringIncrement(key, -value, flags);
        }

        public Task<long> StringDecrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            return StringIncrementAsync(key, -value, flags);
        }

        public Task<double> StringDecrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        {
            return StringIncrementAsync(key, -value, flags);
        }

        public RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GET, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue StringGetSetExpiry(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringGetExMessage(key, expiry, flags);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue StringGetSetExpiry(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringGetExMessage(key, expiry, flags);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringGetExMessage(key, expiry, flags);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringGetExMessage(key, expiry, flags);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length == 0) return Array.Empty<RedisValue>();
            var msg = Message.Create(Database, flags, RedisCommand.MGET, keys);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Lease<byte>? StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GET, key);
            return ExecuteSync(msg, ResultProcessor.Lease);
        }

        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GET, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<Lease<byte>?> StringGetLeaseAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GET, key);
            return ExecuteAsync(msg, ResultProcessor.Lease);
        }

        public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length == 0) return CompletedTask<RedisValue[]>.FromDefault(Array.Empty<RedisValue>(), asyncState);
            var msg = Message.Create(Database, flags, RedisCommand.MGET, keys);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public bool StringGetBit(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GETBIT, key, offset);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StringGetBitAsync(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GETBIT, key, offset);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GETRANGE, key, start, end);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GETRANGE, key, start, end);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GETSET, key, value);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GETSET, key, value);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue StringGetDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GETDEL, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringGetDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GETDEL, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringGetWithExpiryMessage(key, flags, out ResultProcessor<RedisValueWithExpiry> processor, out ServerEndPoint? server);
            return ExecuteSync(msg, processor, server);
        }

        public Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringGetWithExpiryMessage(key, flags, out ResultProcessor<RedisValueWithExpiry> processor, out ServerEndPoint? server);
            return ExecuteAsync(msg, processor, server);
        }

        public long StringIncrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            var msg = IncrMessage(key, value, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public double StringIncrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Database, flags, RedisCommand.INCRBYFLOAT, key, value);
            return ExecuteSync(msg, ResultProcessor.Double);
        }

        public Task<long> StringIncrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            var msg = IncrMessage(key, value, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<double> StringIncrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Database, flags, RedisCommand.INCRBYFLOAT, key, value);
            return ExecuteAsync(msg, ResultProcessor.Double);
        }

        public long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.STRLEN, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.STRLEN, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        // Backwards compatibility overloads:
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when) =>
            StringSet(key, value, expiry, false, when, CommandFlags.None);
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
            StringSet(key, value, expiry, false, when, flags);

        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetMessage(key, value, expiry, keepTtl, when, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public bool StringSet(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetMessage(values, when, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        // Backwards compatibility overloads:
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when) =>
            StringSetAsync(key, value, expiry, false, when);
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
            StringSetAsync(key, value, expiry, false, when, flags);

        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetMessage(key, value, expiry, keepTtl, when, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetMessage(values, when, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
            StringSetAndGet(key, value, expiry, false, when, flags);

        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetAndGetMessage(key, value, expiry, keepTtl, when, flags);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
            StringSetAndGetAsync(key, value, expiry, false, when, flags);

        public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetAndGetMessage(key, value, expiry, keepTtl, when, flags);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public bool StringSetBit(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SETBIT, key, offset, bit);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SETBIT, key, offset, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SETRANGE, key, offset, value);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.TOUCH, key);
            return ExecuteSync(msg, ResultProcessor.DemandZeroOrOne);
        }

        public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length > 0)
            {
                var msg = keys.Length == 0 ? null : Message.Create(Database, flags, RedisCommand.TOUCH, keys);
                return ExecuteSync(msg, ResultProcessor.Int64);
            }
            return 0;
        }

        public Task<bool> KeyTouchAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.TOUCH, key);
            return ExecuteAsync(msg, ResultProcessor.DemandZeroOrOne);
        }

        public Task<long> KeyTouchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length > 0)
            {
                var msg = keys.Length == 0 ? null : Message.Create(Database, flags, RedisCommand.TOUCH, keys);
                return ExecuteAsync(msg, ResultProcessor.Int64);
            }
            return CompletedTask<long>.Default(0);
        }

        public Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SETRANGE, key, offset, value);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        private long GetMillisecondsUntil(DateTime when) => when.Kind switch
        {
            DateTimeKind.Local or DateTimeKind.Utc => (when.ToUniversalTime() - RedisBase.UnixEpoch).Ticks / TimeSpan.TicksPerMillisecond,
            _ => throw new ArgumentException("Expiry time must be either Utc or Local", nameof(when)),
        };

        private Message GetCopyMessage(in RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase, bool replace, CommandFlags flags) =>
            destinationDatabase switch
            {
                < -1 => throw new ArgumentOutOfRangeException(nameof(destinationDatabase)),
                -1 when replace => Message.Create(Database, flags, RedisCommand.COPY, sourceKey, destinationKey, RedisLiterals.REPLACE),
                -1              => Message.Create(Database, flags, RedisCommand.COPY, sourceKey, destinationKey),
                _ when replace  => Message.Create(Database, flags, RedisCommand.COPY, sourceKey, destinationKey, RedisLiterals.DB, destinationDatabase, RedisLiterals.REPLACE),
                _               => Message.Create(Database, flags, RedisCommand.COPY, sourceKey, destinationKey, RedisLiterals.DB, destinationDatabase),
            };

        private Message GetExpiryMessage(in RedisKey key, CommandFlags flags, TimeSpan? expiry, ExpireWhen when, out ServerEndPoint? server)
        {
            if (expiry is null || expiry.Value == TimeSpan.MaxValue)
            {
                server = null;
                return when switch
                {
                    ExpireWhen.Always => Message.Create(Database, flags, RedisCommand.PERSIST, key),
                    _ => throw new ArgumentException("PERSIST cannot be used with when.")
                };
            }

            long milliseconds = expiry.Value.Ticks / TimeSpan.TicksPerMillisecond;
            return GetExpiryMessage(key, RedisCommand.PEXPIRE, RedisCommand.EXPIRE, milliseconds, when, flags, out server);
        }

        private Message GetExpiryMessage(in RedisKey key, CommandFlags flags, DateTime? expiry, ExpireWhen when, out ServerEndPoint? server)
        {
            if (expiry is null || expiry == DateTime.MaxValue)
            {
                server = null;
                return when switch
                {
                    ExpireWhen.Always => Message.Create(Database, flags, RedisCommand.PERSIST, key),
                    _ => throw new ArgumentException("PERSIST cannot be used with when.")
                };
            }

            long milliseconds = GetMillisecondsUntil(expiry.Value);
            return GetExpiryMessage(key, RedisCommand.PEXPIREAT, RedisCommand.EXPIREAT, milliseconds, when, flags, out server);
        }

        private Message GetExpiryMessage(in RedisKey key,
            RedisCommand millisecondsCommand,
            RedisCommand secondsCommand,
            long milliseconds,
            ExpireWhen when,
            CommandFlags flags,
            out ServerEndPoint? server)
        {
            server = null;
            if ((milliseconds % 1000) != 0)
            {
                var features = GetFeatures(key, flags, out server);
                if (server is not null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(millisecondsCommand))
                {
                    return when switch
                    {
                        ExpireWhen.Always => Message.Create(Database, flags, millisecondsCommand, key, milliseconds),
                        _ => Message.Create(Database, flags, millisecondsCommand, key, milliseconds, when.ToLiteral())
                    };
                }
                server = null;
            }

            long seconds = milliseconds / 1000;
            return when switch
            {
                ExpireWhen.Always => Message.Create(Database, flags, secondsCommand, key, seconds),
                _ => Message.Create(Database, flags, secondsCommand, key, seconds, when.ToLiteral())
            };
        }

        private Message GetListMultiPopMessage(RedisKey[] keys, RedisValue side, long count, CommandFlags flags)
        {
            if (keys is null || keys.Length == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(keys), "keys must have a size of at least 1");
            }

            var slot = multiplexer.ServerSelectionStrategy.HashSlot(keys[0]);

            var args = new RedisValue[2 + keys.Length + (count == 1 ? 0 : 2)];
            var i = 0;
            args[i++] = keys.Length;
            foreach (var key in keys)
            {
                args[i++] = key.AsRedisValue();
            }

            args[i++] = side;

            if (count != 1)
            {
                args[i++] = RedisLiterals.COUNT;
                args[i++] = count;
            }

            return Message.CreateInSlot(Database, slot, flags, RedisCommand.LMPOP, args);
        }

        private Message GetSortedSetMultiPopMessage(RedisKey[] keys, Order order, long count, CommandFlags flags)
        {
            if (keys is null || keys.Length == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(keys), "keys must have a size of at least 1");
            }

            var slot = multiplexer.ServerSelectionStrategy.HashSlot(keys[0]);

            var args = new RedisValue[2 + keys.Length + (count == 1 ? 0 : 2)];
            var i = 0;
            args[i++] = keys.Length;
            foreach (var key in keys)
            {
                args[i++] = key.AsRedisValue();
            }

            args[i++] = order == Order.Ascending ? RedisLiterals.MIN : RedisLiterals.MAX;

            if (count != 1)
            {
                args[i++] = RedisLiterals.COUNT;
                args[i++] = count;
            }

            return Message.CreateInSlot(Database, slot, flags, RedisCommand.ZMPOP, args);
        }

        private Message? GetHashSetMessage(RedisKey key, HashEntry[] hashFields, CommandFlags flags)
        {
            if (hashFields == null) throw new ArgumentNullException(nameof(hashFields));
            switch (hashFields.Length)
            {
                case 0: return null;
                case 1:
                    return Message.Create(Database, flags, RedisCommand.HMSET, key,
                    hashFields[0].name, hashFields[0].value);
                case 2:
                    return Message.Create(Database, flags, RedisCommand.HMSET, key,
                        hashFields[0].name, hashFields[0].value,
                        hashFields[1].name, hashFields[1].value);
                default:
                    var arr = new RedisValue[hashFields.Length * 2];
                    int offset = 0;
                    for (int i = 0; i < hashFields.Length; i++)
                    {
                        arr[offset++] = hashFields[i].name;
                        arr[offset++] = hashFields[i].value;
                    }
                    return Message.Create(Database, flags, RedisCommand.HMSET, key, arr);
            }
        }

        private ITransaction? GetLockExtendTransaction(RedisKey key, RedisValue value, TimeSpan expiry)
        {
            var tran = CreateTransactionIfAvailable(asyncState);
            if (tran is not null)
            {
                tran.AddCondition(Condition.StringEqual(key, value));
                tran.KeyExpireAsync(key, expiry, CommandFlags.FireAndForget);
            }
            return tran;
        }

        private ITransaction? GetLockReleaseTransaction(RedisKey key, RedisValue value)
        {
            var tran = CreateTransactionIfAvailable(asyncState);
            if (tran is not null)
            {
                tran.AddCondition(Condition.StringEqual(key, value));
                tran.KeyDeleteAsync(key, CommandFlags.FireAndForget);
            }
            return tran;
        }

        private static RedisValue GetLexRange(RedisValue value, Exclude exclude, bool isStart)
        {
            if (value.IsNull)
            {
                return isStart ? RedisLiterals.MinusSymbol : RedisLiterals.PlusSumbol;
            }
            byte[] orig = value!;

            byte[] result = new byte[orig.Length + 1];
            // no defaults here; must always explicitly specify [ / (
            result[0] = (exclude & (isStart ? Exclude.Start : Exclude.Stop)) == 0 ? (byte)'[' : (byte)'(';
            Buffer.BlockCopy(orig, 0, result, 1, orig.Length);
            return result;
        }

        private Message GetMultiStreamReadGroupMessage(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, bool noAck, CommandFlags flags) =>
            new MultiStreamReadGroupCommandMessage(Database,
                flags,
                streamPositions,
                groupName,
                consumerName,
                countPerStream,
                noAck);

        private sealed class MultiStreamReadGroupCommandMessage : Message // XREADGROUP with multiple stream. Example: XREADGROUP GROUP groupName consumerName COUNT countPerStream STREAMS stream1 stream2 id1 id2
        {
            private readonly StreamPosition[] streamPositions;
            private readonly RedisValue groupName;
            private readonly RedisValue consumerName;            
            private readonly int? countPerStream;
            private readonly bool noAck;
            private readonly int argCount;

            public MultiStreamReadGroupCommandMessage(int db, CommandFlags flags, StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, bool noAck)
                : base(db, flags, RedisCommand.XREADGROUP)
            {
                if (streamPositions == null) throw new ArgumentNullException(nameof(streamPositions));
                if (streamPositions.Length == 0) throw new ArgumentOutOfRangeException(nameof(streamPositions), "streamOffsetPairs must contain at least one item.");
                for (int i = 0; i < streamPositions.Length; i++)
                {
                    streamPositions[i].Key.AssertNotNull();
                }

                if (countPerStream.HasValue && countPerStream <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(countPerStream), "countPerStream must be greater than 0.");
                }                

                groupName.AssertNotNull();
                consumerName.AssertNotNull();
                
                this.streamPositions = streamPositions;
                this.groupName = groupName;
                this.consumerName = consumerName;
                this.countPerStream = countPerStream;
                this.noAck = noAck;

                argCount =  4                               // Room for GROUP groupName consumerName & STREAMS
                    + (streamPositions.Length * 2)          // Enough room for the stream keys and associated IDs.
                    + (countPerStream.HasValue ? 2 : 0)     // Room for "COUNT num" or 0 if countPerStream is null.
                    + (noAck ? 1 : 0);                      // Allow for the NOACK subcommand.
                 
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                int slot = ServerSelectionStrategy.NoSlot;
                for (int i = 0; i < streamPositions.Length; i++)
                {
                    slot = serverSelectionStrategy.CombineSlot(slot, streamPositions[i].Key);
                }
                return slot;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, argCount);
                physical.WriteBulkString(StreamConstants.Group);
                physical.WriteBulkString(groupName);
                physical.WriteBulkString(consumerName);

                if (countPerStream.HasValue)
                {
                    physical.WriteBulkString(StreamConstants.Count);
                    physical.WriteBulkString(countPerStream.Value);
                }

                if (noAck)
                {
                    physical.WriteBulkString(StreamConstants.NoAck);
                }

                physical.WriteBulkString(StreamConstants.Streams);
                for (int i = 0; i < streamPositions.Length; i++)
                {
                    physical.Write(streamPositions[i].Key);                    
                }
                for (int i = 0; i < streamPositions.Length; i++)
                {
                    physical.WriteBulkString(StreamPosition.Resolve(streamPositions[i].Position, RedisCommand.XREADGROUP));
                }                
            }            

            public override int ArgCount => argCount;
        }

        private Message GetMultiStreamReadMessage(StreamPosition[] streamPositions, int? countPerStream, CommandFlags flags) =>
            new MultiStreamReadCommandMessage(Database, flags, streamPositions, countPerStream);

        private sealed class MultiStreamReadCommandMessage : Message // XREAD with multiple stream. Example: XREAD COUNT 2 STREAMS mystream writers 0-0 0-0
        {
            private readonly StreamPosition[] streamPositions;            
            private readonly int? countPerStream;            
            private readonly int argCount;

            public MultiStreamReadCommandMessage(int db, CommandFlags flags, StreamPosition[] streamPositions, int? countPerStream)
                : base(db, flags, RedisCommand.XREAD)
            {
                if (streamPositions == null) throw new ArgumentNullException(nameof(streamPositions));
                if (streamPositions.Length == 0) throw new ArgumentOutOfRangeException(nameof(streamPositions), "streamOffsetPairs must contain at least one item.");
                for (int i = 0; i < streamPositions.Length; i++)
                {
                    streamPositions[i].Key.AssertNotNull();
                }

                if (countPerStream.HasValue && countPerStream <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(countPerStream), "countPerStream must be greater than 0.");
                }               

                this.streamPositions = streamPositions;                
                this.countPerStream = countPerStream;                

                argCount = 1                             // Streams keyword.
                    + (countPerStream.HasValue ? 2 : 0)  // Room for "COUNT num" or 0 if countPerStream is null.
                    + (streamPositions.Length * 2);      // Room for the stream names and the ID after which to begin reading.
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                int slot = ServerSelectionStrategy.NoSlot;
                for (int i = 0; i < streamPositions.Length; i++)
                {
                    slot = serverSelectionStrategy.CombineSlot(slot, streamPositions[i].Key);
                }
                return slot;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, argCount);

                if (countPerStream.HasValue)
                {
                    physical.WriteBulkString(StreamConstants.Count);
                    physical.WriteBulkString(countPerStream.Value);
                }                

                physical.WriteBulkString(StreamConstants.Streams);
                for (int i = 0; i < streamPositions.Length; i++)
                {
                    physical.Write(streamPositions[i].Key);
                }
                for (int i = 0; i < streamPositions.Length; i++)
                {
                    physical.WriteBulkString(StreamPosition.Resolve(streamPositions[i].Position, RedisCommand.XREADGROUP));
                }
            }

            public override int ArgCount => argCount;
        }

        private static RedisValue GetRange(double value, Exclude exclude, bool isStart)
        {
            if (isStart)
            {
                if ((exclude & Exclude.Start) == 0) return value; // inclusive is default
            }
            else
            {
                if ((exclude & Exclude.Stop) == 0) return value; // inclusive is default
            }
            return "(" + Format.ToString(value); // '(' prefix means exclusive
        }

        private Message GetRestoreMessage(RedisKey key, byte[] value, TimeSpan? expiry, CommandFlags flags)
        {
            long pttl = (expiry == null || expiry.Value == TimeSpan.MaxValue) ? 0 : (expiry.Value.Ticks / TimeSpan.TicksPerMillisecond);
            return Message.Create(Database, flags, RedisCommand.RESTORE, key, pttl, value);
        }

        private Message GetSetIntersectionLengthMessage(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            var values = new RedisValue[1 + keys.Length + (limit > 0 ? 2 : 0)];
            int i = 0;
            values[i++] = keys.Length;
            for (var j = 0; j < keys.Length; j++)
            {
                values[i++] = keys[j].AsRedisValue();
            }
            if (limit > 0)
            {
                values[i++] = RedisLiterals.LIMIT;
                values[i] = limit;
            }

            return Message.Create(Database, flags, RedisCommand.SINTERCARD, values);
        }

        private Message GetSortedSetAddMessage(RedisKey key, RedisValue member, double score, SortedSetWhen when, bool change, CommandFlags flags)
        {
            RedisValue[] arr = new RedisValue[2 + when.CountBits() + (change? 1:0)];
            int index = 0;
            if ((when & SortedSetWhen.NotExists) != 0) {
                arr[index++] = RedisLiterals.NX;
            }
            if ((when & SortedSetWhen.Exists) != 0) {
                arr[index++] = RedisLiterals.XX;
            }
            if ((when & SortedSetWhen.GreaterThan) != 0) {
                arr[index++] = RedisLiterals.GT;
            }
            if ((when & SortedSetWhen.LessThan) != 0) {
                arr[index++] = RedisLiterals.LT;
            }
            if (change) {
                arr[index++] = RedisLiterals.CH;
            }
            arr[index++] = score;
            arr[index++] = member;
            return Message.Create(Database, flags, RedisCommand.ZADD, key, arr);
        }

        private Message? GetSortedSetAddMessage(RedisKey key, SortedSetEntry[] values, SortedSetWhen when, bool change, CommandFlags flags)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            switch (values.Length)
            {
                case 0: return null;
                case 1:
                    return GetSortedSetAddMessage(key, values[0].element, values[0].score, when, change, flags);
                default:
                    RedisValue[] arr = new RedisValue[(values.Length * 2) + when.CountBits() + (change? 1:0)];
                    int index = 0;
                    if ((when & SortedSetWhen.NotExists) != 0) {
                        arr[index++] = RedisLiterals.NX;
                    }
                    if ((when & SortedSetWhen.Exists) != 0) {
                        arr[index++] = RedisLiterals.XX;
                    }
                    if ((when & SortedSetWhen.GreaterThan) != 0) {
                        arr[index++] = RedisLiterals.GT;
                    }
                    if ((when & SortedSetWhen.LessThan) != 0) {
                        arr[index++] = RedisLiterals.LT;
                    }
                    if (change) {
                        arr[index++] = RedisLiterals.CH;
                    }

                    for (int i = 0; i < values.Length; i++)
                    {
                        arr[index++] = values[i].score;
                        arr[index++] = values[i].element;
                    }
                    return Message.Create(Database, flags, RedisCommand.ZADD, key, arr);
            }
        }

        private Message GetSortMessage(RedisKey destination, RedisKey key, long skip, long take, Order order, SortType sortType, RedisValue by, RedisValue[]? get, CommandFlags flags, out ServerEndPoint? server)
        {
            server = null;
            var command = destination.IsNull && GetFeatures(key, flags, out server).ReadOnlySort
                ? RedisCommand.SORT_RO
                : RedisCommand.SORT;

            // most common cases; no "get", no "by", no "destination", no "skip", no "take"
            if (destination.IsNull && skip == 0 && take == -1 && by.IsNull && (get == null || get.Length == 0))
            {
                return order switch
                {
                    Order.Ascending  when sortType == SortType.Numeric    => Message.Create(Database, flags, command, key),
                    Order.Ascending  when sortType == SortType.Alphabetic => Message.Create(Database, flags, command, key, RedisLiterals.ALPHA),
                    Order.Descending when sortType == SortType.Numeric    => Message.Create(Database, flags, command, key, RedisLiterals.DESC),
                    Order.Descending when sortType == SortType.Alphabetic => Message.Create(Database, flags, command, key, RedisLiterals.DESC, RedisLiterals.ALPHA),
                    Order.Ascending or Order.Descending => throw new ArgumentOutOfRangeException(nameof(sortType)),
                    _ => throw new ArgumentOutOfRangeException(nameof(order)),
                };
            }

            // and now: more complicated scenarios...
            var values = new List<RedisValue>();
            if (!by.IsNull)
            {
                values.Add(RedisLiterals.BY);
                values.Add(by);
            }
            if (skip != 0 || take != -1)// these are our defaults that mean "everything"; anything else needs to be sent explicitly
            {
                values.Add(RedisLiterals.LIMIT);
                values.Add(skip);
                values.Add(take);
            }
            switch (order)
            {
                case Order.Ascending:
                    break; // default
                case Order.Descending:
                    values.Add(RedisLiterals.DESC);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(order));
            }
            switch (sortType)
            {
                case SortType.Numeric:
                    break; // default
                case SortType.Alphabetic:
                    values.Add(RedisLiterals.ALPHA);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sortType));
            }
            if (get != null && get.Length != 0)
            {
                foreach (var item in get)
                {
                    values.Add(RedisLiterals.GET);
                    values.Add(item);
                }
            }
            if (destination.IsNull) return Message.Create(Database, flags, command, key, values.ToArray());

            // Because we are using STORE, we need to push this to a primary
            if (Message.GetPrimaryReplicaFlags(flags) == CommandFlags.DemandReplica)
            {
                throw ExceptionFactory.PrimaryOnly(multiplexer.RawConfig.IncludeDetailInExceptions, RedisCommand.SORT, null, null);
            }
            flags = Message.SetPrimaryReplicaFlags(flags, CommandFlags.DemandMaster);
            values.Add(RedisLiterals.STORE);
            return Message.Create(Database, flags, RedisCommand.SORT, key, values.ToArray(), destination);
        }

        private Message GetSortedSetCombineAndStoreCommandMessage(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights, Aggregate aggregate, CommandFlags flags)
        {
            var command = operation.ToCommand(store: true);
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }
            if (command == RedisCommand.ZDIFFSTORE && (weights != null || aggregate != Aggregate.Sum))
            {
                throw new ArgumentException("ZDIFFSTORE cannot be used with weights or aggregation.");
            }
            if (weights != null && keys.Length != weights.Length)
            {
                throw new ArgumentException("Keys and weights should have the same number of elements.", nameof(weights));
            }

            RedisValue[] values = RedisValue.EmptyArray;

            var argsLength = (weights?.Length > 0 ? 1 + weights.Length : 0) + (aggregate != Aggregate.Sum ? 2 : 0);
            if (argsLength > 0)
            {
                values = new RedisValue[argsLength];
                AddWeightsAggregationAndScore(values, weights, aggregate);
            }
            return new SortedSetCombineAndStoreCommandMessage(Database, flags, command, destination, keys, values);
        }

        private Message GetSortedSetCombineCommandMessage(SetOperation operation, RedisKey[] keys, double[]? weights, Aggregate aggregate, bool withScores, CommandFlags flags)
        {
            var command = operation.ToCommand(store: false);
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }
            if (command == RedisCommand.ZDIFF && (weights != null || aggregate != Aggregate.Sum))
            {
                throw new ArgumentException("ZDIFF cannot be used with weights or aggregation.");
            }
            if (weights != null && keys.Length != weights.Length)
            {
                throw new ArgumentException("Keys and weights should have the same number of elements.", nameof(weights));
            }

            var i = 0;
            var values = new RedisValue[1 + keys.Length +
                                        (weights?.Length > 0 ? 1 + weights.Length : 0) +
                                        (aggregate != Aggregate.Sum ? 2 : 0) +
                                        (withScores ? 1 : 0)];
            values[i++] = keys.Length;
            foreach (var key in keys)
            {
                values[i++] = key.AsRedisValue();
            }
            AddWeightsAggregationAndScore(values.AsSpan(i), weights, aggregate, withScores: withScores);
            return Message.Create(Database, flags, command, values ?? RedisValue.EmptyArray);
        }

        private void AddWeightsAggregationAndScore(Span<RedisValue> values, double[]? weights, Aggregate aggregate, bool withScores = false)
        {
            int i = 0;
            if (weights?.Length > 0)
            {
                values[i++] = RedisLiterals.WEIGHTS;
                foreach (var weight in weights)
                {
                    values[i++] = weight;
                }
            }
            switch (aggregate)
            {
                case Aggregate.Sum:
                    break; // add nothing - Redis default
                case Aggregate.Min:
                    values[i++] = RedisLiterals.AGGREGATE;
                    values[i++] = RedisLiterals.MIN;
                    break;
                case Aggregate.Max:
                    values[i++] = RedisLiterals.AGGREGATE;
                    values[i++] = RedisLiterals.MAX;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(aggregate));
            }
            if (withScores)
            {
                values[i++] = RedisLiterals.WITHSCORES;
            }
        }

        private Message GetSortedSetLengthMessage(RedisKey key, double min, double max, Exclude exclude, CommandFlags flags)
        {
            if (double.IsNegativeInfinity(min) && double.IsPositiveInfinity(max))
                return Message.Create(Database, flags, RedisCommand.ZCARD, key);

            var from = GetRange(min, exclude, true);
            var to = GetRange(max, exclude, false);
            return Message.Create(Database, flags, RedisCommand.ZCOUNT, key, from, to);
        }

        private Message GetSortedSetIntersectionLengthMessage(RedisKey[] keys, long limit, CommandFlags flags)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            var i = 0;
            var values = new RedisValue[1 + keys.Length + (limit > 0 ? 2 : 0)];
            values[i++] = keys.Length;
            foreach (var key in keys)
            {
                values[i++] = key.AsRedisValue();
            }
            if (limit > 0)
            {
                values[i++] = RedisLiterals.LIMIT;
                values[i++] = limit;
            }
            return Message.Create(Database, flags, RedisCommand.ZINTERCARD, values);
        }

        private Message GetSortedSetRangeByScoreMessage(RedisKey key, double start, double stop, Exclude exclude, Order order, long skip, long take, CommandFlags flags, bool withScores)
        {
            // usage: {ZRANGEBYSCORE|ZREVRANGEBYSCORE} key from to [WITHSCORES] [LIMIT offset count]
            // there's basically only 4 layouts; with/without each of scores/limit
            var command = order == Order.Descending ? RedisCommand.ZREVRANGEBYSCORE : RedisCommand.ZRANGEBYSCORE;
            bool unlimited = skip == 0 && take == -1; // these are our defaults that mean "everything"; anything else needs to be sent explicitly

            bool reverseLimits = (order == Order.Ascending) == (start > stop);
            if (reverseLimits)
            {
                var tmp = start;
                start = stop;
                stop = tmp;
                switch (exclude)
                {
                    case Exclude.Start: exclude = Exclude.Stop; break;
                    case Exclude.Stop: exclude = Exclude.Start; break;
                }
            }

            RedisValue from = GetRange(start, exclude, true), to = GetRange(stop, exclude, false);
            if (withScores)
            {
                return unlimited ? Message.Create(Database, flags, command, key, from, to, RedisLiterals.WITHSCORES)
                    : Message.Create(Database, flags, command, key, new[] { from, to, RedisLiterals.WITHSCORES, RedisLiterals.LIMIT, skip, take });
            }
            else
            {
                return unlimited ? Message.Create(Database, flags, command, key, from, to)
                    : Message.Create(Database, flags, command, key, new[] { from, to, RedisLiterals.LIMIT, skip, take });
            }
        }

        private Message GetSortedSetRemoveRangeByScoreMessage(RedisKey key, double start, double stop, Exclude exclude, CommandFlags flags)
        {
            return Message.Create(Database, flags, RedisCommand.ZREMRANGEBYSCORE, key,
                    GetRange(start, exclude, true), GetRange(stop, exclude, false));
        }

        private Message GetStreamAcknowledgeMessage(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags)
        {
            var values = new RedisValue[]
            {
                groupName,
                messageId
            };

            return Message.Create(Database, flags, RedisCommand.XACK, key, values);
        }

        private Message GetStreamAcknowledgeMessage(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags)
        {
            if (messageIds == null) throw new ArgumentNullException(nameof(messageIds));
            if (messageIds.Length == 0) throw new ArgumentOutOfRangeException(nameof(messageIds), "messageIds must contain at least one item.");

            var values = new RedisValue[messageIds.Length + 1];

            var offset = 0;

            values[offset++] = groupName;

            for (var i = 0; i < messageIds.Length; i++)
            {
                values[offset++] = messageIds[i];
            }

            return Message.Create(Database, flags, RedisCommand.XACK, key, values);
        }

        private Message GetStreamAddMessage(RedisKey key, RedisValue messageId, int? maxLength, bool useApproximateMaxLength, NameValueEntry streamPair, CommandFlags flags)
        {
            // Calculate the correct number of arguments:
            //  3 array elements for Entry ID & NameValueEntry.Name & NameValueEntry.Value.
            //  2 elements if using MAXLEN (keyword & value), otherwise 0.
            //  1 element if using Approximate Length (~), otherwise 0.
            var totalLength = 3 + (maxLength.HasValue ? 2 : 0)
                                + (maxLength.HasValue && useApproximateMaxLength ? 1 : 0);

            var values = new RedisValue[totalLength];
            var offset = 0;

            if (maxLength.HasValue)
            {
                values[offset++] = StreamConstants.MaxLen;

                if (useApproximateMaxLength)
                {
                    values[offset++] = StreamConstants.ApproximateMaxLen;
                    values[offset++] = maxLength.Value;
                }
                else
                {
                    values[offset++] = maxLength.Value;
                }
            }

            values[offset++] = messageId;

            values[offset++] = streamPair.Name;
            values[offset] = streamPair.Value;

            return Message.Create(Database, flags, RedisCommand.XADD, key, values);
        }

        /// <summary>
        /// Gets message for <see href="https://redis.io/commands/xadd"/>.
        /// </summary>
        private Message GetStreamAddMessage(RedisKey key, RedisValue entryId, int? maxLength, bool useApproximateMaxLength, NameValueEntry[] streamPairs, CommandFlags flags)
        {
            if (streamPairs == null) throw new ArgumentNullException(nameof(streamPairs));
            if (streamPairs.Length == 0) throw new ArgumentOutOfRangeException(nameof(streamPairs), "streamPairs must contain at least one item.");

            if (maxLength.HasValue && maxLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength), "maxLength must be greater than 0.");
            }

            var includeMaxLen = maxLength.HasValue ? 2 : 0;
            var includeApproxLen = maxLength.HasValue && useApproximateMaxLength ? 1 : 0;

            var totalLength = (streamPairs.Length * 2)     // Room for the name/value pairs
                                + 1                        // The stream entry ID
                                + includeMaxLen            // 2 or 0 (MAXLEN keyword & the count)
                                + includeApproxLen;        // 1 or 0

            var values = new RedisValue[totalLength];

            var offset = 0;

            if (maxLength.HasValue)
            {
                values[offset++] = StreamConstants.MaxLen;

                if (useApproximateMaxLength)
                {
                    values[offset++] = StreamConstants.ApproximateMaxLen;
                }

                values[offset++] = maxLength.Value;
            }

            values[offset++] = entryId;

            for (var i = 0; i < streamPairs.Length; i++)
            {
                values[offset++] = streamPairs[i].Name;
                values[offset++] = streamPairs[i].Value;
            }

            return Message.Create(Database, flags, RedisCommand.XADD, key, values);
        }

        private Message GetStreamAutoClaimMessage(RedisKey key, RedisValue consumerGroup, RedisValue assignToConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count, bool idsOnly, CommandFlags flags)
        {
            // XAUTOCLAIM <key> <group> <consumer> <min-idle-time> <start> [COUNT count] [JUSTID]
            var values = new RedisValue[4 + (count is null ? 0 : 2) + (idsOnly ? 1 : 0)];

            var offset = 0;

            values[offset++] = consumerGroup;
            values[offset++] = assignToConsumer;
            values[offset++] = minIdleTimeInMs;
            values[offset++] = startAtId;

            if (count is not null)
            {
                values[offset++] = StreamConstants.Count;
                values[offset++] = count.Value;
            }

            if (idsOnly)
            {
                values[offset++] = StreamConstants.JustId;
            }

            return Message.Create(Database, flags, RedisCommand.XAUTOCLAIM, key, values);
        }

        private Message GetStreamClaimMessage(RedisKey key, RedisValue consumerGroup, RedisValue assignToConsumer, long minIdleTimeInMs, RedisValue[] messageIds, bool returnJustIds, CommandFlags flags)
        {
            if (messageIds == null) throw new ArgumentNullException(nameof(messageIds));
            if (messageIds.Length == 0) throw new ArgumentOutOfRangeException(nameof(messageIds), "messageIds must contain at least one item.");

            // XCLAIM <key> <group> <consumer> <min-idle-time> <ID-1> <ID-2> ... <ID-N>
            var values = new RedisValue[3 + messageIds.Length + (returnJustIds ? 1 : 0)];

            var offset = 0;

            values[offset++] = consumerGroup;
            values[offset++] = assignToConsumer;
            values[offset++] = minIdleTimeInMs;

            for (var i = 0; i < messageIds.Length; i++)
            {
                values[offset++] = messageIds[i];
            }

            if (returnJustIds)
            {
                values[offset] = StreamConstants.JustId;
            }

            return Message.Create(Database, flags, RedisCommand.XCLAIM, key, values);
        }

        private Message GetStreamCreateConsumerGroupMessage(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None)
        {
            var actualPosition = position ?? StreamConstants.NewMessages;

            var values = new RedisValue[createStream ? 5 : 4];

            values[0] = StreamConstants.Create;
            values[1] = key.AsRedisValue();
            values[2] = groupName;
            values[3] = StreamPosition.Resolve(actualPosition, RedisCommand.XGROUP);

            if (createStream)
            {
                values[4] = StreamConstants.MkStream;
            }

            return Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                values);
        }

        /// <summary>
        /// Gets a message for <see href="https://redis.io/commands/xpending/"/>
        /// </summary>
        /// <remarks><seealso href="https://redis.io/topics/streams-intro"/></remarks>
        private Message GetStreamPendingMessagesMessage(RedisKey key, RedisValue groupName, RedisValue? minId, RedisValue? maxId, int count, RedisValue consumerName, CommandFlags flags)
        {
            // > XPENDING mystream mygroup - + 10 [consumer name]
            // 1) 1) 1526569498055 - 0
            //    2) "Bob"
            //    3) (integer)74170458
            //    4) (integer)1
            // 2) 1) 1526569506935 - 0
            //    2) "Bob"
            //    3) (integer)74170458
            //    4) (integer)1

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");
            }

            var values = new RedisValue[consumerName == RedisValue.Null ? 4 : 5];

            values[0] = groupName;
            values[1] = minId ?? StreamConstants.ReadMinValue;
            values[2] = maxId ?? StreamConstants.ReadMaxValue;
            values[3] = count;

            if (consumerName != RedisValue.Null)
            {
                values[4] = consumerName;
            }

            return Message.Create(Database,
                flags,
                RedisCommand.XPENDING,
                key,
                values);
        }

        private Message GetStreamRangeMessage(RedisKey key, RedisValue? minId, RedisValue? maxId, int? count, Order messageOrder, CommandFlags flags)
        {
            if (count.HasValue && count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");
            }

            var actualMin = minId ?? StreamConstants.ReadMinValue;
            var actualMax = maxId ?? StreamConstants.ReadMaxValue;

            var values = new RedisValue[2 + (count.HasValue ? 2 : 0)];

            values[0] = (messageOrder == Order.Ascending ? actualMin : actualMax);
            values[1] = (messageOrder == Order.Ascending ? actualMax : actualMin);

            if (count.HasValue)
            {
                values[2] = StreamConstants.Count;
                values[3] = count.Value;
            }

            return Message.Create(Database,
                flags,
                messageOrder == Order.Ascending ? RedisCommand.XRANGE : RedisCommand.XREVRANGE,
                key,
                values);
        }

        private Message GetStreamReadGroupMessage(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue afterId, int? count, bool noAck, CommandFlags flags) =>
            new SingleStreamReadGroupCommandMessage(Database, flags, key, groupName, consumerName, afterId, count, noAck);

        private sealed class SingleStreamReadGroupCommandMessage : Message.CommandKeyBase // XREADGROUP with single stream. eg XREADGROUP GROUP mygroup Alice COUNT 1 STREAMS mystream >
        {
            private readonly RedisValue groupName;
            private readonly RedisValue consumerName;            
            private readonly RedisValue afterId;
            private readonly int? count;
            private readonly bool noAck;
            private readonly int argCount;

            public SingleStreamReadGroupCommandMessage(int db, CommandFlags flags, RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue afterId, int? count, bool noAck)
                : base(db, flags, RedisCommand.XREADGROUP, key)
            {
                if (count.HasValue && count <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");
                }

                groupName.AssertNotNull();
                consumerName.AssertNotNull();
                afterId.AssertNotNull();

                this.groupName = groupName;
                this.consumerName = consumerName;
                this.afterId = afterId;
                this.count = count;
                this.noAck = noAck;
                argCount = 6 + (count.HasValue ? 2 : 0) + (noAck ? 1 : 0);
            }

            protected override void WriteImpl(PhysicalConnection physical) {
                physical.WriteHeader(Command, argCount);
                physical.WriteBulkString(StreamConstants.Group);
                physical.WriteBulkString(groupName);
                physical.WriteBulkString(consumerName);

                if (count.HasValue)
                {
                    physical.WriteBulkString(StreamConstants.Count);
                    physical.WriteBulkString(count.Value);                    
                }

                if (noAck)
                {
                    physical.WriteBulkString(StreamConstants.NoAck);
                }

                physical.WriteBulkString(StreamConstants.Streams);
                physical.Write(Key);
                physical.WriteBulkString(afterId);
            }

            public override int ArgCount => argCount;
        }

        private Message GetSingleStreamReadMessage(RedisKey key, RedisValue afterId, int? count, CommandFlags flags) =>
            new SingleStreamReadCommandMessage(Database, flags, key, afterId, count);

        private sealed class SingleStreamReadCommandMessage : Message.CommandKeyBase // XREAD with a single stream. Example: XREAD COUNT 2 STREAMS mystream 0-0
        {
            private readonly RedisValue afterId;
            private readonly int? count;            
            private readonly int argCount;

            public SingleStreamReadCommandMessage(int db, CommandFlags flags, RedisKey key, RedisValue afterId, int? count)
                : base(db, flags, RedisCommand.XREAD, key)
            {
                if (count.HasValue && count <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");
                }

                afterId.AssertNotNull();

                this.afterId = afterId;
                this.count = count;
                argCount = count.HasValue ? 5 : 3;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, argCount);

                if (count.HasValue)
                {
                    physical.WriteBulkString(StreamConstants.Count);
                    physical.WriteBulkString(count.Value);
                }                

                physical.WriteBulkString(StreamConstants.Streams);
                physical.Write(Key);
                physical.WriteBulkString(afterId);
            }

            public override int ArgCount => argCount;
        }


        private Message GetStreamTrimMessage(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags)
        {
            if (maxLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength), "maxLength must be equal to or greater than 0.");
            }

            var values = new RedisValue[2 + (useApproximateMaxLength ? 1 : 0)];

            values[0] = StreamConstants.MaxLen;

            if (useApproximateMaxLength)
            {
                values[1] = StreamConstants.ApproximateMaxLen;
                values[2] = maxLength;
            }
            else
            {
                values[1] = maxLength;
            }

            return Message.Create(Database,
                flags,
                RedisCommand.XTRIM,
                key,
                values);
        }

        private Message? GetStringBitOperationMessage(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length == 0) return null;

            // these ones are too bespoke to warrant custom Message implementations
            var serverSelectionStrategy = multiplexer.ServerSelectionStrategy;
            int slot = serverSelectionStrategy.HashSlot(destination);
            var values = new RedisValue[keys.Length + 2];
            values[0] = RedisLiterals.Get(operation);
            values[1] = destination.AsRedisValue();
            for (int i = 0; i < keys.Length; i++)
            {
                values[i + 2] = keys[i].AsRedisValue();
                slot = serverSelectionStrategy.CombineSlot(slot, keys[i]);
            }
            return Message.CreateInSlot(Database, slot, flags, RedisCommand.BITOP, values);
        }

        private Message GetStringBitOperationMessage(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags)
        {
            // these ones are too bespoke to warrant custom Message implementations
            var op = RedisLiterals.Get(operation);
            var serverSelectionStrategy = multiplexer.ServerSelectionStrategy;
            int slot = serverSelectionStrategy.HashSlot(destination);
            slot = serverSelectionStrategy.CombineSlot(slot, first);
            if (second.IsNull || operation == Bitwise.Not)
            { // unary
                return Message.CreateInSlot(Database, slot, flags, RedisCommand.BITOP, new[] { op, destination.AsRedisValue(), first.AsRedisValue() });
            }
            // binary
            slot = serverSelectionStrategy.CombineSlot(slot, second);
            return Message.CreateInSlot(Database, slot, flags, RedisCommand.BITOP, new[] { op, destination.AsRedisValue(), first.AsRedisValue(), second.AsRedisValue() });
        }

        private Message GetStringGetExMessage(in RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None) => expiry switch
        {
            null => Message.Create(Database, flags, RedisCommand.GETEX, key, RedisLiterals.PERSIST),
            _ => Message.Create(Database, flags, RedisCommand.GETEX, key, RedisLiterals.PX, (long)expiry.Value.TotalMilliseconds)
        };

        private Message GetStringGetExMessage(in RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None) => expiry == DateTime.MaxValue
            ? Message.Create(Database, flags, RedisCommand.GETEX, key, RedisLiterals.PERSIST)
            : Message.Create(Database, flags, RedisCommand.GETEX, key, RedisLiterals.PXAT, GetMillisecondsUntil(expiry));

        private Message GetStringGetWithExpiryMessage(RedisKey key, CommandFlags flags, out ResultProcessor<RedisValueWithExpiry> processor, out ServerEndPoint? server)
        {
            if (this is IBatch)
            {
                throw new NotSupportedException("This operation is not possible inside a transaction or batch; please issue separate GetString and KeyTimeToLive requests");
            }
            var features = GetFeatures(key, flags, out server);
            processor = StringGetWithExpiryProcessor.Default;
            if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PTTL))
            {
                return new StringGetWithExpiryMessage(Database, flags, RedisCommand.PTTL, key);
            }
            // if we use TTL, it doesn't matter which server
            server = null;
            return new StringGetWithExpiryMessage(Database, flags, RedisCommand.TTL, key);
        }

        private Message? GetStringSetMessage(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            switch (values.Length)
            {
                case 0: return null;
                case 1: return GetStringSetMessage(values[0].Key, values[0].Value, null, false, when, flags);
                default:
                    WhenAlwaysOrNotExists(when);
                    int slot = ServerSelectionStrategy.NoSlot, offset = 0;
                    var args = new RedisValue[values.Length * 2];
                    var serverSelectionStrategy = multiplexer.ServerSelectionStrategy;
                    for (int i = 0; i < values.Length; i++)
                    {
                        args[offset++] = values[i].Key.AsRedisValue();
                        args[offset++] = values[i].Value;
                        slot = serverSelectionStrategy.CombineSlot(slot, values[i].Key);
                    }
                    return Message.CreateInSlot(Database, slot, flags, when == When.NotExists ? RedisCommand.MSETNX : RedisCommand.MSET, args);
            }
        }

        private Message GetStringSetMessage(
            RedisKey key,
            RedisValue value,
            TimeSpan? expiry = null,
            bool keepTtl = false,
            When when = When.Always,
            CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExistsOrNotExists(when);
            if (value.IsNull) return Message.Create(Database, flags, RedisCommand.DEL, key);

            if (expiry == null || expiry.Value == TimeSpan.MaxValue)
            {
                // no expiry
                return when switch
                {
                    When.Always when !keepTtl    => Message.Create(Database, flags, RedisCommand.SET, key, value),
                    When.Always when keepTtl     => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.KEEPTTL),
                    When.NotExists when !keepTtl => Message.Create(Database, flags, RedisCommand.SETNX, key, value),
                    When.NotExists when keepTtl  => Message.Create(Database, flags, RedisCommand.SETNX, key, value, RedisLiterals.KEEPTTL),
                    When.Exists when !keepTtl    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.XX),
                    When.Exists when keepTtl     => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.XX, RedisLiterals.KEEPTTL),
                    _ => throw new ArgumentOutOfRangeException(nameof(when)),
                };
            }
            long milliseconds = expiry.Value.Ticks / TimeSpan.TicksPerMillisecond;

            if ((milliseconds % 1000) == 0)
            {
                // a nice round number of seconds
                long seconds = milliseconds / 1000;
                return when switch
                {
                    When.Always    => Message.Create(Database, flags, RedisCommand.SETEX, key, seconds, value),
                    When.Exists    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.XX),
                    When.NotExists => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.NX),
                    _ => throw new ArgumentOutOfRangeException(nameof(when)),
                };
            }

            return when switch
            {
                When.Always    => Message.Create(Database, flags, RedisCommand.PSETEX, key, milliseconds, value),
                When.Exists    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.XX),
                When.NotExists => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.NX),
                _ => throw new ArgumentOutOfRangeException(nameof(when)),
            };
        }

        private Message GetStringSetAndGetMessage(
            RedisKey key,
            RedisValue value,
            TimeSpan? expiry = null,
            bool keepTtl = false,
            When when = When.Always,
            CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExistsOrNotExists(when);
            if (value.IsNull) return Message.Create(Database, flags, RedisCommand.GETDEL, key);

            if (expiry == null || expiry.Value == TimeSpan.MaxValue)
            {
                // no expiry
                return when switch
                {
                    When.Always when !keepTtl    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.GET),
                    When.Always when keepTtl     => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.GET, RedisLiterals.KEEPTTL),
                    When.Exists when !keepTtl    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.XX, RedisLiterals.GET),
                    When.Exists when keepTtl     => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.XX, RedisLiterals.GET, RedisLiterals.KEEPTTL),
                    When.NotExists when !keepTtl => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.NX, RedisLiterals.GET),
                    When.NotExists when keepTtl  => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.NX, RedisLiterals.GET, RedisLiterals.KEEPTTL),
                    _ => throw new ArgumentOutOfRangeException(nameof(when)),
                };
            }
            long milliseconds = expiry.Value.Ticks / TimeSpan.TicksPerMillisecond;

            if ((milliseconds % 1000) == 0)
            {
                // a nice round number of seconds
                long seconds = milliseconds / 1000;
                return when switch
                {
                    When.Always    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.GET),
                    When.Exists    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.XX, RedisLiterals.GET),
                    When.NotExists => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.NX, RedisLiterals.GET),
                    _ => throw new ArgumentOutOfRangeException(nameof(when)),
                };
            }

            return when switch
            {
                When.Always    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.GET),
                When.Exists    => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.XX, RedisLiterals.GET),
                When.NotExists => Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.NX, RedisLiterals.GET),
                _ => throw new ArgumentOutOfRangeException(nameof(when)),
            };
        }

        private Message? IncrMessage(RedisKey key, long value, CommandFlags flags) => value switch
        {
            0 => ((flags & CommandFlags.FireAndForget) != 0)
                 ? null
                 : Message.Create(Database, flags, RedisCommand.INCRBY, key, value),
            1   => Message.Create(Database, flags, RedisCommand.INCR, key),
            -1  => Message.Create(Database, flags, RedisCommand.DECR, key),
            > 0 => Message.Create(Database, flags, RedisCommand.INCRBY, key, value),
            _   => Message.Create(Database, flags, RedisCommand.DECRBY, key, -value),
        };

        private static RedisCommand SetOperationCommand(SetOperation operation, bool store) => operation switch
        {
            SetOperation.Difference => store ? RedisCommand.SDIFFSTORE : RedisCommand.SDIFF,
            SetOperation.Intersect => store ? RedisCommand.SINTERSTORE : RedisCommand.SINTER,
            SetOperation.Union => store ? RedisCommand.SUNIONSTORE : RedisCommand.SUNION,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        private CursorEnumerable<T>? TryScan<T>(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags, RedisCommand command, ResultProcessor<ScanEnumerable<T>.ScanResult> processor, out ServerEndPoint? server)
        {
            server = null;
            if (pageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (!multiplexer.CommandMap.IsAvailable(command)) return null;

            var features = GetFeatures(key, flags, out server);
            if (!features.Scan) return null;

            if (CursorUtils.IsNil(pattern)) pattern = (byte[]?)null;
            return new ScanEnumerable<T>(this, server, key, pattern, pageSize, cursor, pageOffset, flags, command, processor);
        }

        private Message GetLexMessage(RedisCommand command, RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take, CommandFlags flags)
        {
            RedisValue start = GetLexRange(min, exclude, true), stop = GetLexRange(max, exclude, false);

            if (skip == 0 && take == -1)
                return Message.Create(Database, flags, command, key, start, stop);

            return Message.Create(Database, flags, command, key, new[] { start, stop, RedisLiterals.LIMIT, skip, take });
        }

        public long SortedSetLengthByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetLexMessage(RedisCommand.ZLEXCOUNT, key, min, max, exclude, 0, -1, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take, CommandFlags flags)
            => SortedSetRangeByValue(key, min, max, exclude, Order.Ascending, skip, take, flags);

        private static void ReverseLimits(Order order, ref Exclude exclude, ref RedisValue start, ref RedisValue stop)
        {
            bool reverseLimits = (order == Order.Ascending) == (stop != default && start.CompareTo(stop) > 0);
            if (reverseLimits)
            {
                var tmp = start;
                start = stop;
                stop = tmp;
                switch (exclude)
                {
                    case Exclude.Start: exclude = Exclude.Stop; break;
                    case Exclude.Stop: exclude = Exclude.Start; break;
                }
            }
        }
        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min = default, RedisValue max = default,
            Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            ReverseLimits(order, ref exclude, ref min, ref max);
            var msg = GetLexMessage(order == Order.Ascending ? RedisCommand.ZRANGEBYLEX : RedisCommand.ZREVRANGEBYLEX, key, min, max, exclude, skip, take, flags);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public long SortedSetRemoveRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetLexMessage(RedisCommand.ZREMRANGEBYLEX, key, min, max, exclude, 0, -1, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetLengthByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetLexMessage(RedisCommand.ZLEXCOUNT, key, min, max, exclude, 0, -1, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take, CommandFlags flags)
            => SortedSetRangeByValueAsync(key, min, max, exclude, Order.Ascending, skip, take, flags);

        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min = default, RedisValue max = default,
            Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            ReverseLimits(order, ref exclude, ref min, ref max);
            var msg = GetLexMessage(order == Order.Ascending ? RedisCommand.ZRANGEBYLEX : RedisCommand.ZREVRANGEBYLEX, key, min, max, exclude, skip, take, flags);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
        }

        public Task<long> SortedSetRemoveRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetLexMessage(RedisCommand.ZREMRANGEBYLEX, key, min, max, exclude, 0, -1, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        internal class ScanEnumerable<T> : CursorEnumerable<T>
        {
            private readonly RedisKey key;
            private readonly RedisValue pattern;
            private readonly RedisCommand command;

            public ScanEnumerable(RedisDatabase database, ServerEndPoint? server, RedisKey key, in RedisValue pattern, int pageSize, in RedisValue cursor, int pageOffset, CommandFlags flags,
                RedisCommand command, ResultProcessor<ScanResult> processor)
                : base(database, server, database.Database, pageSize, cursor, pageOffset, flags)
            {
                this.key = key;
                this.pattern = pattern;
                this.command = command;
                Processor = processor;
            }

            private protected override ResultProcessor<CursorEnumerable<T>.ScanResult> Processor { get; }

            private protected override Message CreateMessage(in RedisValue cursor)
            {
                if (CursorUtils.IsNil(pattern))
                {
                    if (pageSize == CursorUtils.DefaultRedisPageSize)
                    {
                        return Message.Create(db, flags, command, key, cursor);
                    }
                    else
                    {
                        return Message.Create(db, flags, command, key, cursor, RedisLiterals.COUNT, pageSize);
                    }
                }
                else
                {
                    if (pageSize == CursorUtils.DefaultRedisPageSize)
                    {
                        return Message.Create(db, flags, command, key, cursor, RedisLiterals.MATCH, pattern);
                    }
                    else
                    {
                        return Message.Create(db, flags, command, key, new RedisValue[] { cursor, RedisLiterals.MATCH, pattern, RedisLiterals.COUNT, pageSize });
                    }
                }
            }
        }

        internal sealed class ScriptLoadMessage : Message
        {
            internal readonly string Script;
            public ScriptLoadMessage(CommandFlags flags, string script)
                : base(-1, flags, RedisCommand.SCRIPT)
            {
                Script = script ?? throw new ArgumentNullException(nameof(script));
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.WriteBulkString(RedisLiterals.LOAD);
                physical.WriteBulkString((RedisValue)Script);
            }
            public override int ArgCount => 2;
        }

        private sealed class HashScanResultProcessor : ScanResultProcessor<HashEntry>
        {
            public static readonly ResultProcessor<ScanEnumerable<HashEntry>.ScanResult> Default = new HashScanResultProcessor();
            private HashScanResultProcessor() { }
            protected override HashEntry[]? Parse(in RawResult result, out int count)
                => HashEntryArray.TryParse(result, out HashEntry[]? pairs, true, out count) ? pairs : null;
        }

        private abstract class ScanResultProcessor<T> : ResultProcessor<ScanEnumerable<T>.ScanResult>
        {
            protected abstract T[]? Parse(in RawResult result, out int count);

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var arr = result.GetItems();
                        if (arr.Length == 2)
                        {
                            ref RawResult inner = ref arr[1];
                            if (inner.Type == ResultType.MultiBulk && arr[0].TryGetInt64(out var i64))
                            {
                                T[]? oversized = Parse(inner, out int count);
                                var sscanResult = new ScanEnumerable<T>.ScanResult(i64, oversized, count, true);
                                SetResult(message, sscanResult);
                                return true;
                            }
                        }
                        break;
                }
                return false;
            }
        }

        internal sealed class ExecuteMessage : Message
        {
            private readonly ICollection<object> _args;
            public new CommandBytes Command { get; }

            public ExecuteMessage(CommandMap? map, int db, CommandFlags flags, string command, ICollection<object>? args) : base(db, flags, RedisCommand.UNKNOWN)
            {
                if (args != null && args.Count >= PhysicalConnection.REDIS_MAX_ARGS) // using >= here because we will be adding 1 for the command itself (which is an arg for the purposes of the multi-bulk protocol)
                {
                    throw ExceptionFactory.TooManyArgs(command, args.Count);
                }
                Command = map?.GetBytes(command) ?? default;
                if (Command.IsEmpty) throw ExceptionFactory.CommandDisabled(command);
                _args = args ?? Array.Empty<object>();
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(RedisCommand.UNKNOWN, _args.Count, Command);
                foreach (object arg in _args)
                {
                    if (arg is RedisKey key)
                    {
                        physical.Write(key);
                    }
                    else if (arg is RedisChannel channel)
                    {
                        physical.Write(channel);
                    }
                    else
                    {   // recognises well-known types
                        var val = RedisValue.TryParse(arg, out var valid);
                        if (!valid) throw new InvalidCastException($"Unable to parse value: '{arg}'");
                        physical.WriteBulkString(val);
                    }
                }
            }

            public override string CommandAndKey => Command.ToString();

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                int slot = ServerSelectionStrategy.NoSlot;
                foreach (object arg in _args)
                {
                    if (arg is RedisKey key)
                    {
                        slot = serverSelectionStrategy.CombineSlot(slot, key);
                    }
                }
                return slot;
            }
            public override int ArgCount => _args.Count;
        }

        private sealed class ScriptEvalMessage : Message, IMultiMessage
        {
            private readonly RedisKey[] keys;
            private readonly string? script;
            private readonly RedisValue[] values;
            private byte[]? asciiHash;
            private readonly byte[]? hexHash;

            public ScriptEvalMessage(int db, CommandFlags flags, string script, RedisKey[]? keys, RedisValue[]? values)
                : this(db, flags, ResultProcessor.ScriptLoadProcessor.IsSHA1(script) ? RedisCommand.EVALSHA : RedisCommand.EVAL, script, null, keys, values)
            {
                if (script == null) throw new ArgumentNullException(nameof(script));
            }

            public ScriptEvalMessage(int db, CommandFlags flags, byte[] hash, RedisKey[]? keys, RedisValue[]? values)
                : this(db, flags, RedisCommand.EVALSHA, null, hash, keys, values)
            {
                if (hash == null) throw new ArgumentNullException(nameof(hash));
                if (hash.Length != ResultProcessor.ScriptLoadProcessor.Sha1HashLength) throw new ArgumentOutOfRangeException(nameof(hash), "Invalid hash length");
            }

            private ScriptEvalMessage(int db, CommandFlags flags, RedisCommand command, string? script, byte[]? hexHash, RedisKey[]? keys, RedisValue[]? values)
                : base(db, flags, command)
            {
                this.script = script;
                this.hexHash = hexHash;

                if (keys == null) keys = Array.Empty<RedisKey>();
                if (values == null) values = Array.Empty<RedisValue>();
                for (int i = 0; i < keys.Length; i++)
                    keys[i].AssertNotNull();
                this.keys = keys;
                for (int i = 0; i < values.Length; i++)
                    values[i].AssertNotNull();
                this.values = values;
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                int slot = ServerSelectionStrategy.NoSlot;
                for (int i = 0; i < keys.Length; i++)
                    slot = serverSelectionStrategy.CombineSlot(slot, keys[i]);
                return slot;
            }

            public IEnumerable<Message> GetMessages(PhysicalConnection connection)
            {
                PhysicalBridge? bridge;
                if (script != null && (bridge = connection.BridgeCouldBeNull) != null
                    && bridge.Multiplexer.CommandMap.IsAvailable(RedisCommand.SCRIPT)
                    && (Flags & CommandFlags.NoScriptCache) == 0)
                {
                    // a script was provided (rather than a hash); check it is known and supported
                    asciiHash = bridge.ServerEndPoint.GetScriptHash(script, command);

                    if (asciiHash == null)
                    {
                        var msg = new ScriptLoadMessage(Flags, script);
                        msg.SetInternalCall();
                        msg.SetSource(ResultProcessor.ScriptLoad, null);
                        yield return msg;
                    }
                }
                yield return this;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                if (hexHash != null)
                {
                    physical.WriteHeader(RedisCommand.EVALSHA, 2 + keys.Length + values.Length);
                    physical.WriteSha1AsHex(hexHash);
                }
                else if (asciiHash != null)
                {
                    physical.WriteHeader(RedisCommand.EVALSHA, 2 + keys.Length + values.Length);
                    physical.WriteBulkString((RedisValue)asciiHash);
                }
                else
                {
                    physical.WriteHeader(RedisCommand.EVAL, 2 + keys.Length + values.Length);
                    physical.WriteBulkString((RedisValue)script);
                }
                physical.WriteBulkString(keys.Length);
                for (int i = 0; i < keys.Length; i++)
                    physical.Write(keys[i]);
                for (int i = 0; i < values.Length; i++)
                    physical.WriteBulkString(values[i]);
            }
            public override int ArgCount => 2 + keys.Length + values.Length;
        }

        private sealed class SetScanResultProcessor : ScanResultProcessor<RedisValue>
        {
            public static readonly ResultProcessor<ScanEnumerable<RedisValue>.ScanResult> Default = new SetScanResultProcessor();
            private SetScanResultProcessor() { }
            protected override RedisValue[] Parse(in RawResult result, out int count)
            {
                var items = result.GetItems();
                if (items.IsEmpty)
                {
                    count = 0;
                    return Array.Empty<RedisValue>();
                }
                count = (int)items.Length;
                RedisValue[] arr = ArrayPool<RedisValue>.Shared.Rent(count);
                items.CopyTo(arr, (in RawResult r) => r.AsRedisValue());
                return arr;
            }
        }

        private static Message CreateListPositionMessage(int db, CommandFlags flags, RedisKey key, RedisValue element, long rank, long maxLen, long? count = null) =>
            count != null
                ? Message.Create(db, flags, RedisCommand.LPOS, key, element, RedisLiterals.RANK, rank, RedisLiterals.MAXLEN, maxLen, RedisLiterals.COUNT, count)
                : Message.Create(db, flags, RedisCommand.LPOS, key, element, RedisLiterals.RANK, rank, RedisLiterals.MAXLEN, maxLen);

        private static Message CreateSortedSetRangeStoreMessage(
            int db,
            CommandFlags flags,
            RedisKey sourceKey,
            RedisKey destinationKey,
            RedisValue start,
            RedisValue stop,
            SortedSetOrder sortedSetOrder,
            Order order,
            Exclude exclude,
            long skip,
            long? take)
        {
            if (sortedSetOrder == SortedSetOrder.ByRank)
            {
                if (take > 0)
                {
                    throw new ArgumentException("take argument is not valid when sortedSetOrder is ByRank you may want to try setting the SortedSetOrder to ByLex or ByScore", nameof(take));
                }
                if (exclude != Exclude.None)
                {
                    throw new ArgumentException("exclude argument is not valid when sortedSetOrder is ByRank, you may want to try setting the sortedSetOrder to ByLex or ByScore", nameof(exclude));
                }

                return order switch
                {
                    Order.Ascending => Message.Create(db, flags, RedisCommand.ZRANGESTORE, destinationKey, sourceKey, start, stop),
                    Order.Descending => Message.Create(db, flags, RedisCommand.ZRANGESTORE, destinationKey, sourceKey, start, stop, RedisLiterals.REV),
                    _ => throw new ArgumentOutOfRangeException(nameof(order))
                };
            }

            RedisValue formattedStart = exclude switch
            {
                Exclude.Both or Exclude.Start => $"({start}",
                _ when sortedSetOrder == SortedSetOrder.ByLex => $"[{start}",
                _ => start
            };

            RedisValue formattedStop = exclude switch
            {
                Exclude.Both or Exclude.Stop => $"({stop}",
                _ when sortedSetOrder == SortedSetOrder.ByLex => $"[{stop}",
                _ => stop
            };

            return order switch
            {
                Order.Ascending when take != null && take > 0 =>
                    Message.Create(db, flags, RedisCommand.ZRANGESTORE, destinationKey, sourceKey, formattedStart, formattedStop, sortedSetOrder.GetLiteral(), RedisLiterals.LIMIT, skip, take),
                Order.Ascending =>
                    Message.Create(db, flags, RedisCommand.ZRANGESTORE, destinationKey, sourceKey, formattedStart, formattedStop, sortedSetOrder.GetLiteral()),
                Order.Descending when take != null && take > 0 =>
                    Message.Create(db, flags, RedisCommand.ZRANGESTORE, destinationKey, sourceKey, formattedStart, formattedStop, sortedSetOrder.GetLiteral(), RedisLiterals.REV, RedisLiterals.LIMIT, skip, take),
                Order.Descending =>
                    Message.Create(db, flags, RedisCommand.ZRANGESTORE, destinationKey, sourceKey, formattedStart, formattedStop, sortedSetOrder.GetLiteral(), RedisLiterals.REV),
                _ => throw new ArgumentOutOfRangeException(nameof(order))
            };
        }

        private sealed class SortedSetCombineAndStoreCommandMessage : Message.CommandKeyBase // ZINTERSTORE and ZUNIONSTORE have a very unusual signature
        {
            private readonly RedisKey[] keys;
            private readonly RedisValue[] values;
            public SortedSetCombineAndStoreCommandMessage(int db, CommandFlags flags, RedisCommand command, RedisKey destination, RedisKey[] keys, RedisValue[] values)
                : base(db, flags, command, destination)
            {
                for (int i = 0; i < keys.Length; i++)
                    keys[i].AssertNotNull();
                this.keys = keys;
                for (int i = 0; i < values.Length; i++)
                    values[i].AssertNotNull();
                this.values = values;
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                int slot = base.GetHashSlot(serverSelectionStrategy);
                for (int i = 0; i < keys.Length; i++)
                    slot = serverSelectionStrategy.CombineSlot(slot, keys[i]);
                return slot;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2 + keys.Length + values.Length);
                physical.Write(Key);
                physical.WriteBulkString(keys.Length);
                for (int i = 0; i < keys.Length; i++)
                    physical.Write(keys[i]);
                for (int i = 0; i < values.Length; i++)
                    physical.WriteBulkString(values[i]);
            }
            public override int ArgCount => 2 + keys.Length + values.Length;
        }

        private sealed class SortedSetScanResultProcessor : ScanResultProcessor<SortedSetEntry>
        {
            public static readonly ResultProcessor<ScanEnumerable<SortedSetEntry>.ScanResult> Default = new SortedSetScanResultProcessor();
            private SortedSetScanResultProcessor() { }
            protected override SortedSetEntry[]? Parse(in RawResult result, out int count)
                => SortedSetWithScores.TryParse(result, out SortedSetEntry[]? pairs, true, out count) ? pairs : null;
        }

        private class StringGetWithExpiryMessage : Message.CommandKeyBase, IMultiMessage
        {
            private readonly RedisCommand ttlCommand;
            private IResultBox<TimeSpan?>? box;

            public StringGetWithExpiryMessage(int db, CommandFlags flags, RedisCommand ttlCommand, in RedisKey key)
                : base(db, flags, RedisCommand.GET, key)
            {
                this.ttlCommand = ttlCommand;
            }

            public override string CommandAndKey => ttlCommand + "+" + RedisCommand.GET + " " + (string?)Key;

            public IEnumerable<Message> GetMessages(PhysicalConnection connection)
            {
                box = SimpleResultBox<TimeSpan?>.Create();
                var ttl = Message.Create(Db, Flags, ttlCommand, Key);
                var proc = ttlCommand == RedisCommand.PTTL ? ResultProcessor.TimeSpanFromMilliseconds : ResultProcessor.TimeSpanFromSeconds;
                ttl.SetSource(proc, box);
                yield return ttl;
                yield return this;
            }

            public bool UnwrapValue(out TimeSpan? value, out Exception? ex)
            {
                if (box != null)
                {
                    value = box.GetResult(out ex);
                    box = null;
                    return ex == null;
                }
                value = null;
                ex = null;
                return false;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, 1);
                physical.Write(Key);
            }
            public override int ArgCount => 1;
        }

        private class StringGetWithExpiryProcessor : ResultProcessor<RedisValueWithExpiry>
        {
            public static readonly ResultProcessor<RedisValueWithExpiry> Default = new StringGetWithExpiryProcessor();
            private StringGetWithExpiryProcessor() { }
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        RedisValue value = result.AsRedisValue();
                        if (message is StringGetWithExpiryMessage sgwem && sgwem.UnwrapValue(out var expiry, out var ex))
                        {
                            if (ex == null)
                            {
                                SetResult(message, new RedisValueWithExpiry(value, expiry));
                            }
                            else
                            {
                                SetException(message, ex);
                            }
                            return true;
                        }
                        break;
                }
                return false;
            }
        }
    }
}
