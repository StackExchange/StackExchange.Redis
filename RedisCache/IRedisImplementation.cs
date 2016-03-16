using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RedisCache
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
