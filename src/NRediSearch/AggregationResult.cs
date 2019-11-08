// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;
using NRediSearch.Aggregation;
using StackExchange.Redis;

namespace NRediSearch
{
    public sealed class AggregationResult
    {
        private readonly Dictionary<string, RedisValue>[] _results;

        internal AggregationResult(RedisResult result, long cursorId = -1)
        {
            var arr = (RedisResult[])result;

            _results = new Dictionary<string, RedisValue>[arr.Length - 1];
            for (int i = 1; i < arr.Length; i++)
            {
                var raw = (RedisResult[])arr[i];
                var cur = new Dictionary<string, RedisValue>();
                for (int j = 0; j < raw.Length;)
                {
                    var key = (string)raw[j++];
                    var val = raw[j++];
                    if (val.Type != ResultType.MultiBulk)
                        cur.Add(key, (RedisValue)val);
                }
                _results[i - 1] = cur;
            }

            CursorId = cursorId;
        }
        public IReadOnlyList<Dictionary<string, RedisValue>> GetResults() => _results;

        public Dictionary<string, RedisValue> this[int index]
            => index >= _results.Length ? null : _results[index];

        public Row? GetRow(int index)
        {
            if (index >= _results.Length) return null;
            return new Row(_results[index]);
        }

        public long CursorId { get; }
    }
}
