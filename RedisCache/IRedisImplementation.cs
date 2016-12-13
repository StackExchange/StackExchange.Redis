using System;
using System.Collections.Generic;
using StackExchange.Redis;

namespace Saxo.RedisCache
{
    public interface IRedisImplementation
    {
        bool IsAlive();

        void StringSet(RedisKey primaryKey, RedisValue value, TimeSpan? expire = null);
        void StringSet(KeyValuePair<RedisKey, RedisValue>[] toArray, TimeSpan? expire = null);

        RedisValue StringGet(RedisKey primaryKey);
        RedisValue[] StringGet(RedisKey[] primaryKey);

        void KeyDelete(RedisKey primaryKey);
        void KeyDelete(RedisKey[] primaryKey);

        void Clear();
        void ClearByTag(string tag);
    }
}
