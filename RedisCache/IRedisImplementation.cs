using System.Collections.Generic;
using StackExchange.Redis;

namespace Saxo.RedisCache
{
    public interface IRedisImplementation
    {
        void StringSet(RedisKey primaryKey, RedisValue value);
        void StringSet(KeyValuePair<RedisKey, RedisValue>[] toArray);

        RedisValue StringGet(RedisKey primaryKey);
        RedisValue[] StringGet(RedisKey[] primaryKey);

        void KeyDelete(RedisKey primaryKey);
        void KeyDelete(RedisKey[] primaryKey);

        void Clear();
    }
}
