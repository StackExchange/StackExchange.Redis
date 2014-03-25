using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal class RedisDatabase : RedisBase, IDatabase
    {
        private readonly int Db;

        internal RedisDatabase(ConnectionMultiplexer multiplexer, int db, object asyncState) : base(multiplexer, asyncState)
        {
            this.Db = db;
        }

        public object AsyncState { get { return asyncState; } }

        public int Database { get { return Db; } }

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

        public RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.DEBUG, RedisLiterals.OBJECT, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> DebugObjectAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.DEBUG, RedisLiterals.OBJECT, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
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
            var msg = Message.Create(Db, flags, RedisCommand.HDEL, key, hashField);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException("hashFields");
            var msg = hashFields.Length == 0 ? null : Message.Create(Db, flags, RedisCommand.HDEL, key, hashFields);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HDEL, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException("hashFields");

            var msg = hashFields.Length == 0 ? null : Message.Create(Db, flags, RedisCommand.HDEL, key, hashFields);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public bool HashExists(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HEXISTS, key, hashField);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HEXISTS, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue HashGet(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HGET, key, hashField);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException("hashFields");
            if (hashFields.Length == 0) return new RedisValue[0];
            var msg = Message.Create(Db, flags, RedisCommand.HMGET, key, hashFields);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public KeyValuePair<RedisValue, RedisValue>[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HGETALL, key);
            return ExecuteSync(msg, ResultProcessor.ValuePairInterleaved);
        }

        public Task<KeyValuePair<RedisValue, RedisValue>[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HGETALL, key);
            return ExecuteAsync(msg, ResultProcessor.ValuePairInterleaved);
        }

        public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HGET, key, hashField);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue[]> HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            if (hashFields == null) throw new ArgumentNullException("hashFields");
            if (hashFields.Length == 0) return CompletedTask<RedisValue[]>.FromResult(new RedisValue[0], asyncState);
            var msg = Message.Create(Db, flags, RedisCommand.HMGET, key, hashFields);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public long HashIncrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Db, flags, RedisCommand.HINCRBY, key, hashField, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public double HashIncrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Db, flags, RedisCommand.HINCRBYFLOAT, key, hashField, value);
            return ExecuteSync(msg, ResultProcessor.Double);
        }

        public Task<long> HashIncrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Db, flags, RedisCommand.HINCRBY, key, hashField, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<double> HashIncrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = value == 0 && (flags & CommandFlags.FireAndForget) != 0
                ? null : Message.Create(Db, flags, RedisCommand.HINCRBYFLOAT, key, hashField, value);
            return ExecuteAsync(msg, ResultProcessor.Double);
        }

        public RedisValue[] HashKeys(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HKEYS, key);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HKEYS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HLEN, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HLEN, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public bool HashSet(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrNotExists(when);
            var msg = value.IsNull
                ? Message.Create(Db, flags, RedisCommand.HDEL, key, hashField)
                : Message.Create(Db, flags, when == When.Always ? RedisCommand.HSET : RedisCommand.HSETNX, key, hashField, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public void HashSet(RedisKey key, KeyValuePair<RedisValue, RedisValue>[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetHashSetMessage(key, hashFields, flags);
            if (msg == null) return;
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task<bool> HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrNotExists(when);
            var msg = value.IsNull
                ? Message.Create(Db, flags, RedisCommand.HDEL, key, hashField)
                : Message.Create(Db, flags, when == When.Always ? RedisCommand.HSET : RedisCommand.HSETNX, key, hashField, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task HashSetAsync(RedisKey key, KeyValuePair<RedisValue, RedisValue>[] hashFields, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetHashSetMessage(key, hashFields, flags);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public Task<bool> HashSetIfNotExistsAsync(RedisKey key, RedisValue hashField, RedisValue value, CommandFlags flags)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HSETNX, key, hashField, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue[] HashValues(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HVALS, key);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.HVALS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public EndPoint IdentifyEndpoint(RedisKey key = default(RedisKey), CommandFlags flags = CommandFlags.None)
        {
            var msg = key.IsNull ? Message.Create(Db, flags, RedisCommand.PING) : Message.Create(Db, flags, RedisCommand.EXISTS, key);
            return ExecuteSync(msg, ResultProcessor.ConnectionIdentity);
        }

        public Task<EndPoint> IdentifyEndpointAsync(RedisKey key = default(RedisKey), CommandFlags flags = CommandFlags.None)
        {
            var msg = key.IsNull ? Message.Create(Db, flags, RedisCommand.PING) : Message.Create(Db, flags, RedisCommand.EXISTS, key);
            return ExecuteAsync(msg, ResultProcessor.ConnectionIdentity);
        }

        public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var server = multiplexer.SelectServer(Db, RedisCommand.PING, flags, key);
            return server != null && server.IsConnected;
        }
        public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.DEL, key);
            return ExecuteSync(msg, ResultProcessor.DemandZeroOrOne);
        }

        public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException("keys");
            var msg = keys.Length == 0 ? null : Message.Create(Db, flags, RedisCommand.DEL, keys);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.DEL, key);
            return ExecuteAsync(msg, ResultProcessor.DemandZeroOrOne);
        }

        public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys == null) throw new ArgumentNullException("keys");

            var msg = keys.Length == 0 ? null : Message.Create(Db, flags, RedisCommand.DEL, keys);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public byte[] KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.DUMP, key);
            return ExecuteSync(msg, ResultProcessor.ByteArray);
        }

        public Task<byte[]> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.DUMP, key);
            return ExecuteAsync(msg, ResultProcessor.ByteArray);
        }

        public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.EXISTS, key);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.EXISTS, key);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        {
            ServerEndPoint server;
            var msg = GetExpiryMessage(key, flags, expiry, out server);
            return ExecuteSync(msg, ResultProcessor.Boolean, server: server);
        }

        public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags = CommandFlags.None)
        {
            ServerEndPoint server;
            var msg = GetExpiryMessage(key, flags, expiry, out server);
            return ExecuteSync(msg, ResultProcessor.Boolean, server: server);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
        {
            ServerEndPoint server;
            var msg = GetExpiryMessage(key, flags, expiry, out server);
            return ExecuteAsync(msg, ResultProcessor.Boolean, server: server);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags = CommandFlags.None)
        {
            ServerEndPoint server;
            var msg = GetExpiryMessage(key, flags, expiry, out server);
            return ExecuteAsync(msg, ResultProcessor.Boolean, server: server);
        }

        public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.MOVE, key, database);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.MOVE, key, database);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.PERSIST, key);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.PERSIST, key);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrNotExists(when);
            var msg = Message.Create(Db, flags, when == When.Always ? RedisCommand.RENAME : RedisCommand.RENAMENX, key, newKey);
            return ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task<bool> KeyRenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrNotExists(when);
            var msg = Message.Create(Db, flags, when == When.Always ? RedisCommand.RENAME : RedisCommand.RENAMENX, key, newKey);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
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
            ServerEndPoint server;
            var features = GetFeatures(Db, key, flags, out server);
            Message msg;
            if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PTTL))
            {
                msg = Message.Create(Db, flags, RedisCommand.PTTL, key);
                return ExecuteSync(msg, ResultProcessor.TimeSpanFromMilliseconds);
            }
            msg = Message.Create(Db, flags, RedisCommand.TTL, key);
            return ExecuteSync(msg, ResultProcessor.TimeSpanFromSeconds);
        }

        public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            ServerEndPoint server;
            var features = GetFeatures(Db, key, flags, out server);
            Message msg;
            if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PTTL))
            {
                msg = Message.Create(Db, flags, RedisCommand.PTTL, key);
                return ExecuteAsync(msg, ResultProcessor.TimeSpanFromMilliseconds);
            }
            msg = Message.Create(Db, flags, RedisCommand.TTL, key);
            return ExecuteAsync(msg, ResultProcessor.TimeSpanFromSeconds);

        }

        public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.TYPE, key);
            return ExecuteSync(msg, ResultProcessor.RedisType);
        }

        public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags)
        {
            var msg = Message.Create(Db, flags, RedisCommand.TYPE, key);
            return ExecuteAsync(msg, ResultProcessor.RedisType);
        }

        public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LINDEX, key, index);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LINDEX, key, index);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public long ListInsertAfter(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LINSERT, key, RedisLiterals.AFTER, pivot, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListInsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LINSERT, key, RedisLiterals.AFTER, pivot, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long ListInsertBefore(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LINSERT, key, RedisLiterals.BEFORE, pivot, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListInsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LINSERT, key, RedisLiterals.BEFORE, pivot, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LPOP, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> ListLeftPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LPOP, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Db, flags, when == When.Always ? RedisCommand.LPUSH : RedisCommand.LPUSHX, key, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException("values");
            var msg = values.Length == 0 ? Message.Create(Db, flags, RedisCommand.LLEN, key) : Message.Create(Db, flags, RedisCommand.LPUSH, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Db, flags, when == When.Always ? RedisCommand.LPUSH : RedisCommand.LPUSHX, key, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException("values");
            var msg = values.Length == 0 ? Message.Create(Db, flags, RedisCommand.LLEN, key) : Message.Create(Db, flags, RedisCommand.LPUSH, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LLEN, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LLEN, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LRANGE, key, start, stop);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> ListRangeAsync(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LRANGE, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public long ListRemove(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LREM, key, count, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListRemoveAsync(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LREM, key, count, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.RPOP, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> ListRightPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.RPOP, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue ListRightPopLeftPush(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.RPOPLPUSH, source, destination);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> ListRightPopLeftPushAsync(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.RPOPLPUSH, source, destination);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public long ListRightPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Db, flags, when == When.Always ? RedisCommand.RPUSH : RedisCommand.RPUSHX, key, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException("values");
            var msg = values.Length == 0 ? Message.Create(Db, flags, RedisCommand.LLEN, key) : Message.Create(Db, flags, RedisCommand.RPUSH, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListRightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExists(when);
            var msg = Message.Create(Db, flags, when == When.Always ? RedisCommand.RPUSH : RedisCommand.RPUSHX, key, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException("values");
            var msg = values.Length == 0 ? Message.Create(Db, flags, RedisCommand.LLEN, key) : Message.Create(Db, flags, RedisCommand.RPUSH, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public void ListSetByIndex(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LSET, key, index, value);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ListSetByIndexAsync(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LSET, key, index, value);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public void ListTrim(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LTRIM, key, start, stop);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.LTRIM, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            var tran = GetLockExtendTransaction(key, value, expiry);
            return tran.Execute(flags);
        }

        public Task<bool> LockExtendAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            var tran = GetLockExtendTransaction(key, value, expiry);
            return tran.ExecuteAsync(flags);
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
            var tran = GetLockReleaseTransaction(key, value);
            return tran.Execute(flags);
        }

        public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var tran = GetLockReleaseTransaction(key, value);
            return tran.ExecuteAsync(flags);
        }

        public bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            return StringSet(key, value, expiry, When.NotExists, flags);
        }

        public Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        {
            return StringSetAsync(key, value, expiry, When.NotExists, flags);
        }

        public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.RANDOMKEY);
            return ExecuteSync(msg, ResultProcessor.RedisKey);
        }

        public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.RANDOMKEY);
            return ExecuteAsync(msg, ResultProcessor.RedisKey);
        }

        public RedisResult ScriptEvaluate(string script, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Db, flags, RedisCommand.EVAL, script, keys ?? RedisKey.EmptyArray, values ?? RedisValue.EmptyArray);
            return ExecuteSync(msg, ResultProcessor.ScriptResult);
        }

        public Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = new ScriptEvalMessage(Db, flags, RedisCommand.EVAL, script, keys ?? RedisKey.EmptyArray, values ?? RedisValue.EmptyArray);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult);
        }

        public bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SADD, key, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SADD, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SADD, key, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SADD, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue[] SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, SetOperationCommand(operation, false), first, second);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, SetOperationCommand(operation, false), keys);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, SetOperationCommand(operation, true), destination, first, second);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, SetOperationCommand(operation, true), destination, keys);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, SetOperationCommand(operation, true), destination, first, second);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, SetOperationCommand(operation, true), destination, keys);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, SetOperationCommand(operation, false), first, second);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, SetOperationCommand(operation, false), keys);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SISMEMBER, key, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SISMEMBER, key, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }


        public long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SCARD, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SCARD, key);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SMEMBERS, key);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SMEMBERS, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public bool SetMove(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SMOVE, source, destination, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> SetMoveAsync(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SMOVE, source, destination, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SPOP, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SPOP, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SRANDMEMBER, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SRANDMEMBER, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SRANDMEMBER, key, count);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SRANDMEMBER, key, count);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SREM, key, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SREM, key, values);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SREM, key, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SREM, key, values);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default(RedisValue), int pageSize = RedisDatabase.SetScanIterator.DefaultPageSize, CommandFlags flags = CommandFlags.None)
        {
            if (pageSize <= 0) throw new ArgumentOutOfRangeException("pageSize");
            if (SetScanIterator.IsNil(pattern)) pattern = (byte[])null;

            multiplexer.CommandMap.AssertAvailable(RedisCommand.SCAN);
            ServerEndPoint server;
            var features = GetFeatures(Db, key, flags, out server);
            if (!features.Scan)
            {
                if (pattern.IsNull)
                {
                    return SetMembers(key, flags);
                }
            }
            return new SetScanIterator(this, server, key, pattern, pageSize, flags).Read();
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

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZADD, key, score, member);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SortedSetAdd(RedisKey key, KeyValuePair<RedisValue, double>[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, values, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZADD, key, score, member);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SortedSetAddAsync(RedisKey key, KeyValuePair<RedisValue, double>[] values, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetAddMessage(key, values, flags);
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
            var msg = Message.Create(Db, flags, RedisCommand.ZINCRBY, key, value, member);
            return ExecuteSync(msg, ResultProcessor.Double);
        }

        public Task<double> SortedSetIncrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZINCRBY, key, value, member);
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
            var msg = Message.Create(Db, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue[]> SortedSetRangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public KeyValuePair<RedisValue, double>[] SortedSetRangeByRankWithScores(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop, RedisLiterals.WITHSCORES);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores);
        }

        public Task<KeyValuePair<RedisValue, double>[]> SortedSetRangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE, key, start, stop, RedisLiterals.WITHSCORES);
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

        public KeyValuePair<RedisValue, double>[] SortedSetRangeByScoreWithScores(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, true);
            return ExecuteSync(msg, ResultProcessor.SortedSetWithScores);
        }

        public Task<KeyValuePair<RedisValue, double>[]> SortedSetRangeByScoreWithScoresAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSortedSetRangeByScoreMessage(key, start, stop, exclude, order, skip, take, flags, true);
            return ExecuteAsync(msg, ResultProcessor.SortedSetWithScores);
        }

        public long? SortedSetRank(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, order == Order.Descending ? RedisCommand.ZREVRANK : RedisCommand.ZRANK, key, member);
            return ExecuteSync(msg, ResultProcessor.NullableInt64);
        }

        public Task<long?> SortedSetRankAsync(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, order == Order.Descending ? RedisCommand.ZREVRANK : RedisCommand.ZRANK, key, member);
            return ExecuteAsync(msg, ResultProcessor.NullableInt64);
        }

        public bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZREM, key, member);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZREM, key, members);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<bool> SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZREM, key, member);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<long> SortedSetRemoveAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZREM, key, members);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long SortedSetRemoveRangeByRank(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZREMRANGEBYRANK, key, start, stop);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SortedSetRemoveRangeByRankAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZREMRANGEBYRANK, key, start, stop);
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

        public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZSCORE, key, member);
            return ExecuteSync(msg, ResultProcessor.NullableDouble);
        }

        public Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.ZSCORE, key, member);
            return ExecuteAsync(msg, ResultProcessor.NullableDouble);
        }

        public long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.APPEND, key, value);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.APPEND, key, value);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long StringBitCount(RedisKey key, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.BITCOUNT, key, start, end);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringBitCountAsync(RedisKey key, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.BITCOUNT, key, start, end);
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

        public long StringBitPosition(RedisKey key, bool value, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.BITPOS, key, value, start, end);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringBitPositionAsync(RedisKey key, bool value, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.BITPOS, key, value, start, end);
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
            var msg = Message.Create(Db, flags, RedisCommand.GET, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.MGET, keys);
            return ExecuteSync(msg, ResultProcessor.RedisValueArray);
        }

        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.GET, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.MGET, keys);
            return ExecuteAsync(msg, ResultProcessor.RedisValueArray);
        }

        public bool StringGetBit(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.GETBIT, key, offset);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StringGetBitAsync(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.GETBIT, key, offset);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.GETRANGE, key, start, end);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.GETRANGE, key, start, end);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.GETSET, key, value);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.GETSET, key, value);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            ServerEndPoint server;
            ResultProcessor<RedisValueWithExpiry> processor;
            var msg = GetStringGetWithExpiryMessage(key, flags, out processor, out server);
            return ExecuteSync(msg, processor, server);
        }

        public Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            ServerEndPoint server;
            ResultProcessor<RedisValueWithExpiry> processor;
            var msg = GetStringGetWithExpiryMessage(key, flags, out processor, out server);
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
                ? null : Message.Create(Db, flags, RedisCommand.INCRBYFLOAT, key, value);
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
                ? null : Message.Create(Db, flags, RedisCommand.INCRBYFLOAT, key, value);
            return ExecuteAsync(msg, ResultProcessor.Double);
        }

        public long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.STRLEN, key);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.STRLEN, key);
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

        public bool StringSetBit(RedisKey key, long offset, bool value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SETBIT, key, offset, value);
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SETBIT, key, offset, value);
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public RedisValue StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SETRANGE, key, offset, value);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(Db, flags, RedisCommand.SETRANGE, key, offset, value);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        Message GetExpiryMessage(RedisKey key, CommandFlags flags, TimeSpan? expiry, out ServerEndPoint server)
        {
            TimeSpan duration;
            if (expiry == null || (duration = expiry.Value) == TimeSpan.MaxValue)
            {
                server = null;
                return Message.Create(Db, flags, RedisCommand.PERSIST, key);
            }
            long milliseconds = duration.Ticks / TimeSpan.TicksPerMillisecond;
            if ((milliseconds % 1000) != 0)
            {
                var features = GetFeatures(Db, key, flags, out server);
                if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PEXPIRE))
                {
                    return Message.Create(Db, flags, RedisCommand.PEXPIRE, key, milliseconds);
                }
            }
            server = null;
            long seconds = milliseconds / 1000;
            return Message.Create(Db, flags, RedisCommand.EXPIRE, key, seconds);
        }

        Message GetExpiryMessage(RedisKey key, CommandFlags flags, DateTime? expiry, out ServerEndPoint server)
        {
            DateTime when;
            if (expiry == null || (when = expiry.Value) == DateTime.MaxValue)
            {
                server = null;
                return Message.Create(Db, flags, RedisCommand.PERSIST, key);
            }
            switch (when.Kind)
            {
                case DateTimeKind.Local:
                case DateTimeKind.Utc:
                    break; // fine, we can work with that
                default:
                    throw new ArgumentException("Expiry time must be either Utc or Local", "expiry");
            }
            long milliseconds = (when - RedisBase.UnixEpoch).Ticks / TimeSpan.TicksPerMillisecond;

            if ((milliseconds % 1000) != 0)
            {
                var features = GetFeatures(Db, key, flags, out server);
                if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PEXPIREAT))
                {
                    return Message.Create(Db, flags, RedisCommand.PEXPIREAT, key, milliseconds);
                }
            }
            server = null;
            long seconds = milliseconds / 1000;
            return Message.Create(Db, flags, RedisCommand.EXPIREAT, key, seconds);
        }

        private Message GetHashSetMessage(RedisKey key, KeyValuePair<RedisValue, RedisValue>[] hashFields, CommandFlags flags)
        {
            if (hashFields == null) throw new ArgumentNullException("hashFields");
            switch (hashFields.Length)
            {
                case 0: return null;
                case 1: return Message.Create(Db, flags, RedisCommand.HMSET, key, hashFields[0].Key, hashFields[0].Value);
                default:
                    var arr = new RedisValue[hashFields.Length * 2];
                    int offset = 0;
                    for (int i = 0; i < hashFields.Length; i++)
                    {
                        arr[offset++] = hashFields[i].Key;
                        arr[offset++] = hashFields[i].Value;
                    }
                    return Message.Create(Db, flags, RedisCommand.HMSET, key, arr);
            }
        }

        ITransaction GetLockExtendTransaction(RedisKey key, RedisValue value, TimeSpan expiry)
        {
            var tran = CreateTransaction(asyncState);
            tran.AddCondition(Condition.StringEqual(key, value));
            tran.KeyExpireAsync(key, expiry, CommandFlags.FireAndForget);
            return tran;
        }

        ITransaction GetLockReleaseTransaction(RedisKey key, RedisValue value)
        {
            var tran = CreateTransaction(asyncState);
            tran.AddCondition(Condition.StringEqual(key, value));
            tran.KeyDeleteAsync(key, CommandFlags.FireAndForget);
            return tran;
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

        Message GetRestoreMessage(RedisKey key, byte[] value, TimeSpan? expiry, CommandFlags flags)
        {
            long pttl = (expiry == null || expiry.Value == TimeSpan.MaxValue) ? 0 : (expiry.Value.Ticks / TimeSpan.TicksPerMillisecond);
            return Message.Create(Db, flags, RedisCommand.RESTORE, key, pttl, value);
        }

        private Message GetSortedSetAddMessage(RedisKey key, KeyValuePair<RedisValue, double>[] values, CommandFlags flags)
        {
            if (values == null) throw new ArgumentNullException("values");
            switch (values.Length)
            {
                case 0: return null;
                case 1:
                    return Message.Create(Db, flags, RedisCommand.ZADD, key,
                values[0].Value, values[0].Key);
                case 2:
                    return Message.Create(Db, flags, RedisCommand.ZADD, key,
                values[0].Value, values[0].Key,
                values[1].Value, values[1].Key);
                default:
                    var arr = new RedisValue[values.Length * 2];
                    int index = 0;
                    for (int i = 0; i < values.Length; i++)
                    {
                        arr[index++] = values[i].Value;
                        arr[index++] = values[i].Key;
                    }
                    return Message.Create(Db, flags, RedisCommand.ZADD, key, arr);
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
                            case SortType.Numeric: return Message.Create(Db, flags, RedisCommand.SORT, key);
                            case SortType.Alphabetic: return Message.Create(Db, flags, RedisCommand.SORT, key, RedisLiterals.ALPHA);
                        }
                        break;
                    case Order.Descending:
                        switch (sortType)
                        {
                            case SortType.Numeric: return Message.Create(Db, flags, RedisCommand.SORT, key, RedisLiterals.DESC);
                            case SortType.Alphabetic: return Message.Create(Db, flags, RedisCommand.SORT, key, RedisLiterals.DESC, RedisLiterals.ALPHA);
                        }
                        break;
                }
            }

            // and now: more complicated scenarios...
            List<RedisValue> values = new List<RedisValue>();
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
                    throw new ArgumentOutOfRangeException("order");
            }
            switch (sortType)
            {
                case SortType.Numeric:
                    break; // default
                case SortType.Alphabetic:
                    values.Add(RedisLiterals.ALPHA);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("sortType");
            }
            if (get != null && get.Length != 0)
            {
                foreach (var item in get)
                {
                    values.Add(RedisLiterals.GET);
                    values.Add(item);
                }
            }
            if (destination.IsNull) return Message.Create(Db, flags, RedisCommand.SORT, key, values.ToArray());

            // because we are using STORE, we need to push this to a master
            if (Message.GetMasterSlaveFlags(flags) == CommandFlags.DemandSlave)
            {
                throw ExceptionFactory.MasterOnly(multiplexer.IncludeDetailInExceptions, RedisCommand.SORT, null, null);
            }
            flags = Message.SetMasterSlaveFlags(flags, CommandFlags.DemandMaster);
            values.Add(RedisLiterals.STORE);
            return Message.Create(Db, flags, RedisCommand.SORT, key, values.ToArray(), destination);
        }

        private Message GetSortedSetCombineAndStoreCommandMessage(SetOperation operation, RedisKey destination, RedisKey[] keys, double[] weights, Aggregate aggregate, CommandFlags flags)
        {
            RedisCommand command;
            switch (operation)
            {
                case SetOperation.Intersect: command = RedisCommand.ZINTERSTORE; break;
                case SetOperation.Union: command = RedisCommand.ZUNIONSTORE; break;
                default: throw new ArgumentOutOfRangeException("operation");
            }
            if (keys == null) throw new ArgumentNullException("keys");

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
                    throw new ArgumentOutOfRangeException("aggregate");
            }
            return new SortedSetCombineAndStoreCommandMessage(Db, flags, command, destination, keys, values == null ? RedisValue.EmptyArray : values.ToArray());

        }

        private Message GetSortedSetLengthMessage(RedisKey key, double min, double max, Exclude exclude, CommandFlags flags)
        {
            if (double.IsNegativeInfinity(min) && double.IsPositiveInfinity(max))
                return Message.Create(Db, flags, RedisCommand.ZCARD, key);

            var from = GetRange(min, exclude, true);
            var to = GetRange(max, exclude, false);
            return Message.Create(Db, flags, RedisCommand.ZCOUNT, key, from, to);
        }
        private Message GetSortedSetRangeByScoreMessage(RedisKey key, double start, double stop, Exclude exclude, Order order, long skip, long take, CommandFlags flags, bool withScores)
        {
            // usage: {ZRANGEBYSCORE|ZREVRANGEBYSCORE} key from to [WITHSCORES] [LIMIT offset count]
            // there's basically only 4 layouts; with/without each of scores/limit
            var command = order == Order.Descending ? RedisCommand.ZREVRANGEBYSCORE : RedisCommand.ZRANGEBYSCORE;
            bool unlimited = skip == 0 && take == -1; // these are our defaults that mean "everything"; anything else needs to be sent explicitly
            RedisValue from = GetRange(start, exclude, true), to = GetRange(stop, exclude, false);
            if (withScores)
            {
                return unlimited ? Message.Create(Db, flags, command, key, from, to, RedisLiterals.WITHSCORES)
                    : Message.Create(Db, flags, command, key, new[] { from, to, RedisLiterals.WITHSCORES, RedisLiterals.LIMIT, skip, take });
            }
            else
            {
                return unlimited ? Message.Create(Db, flags, command, key, from, to)
                    : Message.Create(Db, flags, command, key, new[] { from, to, RedisLiterals.LIMIT, skip, take });
            }
        }

        private Message GetSortedSetRemoveRangeByScoreMessage(RedisKey key, double start, double stop, Exclude exclude, CommandFlags flags)
        {
            return Message.Create(Db, flags, RedisCommand.ZREMRANGEBYSCORE, key,
                    GetRange(start, exclude, true), GetRange(start, exclude, false));
        }

        private Message GetStringBitOperationMessage(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags)
        {
            if (keys == null) throw new ArgumentNullException("keys");
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
            return Message.CreateInSlot(Db, slot, flags, RedisCommand.BITOP, values);
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
                return Message.CreateInSlot(Db, slot, flags, RedisCommand.BITOP, new[] { op, destination.AsRedisValue(), first.AsRedisValue() });
            }
            // binary
            slot = serverSelectionStrategy.CombineSlot(slot, second);
            return Message.CreateInSlot(Db, slot, flags, RedisCommand.BITOP, new[] { op, destination.AsRedisValue(), first.AsRedisValue(), second.AsRedisValue() });
        }
        Message GetStringGetWithExpiryMessage(RedisKey key, CommandFlags flags, out ResultProcessor<RedisValueWithExpiry> processor, out ServerEndPoint server)
        {
            if (this is IBatch)
            {
                throw new NotSupportedException("This operation is not possible inside a transaction or batch; please issue separate GetString and KeyTimeToLive requests");
            }
            var features = GetFeatures(Db, key, flags, out server);
            processor = StringGetWithExpiryProcessor.Default;
            if (server != null && features.MillisecondExpiry && multiplexer.CommandMap.IsAvailable(RedisCommand.PTTL))
            {
                return new StringGetWithExpiryMessage(Db, flags, RedisCommand.PTTL, key);
            }
            // if we use TTL, it doesn't matter which server
            server = null;
            return new StringGetWithExpiryMessage(Db, flags, RedisCommand.TTL, key);
        }

        private Message GetStringSetMessage(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            if (values == null) throw new ArgumentNullException("values");
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
                    return Message.CreateInSlot(Db, slot, flags, when == When.NotExists ? RedisCommand.MSETNX : RedisCommand.MSET, args);
            }
        }
        Message GetStringSetMessage(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            WhenAlwaysOrExistsOrNotExists(when);
            if (value.IsNull) return Message.Create(Db, flags, RedisCommand.DEL, key);

            if (expiry == null || expiry.Value == TimeSpan.MaxValue)
            { // no expiry
                switch (when)
                {
                    case When.Always: return Message.Create(Db, flags, RedisCommand.SET, key, value);
                    case When.NotExists: return Message.Create(Db, flags, RedisCommand.SETNX, key, value);
                    case When.Exists: return Message.Create(Db, flags, RedisCommand.SET, key, value, RedisLiterals.XX);
                }
            }
            long milliseconds = expiry.Value.Ticks / TimeSpan.TicksPerMillisecond;

            if ((milliseconds % 1000) == 0)
            {
                // a nice round number of seconds
                long seconds = milliseconds / 1000;
                switch (when)
                {
                    case When.Always: return Message.Create(Db, flags, RedisCommand.SETEX, key, seconds, value);
                    case When.Exists: return Message.Create(Db, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.XX);
                    case When.NotExists: return Message.Create(Db, flags, RedisCommand.SET, key, value, RedisLiterals.EX, seconds, RedisLiterals.NX);
                }
            }

            switch (when)
            {
                case When.Always: return Message.Create(Db, flags, RedisCommand.PSETEX, key, milliseconds, value);
                case When.Exists: return Message.Create(Db, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.XX);
                case When.NotExists: return Message.Create(Db, flags, RedisCommand.SET, key, value, RedisLiterals.PX, milliseconds, RedisLiterals.NX);
            }
            throw new NotSupportedException();
        }
        Message IncrMessage(RedisKey key, long value, CommandFlags flags)
        {
            switch (value)
            {
                case 0:
                    if ((flags & CommandFlags.FireAndForget) != 0) return null;
                    return Message.Create(Db, flags, RedisCommand.INCRBY, key, value);
                case 1:
                    return Message.Create(Db, flags, RedisCommand.INCR, key);
                case -1:
                    return Message.Create(Db, flags, RedisCommand.DECR, key);
                default:
                    return value > 0
                        ? Message.Create(Db, flags, RedisCommand.INCRBY, key, value)
                        : Message.Create(Db, flags, RedisCommand.DECRBY, key, -value);
            }
        }

        private RedisCommand SetOperationCommand(SetOperation operation, bool store)
        {
            switch (operation)
            {
                case SetOperation.Difference: return store ? RedisCommand.SDIFFSTORE : RedisCommand.SDIFF;
                case SetOperation.Intersect: return store ? RedisCommand.SINTERSTORE : RedisCommand.SINTER;
                case SetOperation.Union: return store ? RedisCommand.SUNIONSTORE : RedisCommand.SUNION;
                default: throw new ArgumentOutOfRangeException("operation");
            }
        }
        struct SetScanResult
        {
            public static readonly ResultProcessor<SetScanResult> Processor = new SetScanResultProcessor();
            public readonly long Cursor;
            public readonly RedisValue[] Values;
            public SetScanResult(long cursor, RedisValue[] values)
            {
                this.Cursor = cursor;
                this.Values = values;
            }
            private class SetScanResultProcessor : ResultProcessor<SetScanResult>
            {
                protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
                {
                    switch (result.Type)
                    {
                        case ResultType.MultiBulk:
                            var arr = result.GetItems();
                            long i64;
                            if (arr.Length == 2 && arr[1].Type == ResultType.MultiBulk && arr[0].TryGetInt64(out i64))
                            {
                                var sscanResult = new SetScanResult(i64, arr[1].GetItemsAsValues());
                                SetResult(message, sscanResult);
                                return true;
                            }
                            break;
                    }
                    return false;
                }
            }
        }

        internal sealed class ScriptLoadMessage : Message
        {
            internal readonly string Script;
            public ScriptLoadMessage(CommandFlags flags, string script) : base(-1, flags, RedisCommand.SCRIPT)
            {
                if (script == null) throw new ArgumentNullException("script");
                this.Script = script;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(RedisLiterals.LOAD);
                physical.Write((RedisValue)Script);
            }
        }

        internal sealed class SetScanIterator
        {
            internal const int DefaultPageSize = 10;

            private readonly RedisDatabase database;

            private readonly CommandFlags flags;

            private readonly RedisKey key;
            private readonly int pageSize;
            private readonly RedisValue pattern;

            private readonly ServerEndPoint server;

            public SetScanIterator(RedisDatabase database, ServerEndPoint server, RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            {
                this.key = key;
                this.pageSize = pageSize;
                this.database = database;
                this.pattern = pattern;
                this.flags = flags;
                this.server = server;
            }

            public static bool IsNil(RedisValue pattern)
            {
                if (pattern.IsNullOrEmpty) return true;
                if (pattern.IsInteger) return false;
                byte[] rawValue = pattern;
                return rawValue.Length == 1 && rawValue[0] == '*';
            }
            public IEnumerable<RedisValue> Read()
            {
                var msg = CreateMessage(0, false);
                SetScanResult current = database.ExecuteSync(msg, SetScanResult.Processor, server);
                Task<SetScanResult> pending;
                do
                {
                    // kick off the next immediately, but don't wait for it yet
                    msg = CreateMessage(current.Cursor, true);
                    pending = msg == null ? null : database.ExecuteAsync(msg, SetScanResult.Processor, server);

                    // now we can iterate the rows
                    var values = current.Values;
                    for (int i = 0; i < values.Length; i++)
                        yield return values[i];

                    // wait for the next, if any
                    if (pending != null)
                    {
                        current = database.Wait(pending);
                    }

                } while (pending != null);
            }

            Message CreateMessage(long cursor, bool running)
            {
                if (cursor == 0 && running) return null; // end of the line
                if (IsNil(pattern))
                {
                    if (pageSize == DefaultPageSize)
                    {
                        return Message.Create(database.Database, flags, RedisCommand.SSCAN, key, cursor);
                    }
                    else
                    {
                        return Message.Create(database.Database, flags, RedisCommand.SSCAN, key, cursor, RedisLiterals.COUNT, pageSize);
                    }
                }
                else
                {
                    if (pageSize == DefaultPageSize)
                    {
                        return Message.Create(database.Database, flags, RedisCommand.SSCAN, key, cursor, RedisLiterals.MATCH, pattern);
                    }
                    else
                    {
                        return Message.Create(database.Database, flags, RedisCommand.SSCAN, key, new RedisValue[] { cursor, RedisLiterals.MATCH, pattern, RedisLiterals.COUNT, pageSize });
                    }
                }
            }
        }
        private sealed class ScriptEvalMessage : Message, IMultiMessage
        {
            private readonly RedisKey[] keys;
            private readonly string script;
            private readonly RedisValue[] values;
            private RedisValue hash;
            public ScriptEvalMessage(int db, CommandFlags flags, RedisCommand command, string script, RedisKey[] keys, RedisValue[] values) : base(db, flags, command)
            {
                if (script == null) throw new ArgumentNullException("script");
                this.script = script;
                for (int i = 0; i < keys.Length; i++)
                    keys[i].Assert();
                this.keys = keys;
                for (int i = 0; i < values.Length; i++)
                    values[i].Assert();
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
                this.hash = connection.Bridge.ServerEndPoint.GetScriptHash(script);
                if(hash.IsNull)
                {
                    var msg = new ScriptLoadMessage(Flags, script);
                    msg.SetInternalCall();
                    msg.SetSource(ResultProcessor.ScriptLoad, null);
                    yield return msg;
                }
                yield return this;
            }

            internal override void WriteImpl(PhysicalConnection physical)
            {
                if(hash.IsNull)
                {
                    physical.WriteHeader(RedisCommand.EVAL, 2 + keys.Length + values.Length);
                    physical.Write((RedisValue)script);
                }
                else
                {
                    physical.WriteHeader(RedisCommand.EVALSHA, 2 + keys.Length + values.Length);
                    physical.Write(hash);
                }                
                physical.Write(keys.Length);
                for (int i = 0; i < keys.Length; i++)
                    physical.Write(keys[i]);
                for (int i = 0; i < values.Length; i++)
                    physical.Write(values[i]);
            }
        }
        sealed class SortedSetCombineAndStoreCommandMessage : Message.CommandKeyBase // ZINTERSTORE and ZUNIONSTORE have a very unusual signature
        {
            private readonly RedisKey[] keys;
            private readonly RedisValue[] values;
            public SortedSetCombineAndStoreCommandMessage(int db, CommandFlags flags, RedisCommand command, RedisKey destination, RedisKey[] keys, RedisValue[] values) : base(db, flags, command, destination)
            {
                for (int i = 0; i < keys.Length; i++)
                    keys[i].Assert();
                this.keys = keys;
                for (int i = 0; i < values.Length; i++)
                    values[i].Assert();
                this.values = values;
            }
            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                int slot = base.GetHashSlot(serverSelectionStrategy);
                for (int i = 0; i < keys.Length; i++)
                    slot = serverSelectionStrategy.CombineSlot(slot, keys[i]);
                return slot;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2 + keys.Length + values.Length);
                physical.Write(Key);
                physical.Write(keys.Length);
                for (int i = 0; i < keys.Length; i++)
                    physical.Write(keys[i]);
                for (int i = 0; i < values.Length; i++)
                    physical.Write(values[i]);
            }
        }
        private class StringGetWithExpiryMessage : Message.CommandKeyBase, IMultiMessage
        {
            private readonly RedisCommand ttlCommand;
            private ResultBox<TimeSpan?> box;

            public StringGetWithExpiryMessage(int db, CommandFlags flags, RedisCommand ttlCommand, RedisKey key)
                            : base(db, flags | CommandFlags.NoRedirect /* <== not implemented/tested */, RedisCommand.GET, key)
            {
                this.ttlCommand = ttlCommand;
            }
            public override string CommandAndKey { get { return ttlCommand + "+" + RedisCommand.GET + " " + Key; } }

            public IEnumerable<Message> GetMessages(PhysicalConnection connection)
            {
                box = ResultBox<TimeSpan?>.Get(null);
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
                    ResultBox<TimeSpan?>.UnwrapAndRecycle(box, out value, out ex);
                    box = null;
                    return ex == null;
                }
                value = null;
                ex = null;
                return false;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, 1);
                physical.Write(Key);
            }
        }

        private class StringGetWithExpiryProcessor : ResultProcessor<RedisValueWithExpiry>
        {
            public static readonly ResultProcessor<RedisValueWithExpiry> Default = new StringGetWithExpiryProcessor();
            private StringGetWithExpiryProcessor() { }
            protected  override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
            {
                switch(result.Type)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        RedisValue value = result.AsRedisValue();
                        var sgwem = message as StringGetWithExpiryMessage;
                        TimeSpan? expiry;
                        Exception ex;
                        if (sgwem != null && sgwem.UnwrapValue(out expiry, out ex))
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
