// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;
using StackExchange.Redis;

namespace NRediSearch
{
    /// <summary>
    /// Document represents a single indexed document or entity in the engine
    /// </summary>
    public class Document
    {
        public string Id { get; }
        public double Score { get; }
        public byte[] Payload { get; }
        public string[] ScoreExplained { get; private set; }
        internal readonly Dictionary<string, RedisValue> _properties;
        public Document(string id, double score, byte[] payload) : this(id, null, score, payload) { }
        public Document(string id) : this(id, null, 1.0, null) { }

        public Document(string id, Dictionary<string, RedisValue> fields, double score = 1.0) : this(id, fields, score, null) { }

        public Document(string id, Dictionary<string, RedisValue> fields, double score, byte[] payload)
        {
            Id = id;
            _properties = fields ?? new Dictionary<string, RedisValue>();
            Score = score;
            Payload = payload;
        }

        public IEnumerable<KeyValuePair<string, RedisValue>> GetProperties() => _properties;

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

        public static Document Load(string id, double score, byte[] payload, RedisValue[] fields, string[] scoreExplained)
        {
            Document ret = Document.Load(id, score, payload, fields);
            if (scoreExplained != null)
            {
                ret.ScoreExplained = scoreExplained;
            }
            return ret;
        }

        public RedisValue this[string key]
        {
            get { return _properties.TryGetValue(key, out var val) ? val : default(RedisValue); }
            internal set { _properties[key] = value; }
        }

        public bool HasProperty(string key) => _properties.ContainsKey(key);

        internal static Document Parse(string docId, RedisResult result)
        {
            if (result == null || result.IsNull) return null;
            var arr = (RedisResult[])result;
            var doc = new Document(docId);

            for(int i = 0; i < arr.Length; )
            {
                doc[(string)arr[i++]] = (RedisValue)arr[i++];
            }
            return doc;
        }

        public Document Set(string field, RedisValue value)
        {
            this[field] = value;
            return this;
        }
    }
}
