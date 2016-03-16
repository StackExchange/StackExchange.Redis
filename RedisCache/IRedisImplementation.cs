using System.Collections.Generic;
using StackExchange.Redis;

namespace Saxo.RedisCache
{
    public interface IRedisImplementation
    {
        void StringSet(string primaryKey, string value);
        void StringSet(KeyValuePair<RedisKey, RedisValue>[] toArray);

        string StringGet(string primaryKey);
        RedisValue[] StringGet(RedisKey[] primaryKey);

        void KeyDelete(string primaryKey);
        void KeyDelete(RedisKey[] primaryKey);

        void Clear();
    }
}
