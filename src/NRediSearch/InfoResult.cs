using System.Collections.Generic;
using StackExchange.Redis;

namespace NRediSearch
{
    public class InfoResult
    {
        private readonly Dictionary<string, RedisResult> _all = new Dictionary<string, RedisResult>();

        public string IndexName => GetString("index_name");

        public Dictionary<string, RedisResult[]> Fields => GetRedisResultsDictionary("fields");

        public long NumDocs => GetLong("num_docs");

        public long NumTerms => GetLong("num_terms");

        public long NumRecords => GetLong("num_records");

        public double InvertedSzMebibytes => GetDouble("inverted_sz_mb");

        public double InvertedCapMebibytes => GetDouble("inverted_cap_mb");

        public double InvertedCapOvh => GetDouble("inverted_cap_ovh");

        public double OffsetVectorsSzMebibytes => GetDouble("offset_vectors_sz_mb");

        public double SkipIndexSizeMebibytes => GetDouble("skip_index_size_mb");

        public double ScoreIndexSizeMebibytes => GetDouble("score_index_size_mb");

        public double RecordsPerDocAvg => GetDouble("records_per_doc_avg");

        public double BytesPerRecordAvg => GetDouble("bytes_per_record_avg");

        public double OffsetsPerTermAvg => GetDouble("offsets_per_term_avg");

        public double OffsetBitsPerRecordAvg => GetDouble("offset_bits_per_record_avg");

        public string MaxDocId => GetString("max_doc_id");

        public double DocTableSizeMebibytes => GetDouble("doc_table_size_mb");

        public double SortableValueSizeMebibytes => GetDouble("sortable_value_size_mb");

        public double KeyTableSizeMebibytes => GetDouble("key_table_size_mb");

        public Dictionary<string, RedisResult> GcStats => GetRedisResultDictionary("gc_stats");

        public Dictionary<string, RedisResult> CursorStats => GetRedisResultDictionary("cursor_stats");

        public InfoResult(RedisResult result)
        {
            var results = (RedisResult[])result;

            for (var i = 0; i < results.Length; i += 2)
            {
                var key = (string)results[i];
                var value = results[i + 1];

                _all.Add(key, value);
            }
        }

        private string GetString(string key) => _all.TryGetValue(key, out var value) ? (string)value : default;

        private long GetLong(string key) => _all.TryGetValue(key, out var value) ? (long)value : default;

        private double GetDouble(string key)
        {
            if (_all.TryGetValue(key, out var value))
            {
                if ((string)value == "-nan")
                {
                    return default;
                }
                else
                {
                    return (double)value;
                }
            }
            else
            {
                return default;
            }
        }

        private Dictionary<string, RedisResult> GetRedisResultDictionary(string key)
        {
            if (_all.TryGetValue(key, out var value))
            {
                var values = (RedisResult[])value;
                var result = new Dictionary<string, RedisResult>();

                for (var ii = 0; ii < values.Length; ii += 2)
                {
                    result.Add((string)values[ii], values[ii + 1]);
                }

                return result;
            }
            else
            {
                return default;
            }
        }

        private Dictionary<string, RedisResult[]> GetRedisResultsDictionary(string key)
        {
            if (_all.TryGetValue(key, out var value))
            {
                var result = new Dictionary<string, RedisResult[]>();

                foreach (RedisResult[] fv in (RedisResult[])value)
                {
                    result.Add((string)fv[0], fv);
                }

                return result;
            }
            else
            {
                return default;
            }
        }
    }
}
