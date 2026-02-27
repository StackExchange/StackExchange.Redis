using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Runtime.CompilerServices;

namespace StackExchange.Redis.Server
{
    public class MemoryCacheRedisServer : RedisServer
    {
        public MemoryCacheRedisServer(EndPoint endpoint = null, TextWriter output = null) : base(endpoint, 1, output)
            => CreateNewCache();

        private MemoryCache _cache2;

        private void CreateNewCache()
        {
            var old = _cache2;
            _cache2 = new MemoryCache(GetType().Name);
            old?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cache2.Dispose();
            base.Dispose(disposing);
        }

        protected override long Dbsize(int database) => _cache2.GetCount();

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
        private object Get(in RedisKey key, ExpectedType expectedType)
        {
            var val = _cache2[key];
            switch (val)
            {
                case null:
                    return null;
                case ExpiringValue ev:
                    if (ev.AbsoluteExpiration <= Time())
                    {
                        _cache2.Remove(key);
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
            var val = _cache2[key];
            switch (val)
            {
                case null:
                    return null;
                case ExpiringValue ev:
                    var delta = ev.AbsoluteExpiration - Time();
                    if (delta <= TimeSpan.Zero)
                    {
                        _cache2.Remove(key);
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
            var val = Get(key, ExpectedType.Any);
            if (val is not null)
            {
                _cache2[key] = new ExpiringValue(val, Time() + timeout);
                return true;
            }

            return false;
        }

        protected override RedisValue Get(int database, in RedisKey key)
        {
            var val = Get(key, ExpectedType.Stack);
            return RedisValue.Unbox(val);
        }

        protected override void Set(int database, in RedisKey key, in RedisValue value)
            => _cache2[key] = value.Box();

        protected override void SetEx(int database, in RedisKey key, TimeSpan expiration, in RedisValue value)
        {
            var now = Time();
            var absolute = now + expiration;
            if (absolute <= now) _cache2.Remove(key);
            else _cache2[key] = new ExpiringValue(value.Box(), absolute);
        }

        protected override bool Del(int database, in RedisKey key)
            => _cache2.Remove(key) != null;
        protected override void Flushdb(int database)
            => CreateNewCache();

        protected override bool Exists(int database, in RedisKey key)
        {
            var val = Get(key, ExpectedType.Any);
            return val != null && !(val is ExpiringValue ev && ev.AbsoluteExpiration <= Time());
        }

        protected override IEnumerable<RedisKey> Keys(int database, in RedisKey pattern) => GetKeysCore(pattern);
        private IEnumerable<RedisKey> GetKeysCore(RedisKey pattern)
        {
            foreach (var pair in _cache2)
            {
                if (pair.Value is ExpiringValue ev && ev.AbsoluteExpiration <= Time()) continue;
                if (IsMatch(pattern, pair.Key)) yield return pair.Key;
            }
        }
        protected override bool Sadd(int database, in RedisKey key, in RedisValue value)
            => GetSet(key, true).Add(value);

        protected override bool Sismember(int database, in RedisKey key, in RedisValue value)
            => GetSet(key, false)?.Contains(value) ?? false;

        protected override bool Srem(int database, in RedisKey key, in RedisValue value)
        {
            var set = GetSet(key, false);
            if (set != null && set.Remove(value))
            {
                if (set.Count == 0) _cache2.Remove(key);
                return true;
            }
            return false;
        }
        protected override long Scard(int database, in RedisKey key)
            => GetSet(key, false)?.Count ?? 0;

        private HashSet<RedisValue> GetSet(RedisKey key, bool create)
        {
            var set = (HashSet<RedisValue>)Get(key, ExpectedType.Set);
            if (set == null && create)
            {
                set = new HashSet<RedisValue>();
                _cache2[key] = set;
            }
            return set;
        }

        protected override RedisValue Spop(int database, in RedisKey key)
        {
            var set = GetSet(key, false);
            if (set == null) return RedisValue.Null;

            var result = set.First();
            set.Remove(result);
            if (set.Count == 0) _cache2.Remove(key);
            return result;
        }

        protected override long Lpush(int database, in RedisKey key, in RedisValue value)
        {
            var stack = GetStack(key, true);
            stack.Push(value);
            return stack.Count;
        }
        protected override RedisValue Lpop(int database, in RedisKey key)
        {
            var stack = GetStack(key, false);
            if (stack == null) return RedisValue.Null;

            var val = stack.Pop();
            if (stack.Count == 0) _cache2.Remove(key);
            return val;
        }

        protected override long Llen(int database, in RedisKey key)
            => GetStack(key, false)?.Count ?? 0;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentOutOfRangeException() => throw new ArgumentOutOfRangeException();

        protected override void LRange(int database, in RedisKey key, long start, Span<TypedRedisValue> arr)
        {
            var stack = GetStack(key, false);

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

        private Stack<RedisValue> GetStack(in RedisKey key, bool create)
        {
            var stack = (Stack<RedisValue>)Get(key, ExpectedType.Stack);
            if (stack == null && create)
            {
                stack = new Stack<RedisValue>();
                _cache2[key] = stack;
            }
            return stack;
        }
    }
}
