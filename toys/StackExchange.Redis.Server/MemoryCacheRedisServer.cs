using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StackExchange.Redis.Server
{
    public class MemoryCacheRedisServer : RedisServer
    {
        private readonly string _cacheNamePrefix = $"{nameof(MemoryCacheRedisServer)}.{Guid.NewGuid():N}";
        private readonly ConcurrentDictionary<int, MemoryCache> _cache2 = new();
        private int _nextCacheId;

        public MemoryCacheRedisServer(EndPoint endpoint = null, int databases = DefaultDatabaseCount, TextWriter output = null) : base(endpoint, databases, output)
        {
        }

        private MemoryCache CreateNewCache(int database)
            => new($"{_cacheNamePrefix}.{database}.{Interlocked.Increment(ref _nextCacheId)}");

        private MemoryCache GetDb(int database)
        {
            while (true)
            {
                if (_cache2.TryGetValue(database, out var existing)) return existing;

                var created = CreateNewCache(database);
                if (_cache2.TryAdd(database, created)) return created;

                created.Dispose();
            }
        }

        private void FlushDbCore(int database)
        {
            if (_cache2.TryRemove(database, out var cache)) cache.Dispose();
        }

        private void FlushAllCore()
        {
            foreach (var pair in _cache2)
            {
                if (_cache2.TryRemove(pair.Key, out var cache)) cache.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) FlushAllCore();
            base.Dispose(disposing);
        }

        protected override long Dbsize(int database) => GetDb(database).GetCount();

        private readonly struct ExpiringValue(object value, DateTime absoluteExpiration)
        {
            public readonly object Value = value;
            public readonly DateTime AbsoluteExpiration = absoluteExpiration;
        }

        private enum ExpectedType
        {
            Any = 0,
            Stack,
            Set,
            List,
        }
        private object Get(int database, in RedisKey key, ExpectedType expectedType)
        {
            var db = GetDb(database);
            var val = db[key];
            switch (val)
            {
                case null:
                    return null;
                case ExpiringValue ev:
                    if (ev.AbsoluteExpiration <= Time())
                    {
                        db.Remove(key);
                        return null;
                    }
                    return Validate(ev.Value, expectedType);
                default:
                    return Validate(val, expectedType);
            }
            static object Validate(object value, ExpectedType expectedType)
            {
                return value switch
                {
                    null => value,
                    HashSet<RedisValue> set when expectedType is ExpectedType.Set or ExpectedType.Any => value,
                    HashSet<RedisValue> => Throw(),
                    Stack<RedisValue> stack when expectedType is ExpectedType.List or ExpectedType.Any => value,
                    Stack<RedisValue> => Throw(),
                    _ when expectedType is ExpectedType.Stack or ExpectedType.Any => value,
                    _ => Throw(),
                };

                static object Throw() => throw new WrongTypeException();
            }
        }
        protected override TimeSpan? Ttl(int database, in RedisKey key)
        {
            var db = GetDb(database);
            var val = db[key];
            switch (val)
            {
                case null:
                    return null;
                case ExpiringValue ev:
                    var delta = ev.AbsoluteExpiration - Time();
                    if (delta <= TimeSpan.Zero)
                    {
                        db.Remove(key);
                        return null;
                    }
                    return delta;
                default:
                    return TimeSpan.MaxValue;
            }
        }

        protected override bool Expire(int database, in RedisKey key, TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero) return Del(database, key);
            var db = GetDb(database);
            var val = Get(database, key, ExpectedType.Any);
            if (val is not null)
            {
                db[key] = new ExpiringValue(val, Time() + timeout);
                return true;
            }

            return false;
        }

        protected override RedisValue Get(int database, in RedisKey key)
        {
            var val = Get(database, key, ExpectedType.Stack);
            return RedisValue.Unbox(val);
        }

        protected override void Set(int database, in RedisKey key, in RedisValue value)
            => GetDb(database)[key] = value.Box();

        protected override void SetEx(int database, in RedisKey key, TimeSpan expiration, in RedisValue value)
        {
            var db = GetDb(database);
            var now = Time();
            var absolute = now + expiration;
            if (absolute <= now) db.Remove(key);
            else db[key] = new ExpiringValue(value.Box(), absolute);
        }

        protected override bool Del(int database, in RedisKey key)
            => GetDb(database).Remove(key) != null;
        protected override void Flushdb(int database)
            => FlushDbCore(database);

        protected override TypedRedisValue Flushall(RedisClient client, in RedisRequest request)
        {
            FlushAllCore();
            return TypedRedisValue.OK;
        }

        protected override bool Exists(int database, in RedisKey key)
            => Get(database, key, ExpectedType.Any) is not null;

        protected override IEnumerable<RedisKey> Keys(int database, in RedisKey pattern) => GetKeysCore(database, pattern);
        private IEnumerable<RedisKey> GetKeysCore(int database, RedisKey pattern)
        {
            foreach (var pair in GetDb(database))
            {
                if (pair.Value is ExpiringValue ev && ev.AbsoluteExpiration <= Time()) continue;
                if (IsMatch(pattern, pair.Key)) yield return pair.Key;
            }
        }
        protected override bool Sadd(int database, in RedisKey key, in RedisValue value)
            => GetSet(database, key, true).Add(value);

        protected override bool Sismember(int database, in RedisKey key, in RedisValue value)
            => GetSet(database, key, false)?.Contains(value) ?? false;

        protected override bool Srem(int database, in RedisKey key, in RedisValue value)
        {
            var db = GetDb(database);
            var set = GetSet(database, key, false);
            if (set != null && set.Remove(value))
            {
                if (set.Count == 0) db.Remove(key);
                return true;
            }
            return false;
        }
        protected override long Scard(int database, in RedisKey key)
            => GetSet(database, key, false)?.Count ?? 0;

        private HashSet<RedisValue> GetSet(int database, in RedisKey key, bool create)
        {
            var db = GetDb(database);
            var set = (HashSet<RedisValue>)Get(database, key, ExpectedType.Set);
            if (set == null && create)
            {
                set = new HashSet<RedisValue>();
                db[key] = set;
            }
            return set;
        }

        protected override RedisValue Spop(int database, in RedisKey key)
        {
            var db = GetDb(database);
            var set = GetSet(database, key, false);
            if (set == null) return RedisValue.Null;

            var result = set.First();
            set.Remove(result);
            if (set.Count == 0) db.Remove(key);
            return result;
        }

        protected override long Lpush(int database, in RedisKey key, in RedisValue value)
        {
            var stack = GetStack(database, key, true);
            stack.Push(value);
            return stack.Count;
        }
        protected override RedisValue Lpop(int database, in RedisKey key)
        {
            var db = GetDb(database);
            var stack = GetStack(database, key, false);
            if (stack == null) return RedisValue.Null;

            var val = stack.Pop();
            if (stack.Count == 0) db.Remove(key);
            return val;
        }

        protected override long Llen(int database, in RedisKey key)
            => GetStack(database, key, false)?.Count ?? 0;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentOutOfRangeException() => throw new ArgumentOutOfRangeException();

        protected override void LRange(int database, in RedisKey key, long start, Span<TypedRedisValue> arr)
        {
            var stack = GetStack(database, key, false);

            using (var iter = stack.GetEnumerator())
            {
                // skip
                while (start-- > 0) if (!iter.MoveNext()) ThrowArgumentOutOfRangeException();

                // take
                for (int i = 0; i < arr.Length; i++)
                {
                    if (!iter.MoveNext()) ThrowArgumentOutOfRangeException();
                    arr[i] = TypedRedisValue.BulkString(iter.Current);
                }
            }
        }

        private Stack<RedisValue> GetStack(int database, in RedisKey key, bool create)
        {
            var db = GetDb(database);
            var stack = (Stack<RedisValue>)Get(database, key, ExpectedType.Stack);
            if (stack == null && create)
            {
                stack = new Stack<RedisValue>();
                db[key] = stack;
            }
            return stack;
        }
    }
}
