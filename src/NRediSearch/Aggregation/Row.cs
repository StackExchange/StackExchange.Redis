// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;
using StackExchange.Redis;

namespace NRediSearch.Aggregation
{
    public readonly struct Row
    {
        private readonly Dictionary<string, RedisValue> _fields;

        internal Row(Dictionary<string, RedisValue> fields)
        {
            _fields = fields;
        }

        public bool ContainsKey(string key) => _fields.ContainsKey(key);
        public RedisValue this[string key] => _fields.TryGetValue(key, out var result) ? result : RedisValue.Null;

        public string GetString(string key) => _fields.TryGetValue(key, out var result) ? (string)result : default;
        public long GetInt64(string key) => _fields.TryGetValue(key, out var result) ? (long)result : default;
        public double GetDouble(string key) => _fields.TryGetValue(key, out var result) ? (double)result : default;
    }
}
