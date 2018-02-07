// .NET port of https://github.com/RedisLabs/JRediSearch/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace StackExchange.Redis.Modules.RediSearch
{
    /// <summary>
    /// Document represents a single indexed document or entity in the engine
    /// </summary>
    public class Document
    {

        public string Id { get; }
        public double Score { get; }
        public byte[] Payload { get; }
        private Dictionary<String, RedisValue> properties = new Dictionary<string, RedisValue>();

        public Document(string id, double score, byte[] payload)
        {
            Id = id;
            Score = score;
            Payload = payload;
        }

        public static Document Load(string id, double score, byte[] payload, RedisValue[] fields)
        {
            Document ret = new Document(id, score, payload);
            if (fields != null)
            {
                for (int i = 0; i < fields.Length; i += 2)
                {
                    ret[(string)fields[i]] = fields[i + 1];
                }
            }
            return ret;
        }

        public RedisValue this[string key]
        {
            get { return properties.TryGetValue(key, out var val) ? val : default(RedisValue); }
            internal set { properties[key] = value; }
        }

        public bool HasProperty(string key) => properties.ContainsKey(key);
    }
}
