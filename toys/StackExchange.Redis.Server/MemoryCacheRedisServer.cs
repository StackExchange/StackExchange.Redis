using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Runtime.CompilerServices;

namespace StackExchange.Redis.Server
{
    public class MemoryCacheRedisServer : RedisServer
    {
        public MemoryCacheRedisServer(TextWriter output = null) : base(1, output)
            => CreateNewCache();

        private MemoryCache _cache;

        private void CreateNewCache()
        {
            var old = _cache;
            _cache = new MemoryCache(GetType().Name);
            old?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cache.Dispose();
            base.Dispose(disposing);
        }

        protected override long Dbsize(int database) => _cache.GetCount();
        protected override RedisValue Get(int database, RedisKey key)
            => RedisValue.Unbox(_cache[key]);
        protected override void Set(int database, RedisKey key, RedisValue value)
            => _cache[key] = value.Box();
        protected override bool Del(int database, RedisKey key)
            => _cache.Remove(key) != null;
        protected override void Flushdb(int database)
            => CreateNewCache();

        protected override bool Exists(int database, RedisKey key)
            => _cache.Contains(key);

        protected override IEnumerable<RedisKey> Keys(int database, RedisKey pattern)
        {
            foreach (var pair in _cache)
            {
                if (IsMatch(pattern, pair.Key)) yield return pair.Key;
            }
        }
        protected override bool Sadd(int database, RedisKey key, RedisValue value)
            => GetSet(key, true).Add(value);

        protected override bool Sismember(int database, RedisKey key, RedisValue value)
            => GetSet(key, false)?.Contains(value) ?? false;

        protected override bool Srem(int database, RedisKey key, RedisValue value)
        {
            var set = GetSet(key, false);
            if (set != null && set.Remove(value))
            {
                if (set.Count == 0) _cache.Remove(key);
                return true;
            }
            return false;
        }
        protected override long Scard(int database, RedisKey key)
            => GetSet(key, false)?.Count ?? 0;

        private HashSet<RedisValue> GetSet(RedisKey key, bool create)
        {
            var set = (HashSet<RedisValue>)_cache[key];
            if (set == null && create)
            {
                set = new HashSet<RedisValue>();
                _cache[key] = set;
            }
            return set;
        }

        protected override RedisValue Spop(int database, RedisKey key)
        {
            var set = GetSet(key, false);
            if (set == null) return RedisValue.Null;

            var result = set.First();
            set.Remove(result);
            if (set.Count == 0) _cache.Remove(key);
            return result;
        }

        protected override long Lpush(int database, RedisKey key, RedisValue value)
        {
            var stack = GetStack(key, true);
            stack.Push(value);
            return stack.Count;
        }
        protected override RedisValue Lpop(int database, RedisKey key)
        {
            var stack = GetStack(key, false);
            if (stack == null) return RedisValue.Null;

            var val = stack.Pop();
            if (stack.Count == 0) _cache.Remove(key);
            return val;
        }

        protected override long Llen(int database, RedisKey key)
            => GetStack(key, false)?.Count ?? 0;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentOutOfRangeException() => throw new ArgumentOutOfRangeException();

        protected override void LRange(int database, RedisKey key, long start, Span<TypedRedisValue> arr)
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

        private Stack<RedisValue> GetStack(RedisKey key, bool create)
        {
            var stack = (Stack<RedisValue>)_cache[key];
            if (stack == null && create)
            {
                stack = new Stack<RedisValue>();
                _cache[key] = stack;
            }
            return stack;
        }
    }
}
