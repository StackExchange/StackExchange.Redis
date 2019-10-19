using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal class RedisDatabase : RedisBase, IDatabase
    {
        internal RedisDatabase(ConnectionMultiplexer multiplexer, int db, object asyncState)
            : base(multiplexer, asyncState)
        {
            Database = db;
        }

        public object AsyncState => asyncState;

        public int Database { get; }

        public IBatch CreateBatch(object asyncState)
        {
            if (this is IBatch) throw new NotSupportedException("Nested batches are not supported");
            return new RedisBatch(this, asyncState);
        }

        public ITransaction CreateTransaction(object asyncState)
        {
            if (this is IBatch) throw new NotSupportedException("Nested transactions are not supported");
            return new RedisTransaction(this, asyncState);
        }

        private ITransaction CreateTransactionIfAvailable(object asyncState)
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

        public string[] GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var redisValues = new RedisValue[members.Length];
            for (var i = 0; i < members.Length; i++) redisValues[i] = members[i];
            var msg = Message.Create(Database, flags, RedisCommand.GEOHASH, key, redisValues);
            return ExecuteSync(msg, ResultProcessor.StringArray);
        }

        public Task<string[]> GeoHashAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var redisValues = new RedisValue[members.Length];
            for (var i = 0; i < members.Length; i++) redisValues[i] = members[i];
            var msg = Message.Create(Database, flags, RedisCommand.GEOHASH, key, redisValues);
            return ExecuteAsync(msg, ResultProcessor.StringArray);
        }

        public string GeoHash(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GEOHASH, key, member);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        public Task<string> GeoHashAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
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
            return ExecuteSync(msg, ResultProcessor.RedisGeoPositionArray);
        }

        public Task<GeoPosition?[]> GeoPositionAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var redisValues = new RedisValue[members.Length];
            for (var i = 0; i < members.Length; i++) redisValues[i] = members[i];
            var msg = Message.Create(Database, flags, RedisCommand.GEOPOS, key, redisValues);
            return ExecuteAsync(msg, ResultProcessor.RedisGeoPositionArray);
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

        private static readonly RedisValue
            WITHCOORD = Encoding.ASCII.GetBytes("WITHCOORD"),
            WITHDIST = Encoding.ASCII.GetBytes("WITHDIST"),
            WITHHASH = Encoding.ASCII.GetBytes("WITHHASH"),
            COUNT = Encoding.ASCII.GetBytes("COUNT"),
            ASC = Encoding.ASCII.GetBytes("ASC"),
            DESC = Encoding.ASCII.GetBytes("DESC");
        private Message GetGeoRadiusMessage(in RedisKey key, RedisValue? member, double longitude, double latitude, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            var redisValues = new List<RedisValue>();
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
            redisValues.Add(StackExchange.Redis.GeoPosition.GetRedisUnit(unit));
            if ((options & GeoRadiusOptions.WithCoordinates) != 0) redisValues.Add(WITHCOORD);
            if ((options & GeoRadiusOptions.WithDistance) != 0) redisValues.Add(WITHDIST);
            if ((options & GeoRadiusOptions.WithGeoHash) != 0) redisValues.Add(WITHHASH);
            if (count > 0)
            {
                redisValues.Add(COUNT);
                redisValues.Add(count);
            }
            if (order != null)
            {
                switch (order.Value)
                {
                    case Order.Ascending: redisValues.Add(ASC); break;
                    case Order.Descending: redisValues.Add(DESC); break;
                    default: throw new ArgumentOutOfRangeException(nameof(order));
                }
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
            return ExecuteSync(GetGeoRadiusMessage(key, member, double.NaN, double.NaN, radius, unit, count, order, options, flags), ResultProcessor.GeoRadiusArray(options));
        }

        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, RedisValue member, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            // This gets confused with the double overload below sometimes...throwing when this occurs.
            if (member.Type == RedisValue.StorageType.Double)
            {
                throw new ArgumentException("Member should not be a double, you likely want the GeoRadius(RedisKey, double, double, ...) overload.", nameof(member));
            }
            return ExecuteAsync(GetGeoRadiusMessage(key, member, double.NaN, double.NaN, radius, unit, count, order, options, flags), ResultProcessor.GeoRadiusArray(options));
        }

        public GeoRadiusResult[] GeoRadius(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            return ExecuteSync(GetGeoRadiusMessage(key, null, longitude, latitude, radius, unit, count, order, options, flags), ResultProcessor.GeoRadiusArray(options));
        }

        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            return ExecuteAsync(GetGeoRadiusMessage(key, null, longitude, latitude, radius, unit, count, order, options, flags), ResultProcessor.GeoRadiusArray(options));
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

        public Lease<byte> HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGET, key, hashField);
            return ExecuteSync(msg, ResultProcessor.Lease);
        }

        public RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException(nameof(hashFields));
            if (hashFields.Length == 0) return Array.Empty<RedisValue>();
            var msg = Message.Create(Database, flags, RedisCommand.HMGET, key, hashFields);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public HashEntry[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGETALL, key);
            return ExecuteSync(msg, ResultProcessor.HashEntryArray);
        }

        public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGETALL, key);
            return ExecuteAsync(msg, ResultProcessor.HashEntryArray);
        }

        public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGET, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<Lease<byte>> HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HGET, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.Lease);
        }

        public Task<RedisValue[]> HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException(nameof(hashFields));
            if (hashFields.Length == 0) return CompletedTask<RedisValue[]>.FromResult(new RedisValue[0], asyncState);
            var msg = Message.Create(Database, flags, RedisCommand.HMGET, key, hashFields);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HKEYS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HLEN, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HLEN, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        {
            return HashScan(key, pattern, pageSize, CursorUtils.Origin, 0, flags);
        }

        public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default(RedisValue), int pageSize = CursorUtils.DefaultPageSize, long cursor = CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        {
            var scan = TryScan<HashEntry>(key, pattern, pageSize, cursor, pageOffset, flags, RedisCommand.HSCAN, HashScanResultProcessor.Default);
            if (scan != null) return scan;

            if (cursor != 0 || pageOffset != 0) throw ExceptionFactory.NoCursor(RedisCommand.HGETALL);
            if (pattern.IsNull) return HashGetAll(key, flags);
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
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.HVALS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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
            var features = GetFeatures(key, flags, out ServerEndPoint server);
            var cmd = Message.Create(Database, flags, RedisCommand.PFCOUNT, key);
            // technically a write / master-only command until 2.8.18
            if (server != null && !features.HyperLogLogCountSlaveSafe) cmd.SetMasterOnly();
            return ExecuteSync(cmd, ResultProcessor.Int64, server);
        }

        public long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            ServerEndPoint server = null;
            var cmd = Message.Create(Database, flags, RedisCommand.PFCOUNT, keys);
            if (keys.Length != 0)
            {
                var features = GetFeatures(keys[0], flags, out server);
                // technically a write / master-only command until 2.8.18
                if (server != null && !features.HyperLogLogCountSlaveSafe) cmd.SetMasterOnly();
            }
            return ExecuteSync(cmd, ResultProcessor.Int64, server);
        }

        public Task<long> HyperLogLogLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var features = GetFeatures(key, flags, out ServerEndPoint server);
            var cmd = Message.Create(Database, flags, RedisCommand.PFCOUNT, key);
            // technically a write / master-only command until 2.8.18
            if (server != null && !features.HyperLogLogCountSlaveSafe) cmd.SetMasterOnly();
            return ExecuteAsync(cmd, ResultProcessor.Int64, server);
        }

        public Task<long> HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            ServerEndPoint server = null;
            var cmd = Message.Create(Database, flags, RedisCommand.PFCOUNT, keys);
            if (keys.Length != 0)
            {
                var features = GetFeatures(keys[0], flags, out server);
                // technically a write / master-only command until 2.8.18
                if (server != null && !features.HyperLogLogCountSlaveSafe) cmd.SetMasterOnly();
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

        public EndPoint IdentifyEndpoint(RedisKey key = default(RedisKey), CommandFlags flags = CommandFlags.None)
        {
            var msg = key.IsNull ? Message.Create(-1, flags, RedisCommand.PING) : Message.Create(Database, flags, RedisCommand.EXISTS, key);
            return ExecuteSync(msg, ResultProcessor.ConnectionIdentity);
        }

        public Task<EndPoint> IdentifyEndpointAsync(RedisKey key = default(RedisKey), CommandFlags flags = CommandFlags.None)
        {
            var msg = key.IsNull ? Message.Create(-1, flags, RedisCommand.PING) : Message.Create(Database, flags, RedisCommand.EXISTS, key);
            return ExecuteAsync(msg, ResultProcessor.ConnectionIdentity);
        }

        public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var server = multiplexer.SelectServer(RedisCommand.PING, flags, key);
            return server?.IsConnected == true;
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

        private RedisCommand GetDeleteCommand(RedisKey key, CommandFlags flags, out ServerEndPoint server)
        {
            var features = GetFeatures(key, flags, out server);
            if (server != null && features.Unlink && multiplexer.CommandMap.IsAvailable(RedisCommand.UNLINK))
            {
                return RedisCommand.UNLINK;
            }
            return RedisCommand.DEL;
        }

        public byte[] KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.DUMP, key);
            return ExecuteSync(msg, ResultProcessor.ByteArray);
        }

        public Task<byte[]> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.DUMP, key);
            return ExecuteAsync(msg, ResultProcessor.ByteArray);
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

        public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetExpiryMessage(key, flags, expiry, out ServerEndPoint server);
            return ExecuteSync(msg, ResultProcessor.Boolean, server: server);
        }

        public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetExpiryMessage(key, flags, expiry, out ServerEndPoint server);
            return ExecuteSync(msg, ResultProcessor.Boolean, server: server);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetExpiryMessage(key, flags, expiry, out ServerEndPoint server);
            return ExecuteAsync(msg, ResultProcessor.Boolean, server: server);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetExpiryMessage(key, flags, expiry, out ServerEndPoint server);
            return ExecuteAsync(msg, ResultProcessor.Boolean, server: server);
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
                if (!Format.TryGetHostPort(toServer, out string toHost, out int toPort)) throw new ArgumentException("toServer");
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
            var features = GetFeatures(key, flags, out ServerEndPoint server);
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
            var features = GetFeatures(key, flags, out ServerEndPoint server);
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

        public Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LPOP, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Database, flags, when == When.Always ? RedisCommand.LPUSH : RedisCommand.LPUSHX, key, value);
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

        public RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LRANGE, key, start, stop);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.LRANGE, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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

        public Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.RPOP, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
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

        public RedisResult ScriptEvaluate(string script, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Database, flags, script, keys, values);
            try
            {
                return ExecuteSync(msg, ResultProcessor.ScriptResult);
            }
            catch (RedisServerException)
            {
                // could be a NOSCRIPT; for a sync call, we can re-issue that without problem
                if (msg.IsScriptUnavailable) return ExecuteSync(msg, ResultProcessor.ScriptResult);
                throw;
            }
        }

        public RedisResult Execute(string command, params object[] args)
            => Execute(command, args, CommandFlags.None);
        public RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ExecuteMessage(multiplexer?.CommandMap, Database, flags, command, args);
            return ExecuteSync(msg, ResultProcessor.ScriptResult);
        }

        public Task<RedisResult> ExecuteAsync(string command, params object[] args)
            => ExecuteAsync(command, args, CommandFlags.None);
        public Task<RedisResult> ExecuteAsync(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ExecuteMessage(multiplexer?.CommandMap, Database, flags, command, args);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult);
        }

        public RedisResult ScriptEvaluate(byte[] hash, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Database, flags, hash, keys, values);
            return ExecuteSync(msg, ResultProcessor.ScriptResult);
        }

        public RedisResult ScriptEvaluate(LuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return script.Evaluate(this, parameters, null, flags);
        }

        public RedisResult ScriptEvaluate(LoadedLuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return script.Evaluate(this, parameters, null, flags);
        }

        public Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Database, flags, script, keys, values);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult);
        }

        public Task<RedisResult> ScriptEvaluateAsync(byte[] hash, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Database, flags, hash, keys, values);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult);
        }

        public Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None)
        {
            return script.EvaluateAsync(this, parameters, null, flags);
        }

        public Task<RedisResult> ScriptEvaluateAsync(LoadedLuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None)
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
            var msg = Message.Create(Database, flags, RedisCommand.SADD, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue[] SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, false), first, second);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, false), keys);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
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
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, SetOperationCommand(operation, false), keys);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SMEMBERS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            if(count == 0) return Task.FromResult(Array.Empty<RedisValue>());
            var msg = count == 1
                    ? Message.Create(Database, flags, RedisCommand.SPOP, key)
                    : Message.Create(Database, flags, RedisCommand.SPOP, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SRANDMEMBER, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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
        {
            return SetScan(key, pattern, pageSize, CursorUtils.Origin, 0, flags);
        }

        public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default(RedisValue), int pageSize = CursorUtils.DefaultPageSize, long cursor = CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        {
            var scan = TryScan<RedisValue>(key, pattern, pageSize, cursor, pageOffset, flags, RedisCommand.SSCAN, SetScanResultProcessor.Default);
            if (scan != null) return scan;

            if (cursor != 0 || pageOffset != 0) throw ExceptionFactory.NoCursor(RedisCommand.SMEMBERS);
            if (pattern.IsNull) return SetMembers(key, flags);
            throw ExceptionFactory.NotSupported(true, RedisCommand.SSCAN);
        }

        public RedisValue[] Sort(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default(RedisValue), RedisValue[] get = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(default(RedisKey), key, skip, take, order, sortType, by, get, flags);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public long SortAndStore(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default(RedisValue), RedisValue[] get = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(destination, key, skip, take, order, sortType, by, get, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortAndStoreAsync(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default(RedisValue), RedisValue[] get = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(destination, key, skip, take, order, sortType, by, get, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<RedisValue[]> SortAsync(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default(RedisValue), RedisValue[] get = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(default(RedisKey), key, skip, take, order, sortType, by, get, flags);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags)
        {
            var msg = GetSortedSetAddMessage(key, member, score, When.Always, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, member, score, when, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags)
        {
            var msg = GetSortedSetAddMessage(key, values, When.Always, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, values, when, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, CommandFlags flags)
        {
            var msg = GetSortedSetAddMessage(key, member, score, When.Always, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, member, score, when, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, CommandFlags flags)
        {
            var msg = GetSortedSetAddMessage(key, values, When.Always, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, values, when, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineAndStoreCommandMessage(operation, destination, new[] { first, second }, null, aggregate, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, double[] weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineAndStoreCommandMessage(operation, destination, keys, weights, aggregate, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetCombineAndStoreCommandMessage(operation, destination, new[] { first, second }, null, aggregate, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, double[] weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
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

        public RedisValue[] SortedSetRangeByRank(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SortedSetRangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public SortedSetEntry[] SortedSetRangeByRankWithScores(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop, RedisLiterals.WITHSCORES);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores);
        }

        public Task<SortedSetEntry[]> SortedSetRangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop, RedisLiterals.WITHSCORES);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores);
        }

        public RedisValue[] SortedSetRangeByScore(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, false);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SortedSetRangeByScoreAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, false);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public SortedSetEntry[] SortedSetRangeByScoreWithScores(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, true);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores);
        }

        public Task<SortedSetEntry[]> SortedSetRangeByScoreWithScoresAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, true);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores);
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
        {
            return SortedSetScan(key, pattern, pageSize, CursorUtils.Origin, 0, flags);
        }

        public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default(RedisValue), int pageSize = CursorUtils.DefaultPageSize, long cursor = CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        {
            var scan = TryScan<SortedSetEntry>(key, pattern, pageSize, cursor, pageOffset, flags, RedisCommand.ZSCAN, SortedSetScanResultProcessor.Default);
            if (scan != null) return scan;

            if (cursor != 0 || pageOffset != 0) throw ExceptionFactory.NoCursor(RedisCommand.ZRANGE);
            if (pattern.IsNull) return SortedSetRangeByRankWithScores(key, flags: flags);
            throw ExceptionFactory.NotSupported(true, RedisCommand.ZSCAN);
        }

        public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZSCORE, key, member);
            return ExecuteSync(msg, ResultProcessor.NullableDouble);
        }

        public Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.ZSCORE, key, member);
            return ExecuteAsync(msg, ResultProcessor.NullableDouble);
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
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores);
        }

        public Task<SortedSetEntry[]> SortedSetPopAsync(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            if (count == 0) return Task.FromResult(Array.Empty<SortedSetEntry>());
            var msg = count == 1
                    ? Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN, key)
                    : Message.Create(Database, flags, order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN, key, count);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores);
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

        public StreamEntry[] StreamClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamClaimMessage(key,
                consumerGroup,
                claimingConsumer,
                minIdleTimeInMs,
                messageIds,
                returnJustIds: false,
                flags: flags);

            return ExecuteSync(msg, ResultProcessor.SingleStream);
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

            return ExecuteAsync(msg, ResultProcessor.SingleStream);
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

            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
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

            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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

        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position = null, CommandFlags flags = CommandFlags.None)
        {
            var actualPosition = position ?? StreamConstants.NewMessages;

            var msg = Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                new RedisValue[]
                {
                    StreamConstants.Create,
                    key.AsRedisValue(),
                    groupName,
                    StreamPosition.Resolve(actualPosition, RedisCommand.XGROUP)
                });

            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position = null, CommandFlags flags = CommandFlags.None)
        {
            var actualPosition = position ?? StreamPosition.NewMessages;

            var msg = Message.Create(Database,
                flags,
                RedisCommand.XGROUP,
                new RedisValue[]
                {
                    StreamConstants.Create,
                    key.AsRedisValue(),
                    groupName,
                    StreamPosition.Resolve(actualPosition, RedisCommand.XGROUP)
                });

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

            return ExecuteSync(msg, ResultProcessor.StreamConsumerInfo);
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

            return ExecuteAsync(msg, ResultProcessor.StreamConsumerInfo);
        }

        public StreamGroupInfo[] StreamGroupInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XINFO, StreamConstants.Groups, key);
            return ExecuteSync(msg, ResultProcessor.StreamGroupInfo);
        }

        public Task<StreamGroupInfo[]> StreamGroupInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.XINFO, StreamConstants.Groups, key);
            return ExecuteAsync(msg, ResultProcessor.StreamGroupInfo);
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

            return ExecuteSync(msg, ResultProcessor.StreamPendingMessages);
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

            return ExecuteAsync(msg, ResultProcessor.StreamPendingMessages);
        }

        public StreamEntry[] StreamRange(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamRangeMessage(key,
                minId,
                maxId,
                count,
                messageOrder,
                flags);

            return ExecuteSync(msg, ResultProcessor.SingleStream);
        }

        public Task<StreamEntry[]> StreamRangeAsync(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStreamRangeMessage(key,
                minId,
                maxId,
                count,
                messageOrder,
                flags);

            return ExecuteAsync(msg, ResultProcessor.SingleStream);
        }

        public StreamEntry[] StreamRead(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSingleStreamReadMessage(key,
                StreamPosition.Resolve(position, RedisCommand.XREAD),
                count,
                flags);

            return ExecuteSync(msg, ResultProcessor.SingleStreamWithNameSkip);
        }

        public Task<StreamEntry[]> StreamReadAsync(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSingleStreamReadMessage(key,
                StreamPosition.Resolve(position, RedisCommand.XREAD),
                count,
                flags);

            return ExecuteAsync(msg, ResultProcessor.SingleStreamWithNameSkip);
        }

        public RedisStream[] StreamRead(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetMultiStreamReadMessage(streamPositions, countPerStream, flags);
            return ExecuteSync(msg, ResultProcessor.MultiStream);
        }

        public Task<RedisStream[]> StreamReadAsync(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetMultiStreamReadMessage(streamPositions, countPerStream, flags);
            return ExecuteAsync(msg, ResultProcessor.MultiStream);
        }

        public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var actualPosition = position ?? StreamPosition.NewMessages;

            var msg = GetStreamReadGroupMessage(key,
                groupName,
                consumerName,
                StreamPosition.Resolve(actualPosition, RedisCommand.XREADGROUP),
                count,
                flags);

            return ExecuteSync(msg, ResultProcessor.SingleStreamWithNameSkip);
        }

        public Task<StreamEntry[]> StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, CommandFlags flags = CommandFlags.None)
        {
            var actualPosition = position ?? StreamPosition.NewMessages;

            var msg = GetStreamReadGroupMessage(key,
                groupName,
                consumerName,
                StreamPosition.Resolve(actualPosition, RedisCommand.XREADGROUP),
                count,
                flags);

            return ExecuteAsync(msg, ResultProcessor.SingleStreamWithNameSkip);
        }

        public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetMultiStreamReadGroupMessage(streamPositions, groupName, consumerName, countPerStream, flags);
            return ExecuteSync(msg, ResultProcessor.MultiStream);
        }

        public Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetMultiStreamReadGroupMessage(streamPositions, groupName, consumerName, countPerStream, flags);
            return ExecuteAsync(msg, ResultProcessor.MultiStream);
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

        public long StringBitCount(RedisKey key, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.BITCOUNT, key, start, end);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringBitCountAsync(RedisKey key, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.BITCOUNT, key, start, end);
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

        public long StringBitPosition(RedisKey key, bool bit, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.BITPOS, key, bit, start, end);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringBitPositionAsync(RedisKey key, bool value, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.BITPOS, key, value, start, end);
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

        public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length == 0) return Array.Empty<RedisValue>();
            var msg = Message.Create(Database, flags, RedisCommand.MGET, keys);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Lease<byte> StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GET, key);
            return ExecuteSync(msg, ResultProcessor.Lease);
        }

        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GET, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<Lease<byte>> StringGetLeaseAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.GET, key);
            return ExecuteAsync(msg, ResultProcessor.Lease);
        }

        public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (keys.Length == 0) return CompletedTask<RedisValue[]>.FromResult(Array.Empty<RedisValue>(), asyncState);
            var msg = Message.Create(Database, flags, RedisCommand.MGET, keys);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
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

        public RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringGetWithExpiryMessage(key, flags, out ResultProcessor<RedisValueWithExpiry> processor, out ServerEndPoint server);
            return ExecuteSync(msg, processor, server);
        }

        public Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringGetWithExpiryMessage(key, flags, out ResultProcessor<RedisValueWithExpiry> processor, out ServerEndPoint server);
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

        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetMessage(key, value, expiry, when, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public bool StringSet(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetMessage(values, when, flags);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetMessage(key, value, expiry, when, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetStringSetMessage(values, when, flags);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
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

        public Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Database, flags, RedisCommand.SETRANGE, key, offset, value);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        private Message GetExpiryMessage(in RedisKey key, CommandFlags flags, TimeSpan? expiry, out ServerEndPoint server)
        {
            TimeSpan duration;
            if (expiry == null || (duration = expiry.Value) == TimeSpan.MaxValue)
            {
                server = null;
                return Message.Create(Database, flags, RedisCommand.PERSIST, key);
            }
            long milliseconds = duration.Ticks / TimeSpan.TicksPerMillisecond;
            if ((milliseconds % 1000) != 0)
            {
                var features = GetFeatures(key, flags, out server);
                if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PEXPIRE))
                {
                    return Message.Create(Database, flags, RedisCommand.PEXPIRE, key, milliseconds);
                }
            }
            server = null;
            long seconds = milliseconds / 1000;
            return Message.Create(Database, flags, RedisCommand.EXPIRE, key, seconds);
        }

        private Message GetExpiryMessage(in RedisKey key, CommandFlags flags, DateTime? expiry, out ServerEndPoint server)
        {
            DateTime when;
            if (expiry == null || (when = expiry.Value) == DateTime.MaxValue)
            {
                server = null;
                return Message.Create(Database, flags, RedisCommand.PERSIST, key);
            }
            switch (when.Kind)
            {
                case DateTimeKind.Local:
                case DateTimeKind.Utc:
                    break; // fine, we can work with that
                default:
                    throw new ArgumentException("Expiry time must be either Utc or Local", nameof(expiry));
            }
            long milliseconds = (when.ToUniversalTime() - RedisBase.UnixEpoch).Ticks / TimeSpan.TicksPerMillisecond;

            if ((milliseconds % 1000) != 0)
            {
                var features = GetFeatures(key, flags, out server);
                if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PEXPIREAT))
                {
                    return Message.Create(Database, flags, RedisCommand.PEXPIREAT, key, milliseconds);
                }
            }
            server = null;
            long seconds = milliseconds / 1000;
            return Message.Create(Database, flags, RedisCommand.EXPIREAT, key, seconds);
        }

        private Message GetHashSetMessage(RedisKey key, HashEntry[] hashFields, CommandFlags flags)
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

        private ITransaction GetLockExtendTransaction(RedisKey key, RedisValue value, TimeSpan expiry)
        {
            var tran = CreateTransactionIfAvailable(asyncState);
            if (tran != null)
            {
                tran.AddCondition(Condition.StringEqual(key, value));
                tran.KeyExpireAsync(key, expiry, CommandFlags.FireAndForget);
            }
            return tran;
        }

        private ITransaction GetLockReleaseTransaction(RedisKey key, RedisValue value)
        {
            var tran = CreateTransactionIfAvailable(asyncState);
            if (tran != null)
            {
                tran.AddCondition(Condition.StringEqual(key, value));
                tran.KeyDeleteAsync(key, CommandFlags.FireAndForget);
            }
            return tran;
        }

        private RedisValue GetLexRange(RedisValue value, Exclude exclude, bool isStart)
        {
            if (value.IsNull)
            {
                return isStart ? RedisLiterals.MinusSymbol : RedisLiterals.PlusSumbol;
            }
            byte[] orig = value;

            byte[] result = new byte[orig.Length + 1];
            // no defaults here; must always explicitly specify [ / (
            result[0] = (exclude & (isStart ? Exclude.Start : Exclude.Stop)) == 0 ? (byte)'[' : (byte)'(';
            Buffer.BlockCopy(orig, 0, result, 1, orig.Length);
            return result;
        }

        private Message GetMultiStreamReadGroupMessage(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags)
        {
            // Example: XREADGROUP GROUP groupName consumerName COUNT countPerStream STREAMS stream1 stream2 id1 id2
            if (streamPositions == null) throw new ArgumentNullException(nameof(streamPositions));
            if (streamPositions.Length == 0) throw new ArgumentOutOfRangeException(nameof(streamPositions), "streamOffsetPairs must contain at least one item.");

            if (countPerStream.HasValue && countPerStream <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(countPerStream), "countPerStream must be greater than 0.");
            }

            var values = new RedisValue[
                4                                       // Room for GROUP groupName consumerName & STREAMS
                + (streamPositions.Length * 2)          // Enough room for the stream keys and associated IDs.
                + (countPerStream.HasValue ? 2 : 0)];   // Room for "COUNT num" or 0 if countPerStream is null.

            var offset = 0;

            values[offset++] = StreamConstants.Group;
            values[offset++] = groupName;
            values[offset++] = consumerName;

            if (countPerStream.HasValue)
            {
                values[offset++] = StreamConstants.Count;
                values[offset++] = countPerStream;
            }

            values[offset++] = StreamConstants.Streams;

            var pairCount = streamPositions.Length;

            for (var i = 0; i < pairCount; i++)
            {
                values[offset] = streamPositions[i].Key.AsRedisValue();
                values[offset + pairCount] = StreamPosition.Resolve(streamPositions[i].Position, RedisCommand.XREADGROUP);

                offset++;
            }

            return Message.Create(Database, flags, RedisCommand.XREADGROUP, values);
        }

        private Message GetMultiStreamReadMessage(StreamPosition[] streamPositions, int? countPerStream, CommandFlags flags)
        {
            // Example: XREAD COUNT 2 STREAMS mystream writers 0-0 0-0

            if (streamPositions == null) throw new ArgumentNullException(nameof(streamPositions));
            if (streamPositions.Length == 0) throw new ArgumentOutOfRangeException(nameof(streamPositions), "streamOffsetPairs must contain at least one item.");

            if (countPerStream.HasValue && countPerStream <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(countPerStream), "countPerStream must be greater than 0.");
            }

            var values = new RedisValue[
                1                                     // Streams keyword.
                + (streamPositions.Length * 2)          // Room for the stream names and the ID after which to begin reading.
                + (countPerStream.HasValue ? 2 : 0)]; // Room for "COUNT num" or 0 if countPerStream is null.

            var offset = 0;

            if (countPerStream.HasValue)
            {
                values[offset++] = StreamConstants.Count;
                values[offset++] = countPerStream;
            }

            values[offset++] = StreamConstants.Streams;

            // Write the stream names and the message IDs from which to read for the associated stream. Each pair
            // will be separated by an offset of the index of the stream name plus the pair count.

            /*
             * [0] = COUNT
             * [1] = 2
             * [3] = STREAMS
             * [4] = stream1
             * [5] = stream2
             * [6] = stream3
             * [7] = id1
             * [8] = id2
             * [9] = id3
             * 
             * */

            var pairCount = streamPositions.Length;

            for (var i = 0; i < pairCount; i++)
            {
                values[offset] = streamPositions[i].Key.AsRedisValue();
                values[offset + pairCount] = StreamPosition.Resolve(streamPositions[i].Position, RedisCommand.XREAD);

                offset++;
            }

            return Message.Create(Database, flags, RedisCommand.XREAD, values);
        }

        private RedisValue GetRange(double value, Exclude exclude, bool isStart)
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

        private Message GetSortedSetAddMessage(RedisKey key, RedisValue member, double score, When when, CommandFlags flags)
        {
            WhenAlwaysOrExistsOrNotExists(when);
            switch (when)
            {
                case When.Always: return Message.Create(Database, flags, RedisCommand.ZADD, key, score, member);
                case When.NotExists: return Message.Create(Database, flags, RedisCommand.ZADD, key, RedisLiterals.NX, score, member);
                case When.Exists: return Message.Create(Database, flags, RedisCommand.ZADD, key, RedisLiterals.XX, score, member);
                default: throw new ArgumentOutOfRangeException(nameof(when));
            }
        }

        private Message GetSortedSetAddMessage(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags)
        {
            WhenAlwaysOrExistsOrNotExists(when);
            if (values == null) throw new ArgumentNullException(nameof(values));
            switch (values.Length)
            {
                case 0: return null;
                case 1:
                    return GetSortedSetAddMessage(key, values[0].element, values[0].score, when, flags);
                default:
                    RedisValue[] arr;
                    int index = 0;
                    switch (when)
                    {
                        case When.Always:
                            arr = new RedisValue[values.Length * 2];
                            break;
                        case When.NotExists:
                            arr = new RedisValue[(values.Length * 2) + 1];
                            arr[index++] = RedisLiterals.NX;
                            break;
                        case When.Exists:
                            arr = new RedisValue[(values.Length * 2) + 1];
                            arr[index++] = RedisLiterals.XX;
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(when));
                    }
                    for (int i = 0; i < values.Length; i++)
                    {
                        arr[index++] = values[i].score;
                        arr[index++] = values[i].element;
                    }
                    return Message.Create(Database, flags, RedisCommand.ZADD, key, arr);
            }
        }

        private Message GetSortedSetAddMessage(RedisKey destination, RedisKey key, long skip, long take, Order order, SortType sortType, RedisValue by, RedisValue[] get, CommandFlags flags)
        {
            // most common cases; no "get", no "by", no "destination", no "skip", no "take"
            if (destination.IsNull && skip == 0 && take == -1 && by.IsNull && (get == null || get.Length == 0))
            {
                switch (order)
                {
                    case Order.Ascending:
                        switch (sortType)
                        {
                            case SortType.Numeric: return Message.Create(Database, flags, RedisCommand.SORT, key);
                            case SortType.Alphabetic: return Message.Create(Database, flags, RedisCommand.SORT, key, RedisLiterals.ALPHA);
                        }
                        break;
                    case Order.Descending:
                        switch (sortType)
                        {
                            case SortType.Numeric: return Message.Create(Database, flags, RedisCommand.SORT, key, RedisLiterals.DESC);
                            case SortType.Alphabetic: return Message.Create(Database, flags, RedisCommand.SORT, key, RedisLiterals.DESC, RedisLiterals.ALPHA);
                        }
                        break;
                }
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
            if (destination.IsNull) return Message.Create(Database, flags, RedisCommand.SORT, key, values.ToArray());

            // because we are using STORE, we need to push this to a master
            if (Message.GetMasterSlaveFlags(flags) == CommandFlags.DemandSlave)
            {
                throw ExceptionFactory.MasterOnly(multiplexer.IncludeDetailInExceptions, RedisCommand.SORT, null, null);
            }
            flags = Message.SetMasterSlaveFlags(flags, CommandFlags.DemandMaster);
            values.Add(RedisLiterals.STORE);
            return Message.Create(Database, flags, RedisCommand.SORT, key, values.ToArray(), destination);
        }

        private Message GetSortedSetCombineAndStoreCommandMessage(SetOperation operation, RedisKey destination, RedisKey[] keys, double[] weights, Aggregate aggregate, CommandFlags flags)
        {
            RedisCommand command;
            switch (operation)
            {
                case SetOperation.Intersect: command = RedisCommand.ZINTERSTORE; break;
                case SetOperation.Union: command = RedisCommand.ZUNIONSTORE; break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            List<RedisValue> values = null;
            if (weights != null && weights.Length != 0)
            {
                (values ?? (values = new List<RedisValue>())).Add(RedisLiterals.WEIGHTS);
                foreach (var weight in weights)
                    values.Add(weight);
            }
            switch (aggregate)
            {
                case Aggregate.Sum: break; // default
                case Aggregate.Min:
                    (values ?? (values = new List<RedisValue>())).Add(RedisLiterals.AGGREGATE);
                    values.Add(RedisLiterals.MIN);
                    break;
                case Aggregate.Max:
                    (values ?? (values = new List<RedisValue>())).Add(RedisLiterals.AGGREGATE);
                    values.Add(RedisLiterals.MAX);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(aggregate));
            }
            return new SortedSetCombineAndStoreCommandMessage(Database, flags, command, destination, keys, values?.ToArray() ?? RedisValue.EmptyArray);
        }

        private Message GetSortedSetLengthMessage(RedisKey key, double min, double max, Exclude exclude, CommandFlags flags)
        {
            if (double.IsNegativeInfinity(min) && double.IsPositiveInfinity(max))
                return Message.Create(Database, flags, RedisCommand.ZCARD, key);

            var from = GetRange(min, exclude, true);
            var to = GetRange(max, exclude, false);
            return Message.Create(Database, flags, RedisCommand.ZCOUNT, key, from, to);
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
                key.AsRedisValue(),
                groupName,
                messageId
            };

            return Message.Create(Database, flags, RedisCommand.XACK, values);
        }

        private Message GetStreamAcknowledgeMessage(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags)
        {
            if (messageIds == null) throw new ArgumentNullException(nameof(messageIds));
            if (messageIds.Length == 0) throw new ArgumentOutOfRangeException(nameof(messageIds), "messageIds must contain at least one item.");

            var values = new RedisValue[messageIds.Length + 2];

            var offset = 0;

            values[offset++] = key.AsRedisValue();
            values[offset++] = groupName;

            for (var i = 0; i < messageIds.Length; i++)
            {
                values[offset++] = messageIds[i];
            }

            return Message.Create(Database, flags, RedisCommand.XACK, values);
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

        private Message GetStreamAddMessage(RedisKey key, RedisValue entryId, int? maxLength, bool useApproximateMaxLength, NameValueEntry[] streamPairs, CommandFlags flags)
        {
            // See https://redis.io/commands/xadd.

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

        private Message GetStreamClaimMessage(RedisKey key, RedisValue consumerGroup, RedisValue assignToConsumer, long minIdleTimeInMs, RedisValue[] messageIds, bool returnJustIds, CommandFlags flags)
        {
            if (messageIds == null) throw new ArgumentNullException(nameof(messageIds));
            if (messageIds.Length == 0) throw new ArgumentOutOfRangeException(nameof(messageIds), "messageIds must contain at least one item.");

            // XCLAIM <key> <group> <consumer> <min-idle-time> <ID-1> <ID-2> ... <ID-N>
            var values = new RedisValue[4 + messageIds.Length + (returnJustIds ? 1 : 0)];

            var offset = 0;

            values[offset++] = key.AsRedisValue();
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

            return Message.Create(Database, flags, RedisCommand.XCLAIM, values);
        }

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

            // See https://redis.io/topics/streams-intro.

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");
            }

            var values = new RedisValue[consumerName == RedisValue.Null ? 5 : 6];

            values[0] = key.AsRedisValue();
            values[1] = groupName;
            values[2] = minId ?? StreamConstants.ReadMinValue;
            values[3] = maxId ?? StreamConstants.ReadMaxValue;
            values[4] = count;

            if (consumerName != RedisValue.Null)
            {
                values[5] = consumerName;
            }

            return Message.Create(Database,
                flags,
                RedisCommand.XPENDING,
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

        private Message GetStreamReadGroupMessage(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue afterId, int? count, CommandFlags flags)
        {
            // Example: > XREADGROUP GROUP mygroup Alice COUNT 1 STREAMS mystream >
            if (count.HasValue && count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");
            }

            var totalValueCount = 6 + (count.HasValue ? 2 : 0);
            var values = new RedisValue[totalValueCount];

            var offset = 0;

            values[offset++] = StreamConstants.Group;
            values[offset++] = groupName;
            values[offset++] = consumerName;

            if (count.HasValue)
            {
                values[offset++] = StreamConstants.Count;
                values[offset++] = count.Value;
            }

            values[offset++] = StreamConstants.Streams;
            values[offset++] = key.AsRedisValue();
            values[offset] = afterId;

            return Message.Create(Database,
                flags,
                RedisCommand.XREADGROUP,
                values);
        }

        private Message GetSingleStreamReadMessage(RedisKey key, RedisValue afterId, int? count, CommandFlags flags)
        {
            if (count.HasValue && count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");
            }

            var values = new RedisValue[3 + (count.HasValue ? 2 : 0)];
            var offset = 0;

            if (count.HasValue)
            {
                values[offset++] = StreamConstants.Count;
                values[offset++] = count.Value;
            }

            values[offset++] = StreamConstants.Streams;
            values[offset++] = key.AsRedisValue();
            values[offset] = afterId;

            // Example: > XREAD COUNT 2 STREAMS writers 1526999352406-0
            return Message.Create(Database,
                flags,
                RedisCommand.XREAD,
                values);
        }

        private Message GetStreamTrimMessage(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags)
        {
            if (maxLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength), "maxLength must be greater than 0.");
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

        private Message GetStringBitOperationMessage(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags)
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

        private Message GetStringGetWithExpiryMessage(RedisKey key, CommandFlags flags, out ResultProcessor<RedisValueWithExpiry> processor, out ServerEndPoint server)
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

        private Message GetStringSetMessage(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            switch (values.Length)
            {
                case 0: return null;
                case 1: return GetStringSetMessage(values[0].Key, values[0].Value, null, when, flags);
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

        private Message GetStringSetMessage(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExistsOrNotExists(when);
            if (value.IsNull) return Message.Create(Database, flags, RedisCommand.DEL, key);

            if (expiry == null || expiry.Value == TimeSpan.MaxValue)
            { // no expiry
                switch (when)
                {
                    case When.Always: return Message.Create(Database, flags, RedisCommand.SET, key, value);
                    case When.NotExists: return Message.Create(Database, flags, RedisCommand.SETNX, key, value);
                    case When.Exists: return Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.XX);
                }
            }
            long milliseconds = expiry.Value.Ticks / TimeSpan.TicksPerMillisecond;

            if ((milliseconds % 1000) == 0)
            {
                // a nice round number of seconds
                long seconds = milliseconds / 1000;
                switch (when)
                {
                    case When.Always: return Message.Create(Database, flags, RedisCommand.SETEX, key, seconds, value);
                    case When.Exists: return Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.XX);
                    case When.NotExists: return Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.NX);
                }
            }

            switch (when)
            {
                case When.Always: return Message.Create(Database, flags, RedisCommand.PSETEX, key, milliseconds, value);
                case When.Exists: return Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.XX);
                case When.NotExists: return Message.Create(Database, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.NX);
            }
            throw new NotSupportedException();
        }

        private Message IncrMessage(RedisKey key, long value, CommandFlags flags)
        {
            switch (value)
            {
                case 0:
                    if ((flags & CommandFlags.FireAndForget) != 0) return null;
                    return Message.Create(Database, flags, RedisCommand.INCRBY, key, value);
                case 1:
                    return Message.Create(Database, flags, RedisCommand.INCR, key);
                case -1:
                    return Message.Create(Database, flags, RedisCommand.DECR, key);
                default:
                    return value > 0
                        ? Message.Create(Database, flags, RedisCommand.INCRBY, key, value)
                        : Message.Create(Database, flags, RedisCommand.DECRBY, key, -value);
            }
        }

        private RedisCommand SetOperationCommand(SetOperation operation, bool store)
        {
            switch (operation)
            {
                case SetOperation.Difference: return store ? RedisCommand.SDIFFSTORE : RedisCommand.SDIFF;
                case SetOperation.Intersect: return store ? RedisCommand.SINTERSTORE : RedisCommand.SINTER;
                case SetOperation.Union: return store ? RedisCommand.SUNIONSTORE : RedisCommand.SUNION;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private IEnumerable<T> TryScan<T>(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags, RedisCommand command, ResultProcessor<ScanIterator<T>.ScanResult> processor)
        {
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (!multiplexer.CommandMap.IsAvailable(command)) return null;

            var features = GetFeatures(key, flags, out ServerEndPoint server);
            if (!features.Scan) return null;

            if (CursorUtils.IsNil(pattern)) pattern = (byte[])null;
            return new ScanIterator<T>(this, server, key, pattern, pageSize, cursor, pageOffset, flags, command, processor);
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
            bool reverseLimits = (order == Order.Ascending) == start.CompareTo(stop) > 0;
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
        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min = default(RedisValue), RedisValue max = default(RedisValue),
            Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            ReverseLimits(order, ref exclude, ref min, ref max);
            var msg = GetLexMessage(order == Order.Ascending ? RedisCommand.ZRANGEBYLEX : RedisCommand.ZREVRANGEBYLEX, key, min, max, exclude, skip, take, flags);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
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

        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min = default(RedisValue), RedisValue max = default(RedisValue),
            Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            ReverseLimits(order, ref exclude, ref min, ref max);
            var msg = GetLexMessage(order == Order.Ascending ? RedisCommand.ZRANGEBYLEX : RedisCommand.ZREVRANGEBYLEX, key, min, max, exclude, skip, take, flags);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<long> SortedSetRemoveRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetLexMessage(RedisCommand.ZREMRANGEBYLEX, key, min, max, exclude, 0, -1, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        internal class ScanIterator<T> : CursorEnumerable<T>
        {
            private readonly RedisKey key;
            private readonly RedisValue pattern;
            private readonly RedisCommand command;

            public ScanIterator(RedisDatabase database, ServerEndPoint server, RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags,
                RedisCommand command, ResultProcessor<ScanResult> processor)
                : base(database, server, database.Database, pageSize, cursor, pageOffset, flags)
            {
                this.key = key;
                this.pattern = pattern;
                this.command = command;
                Processor = processor;
            }

            protected override ResultProcessor<CursorEnumerable<T>.ScanResult> Processor { get; }

            protected override Message CreateMessage(long cursor)
            {
                if (CursorUtils.IsNil(pattern))
                {
                    if (pageSize == CursorUtils.DefaultPageSize)
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
                    if (pageSize == CursorUtils.DefaultPageSize)
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
            public static readonly ResultProcessor<ScanIterator<HashEntry>.ScanResult> Default = new HashScanResultProcessor();
            private HashScanResultProcessor() { }
            protected override HashEntry[] Parse(in RawResult result)
            {
                if (!HashEntryArray.TryParse(result, out HashEntry[] pairs)) pairs = null;
                return pairs;
            }
        }

        private abstract class ScanResultProcessor<T> : ResultProcessor<ScanIterator<T>.ScanResult>
        {
            protected abstract T[] Parse(in RawResult result);

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
                                var sscanResult = new ScanIterator<T>.ScanResult(i64, Parse(inner));
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

            public ExecuteMessage(CommandMap map, int db, CommandFlags flags, string command, ICollection<object> args) : base(db, flags, RedisCommand.UNKNOWN)
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
            private readonly string script;
            private readonly RedisValue[] values;
            private byte[] asciiHash;
            private readonly byte[] hexHash;

            public ScriptEvalMessage(int db, CommandFlags flags, string script, RedisKey[] keys, RedisValue[] values)
                : this(db, flags, ResultProcessor.ScriptLoadProcessor.IsSHA1(script) ? RedisCommand.EVALSHA : RedisCommand.EVAL, script, null, keys, values)
            {
                if (script == null) throw new ArgumentNullException(nameof(script));
            }

            public ScriptEvalMessage(int db, CommandFlags flags, byte[] hash, RedisKey[] keys, RedisValue[] values)
                : this(db, flags, RedisCommand.EVAL, null, hash, keys, values)
            {
                if (hash == null) throw new ArgumentNullException(nameof(hash));
                if (hash.Length != ResultProcessor.ScriptLoadProcessor.Sha1HashLength) throw new ArgumentOutOfRangeException(nameof(hash), "Invalid hash length");
            }

            private ScriptEvalMessage(int db, CommandFlags flags, RedisCommand command, string script, byte[] hexHash, RedisKey[] keys, RedisValue[] values)
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
                PhysicalBridge bridge;
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
            public static readonly ResultProcessor<ScanIterator<RedisValue>.ScanResult> Default = new SetScanResultProcessor();
            private SetScanResultProcessor() { }
            protected override RedisValue[] Parse(in RawResult result)
            {
                return result.GetItemsAsValues();
            }
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
            public static readonly ResultProcessor<ScanIterator<SortedSetEntry>.ScanResult> Default = new SortedSetScanResultProcessor();
            private SortedSetScanResultProcessor() { }
            protected override SortedSetEntry[] Parse(in RawResult result)
            {
                if (!SortedSetWithScores.TryParse(result, out SortedSetEntry[] pairs)) pairs = null;
                return pairs;
            }
        }

        private class StringGetWithExpiryMessage : Message.CommandKeyBase, IMultiMessage
        {
            private readonly RedisCommand ttlCommand;
            private IResultBox<TimeSpan?> box;

            public StringGetWithExpiryMessage(int db, CommandFlags flags, RedisCommand ttlCommand, in RedisKey key)
                : base(db, flags, RedisCommand.GET, key)
            {
                this.ttlCommand = ttlCommand;
            }

            public override string CommandAndKey => ttlCommand + "+" + RedisCommand.GET + " " + (string)Key;

            public IEnumerable<Message> GetMessages(PhysicalConnection connection)
            {
                box = SimpleResultBox<TimeSpan?>.Create();
                var ttl = Message.Create(Db, Flags, ttlCommand, Key);
                var proc = ttlCommand == RedisCommand.PTTL ? ResultProcessor.TimeSpanFromMilliseconds : ResultProcessor.TimeSpanFromSeconds;
                ttl.SetSource(proc, box);
                yield return ttl;
                yield return this;
            }

            public bool UnwrapValue(out TimeSpan? value, out Exception ex)
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
