using System.Collections.Generic;
using StackExchange.Redis;

namespace NRediSearch
{
    public class InfoResult
    {
        public string IndexName { get; }

        public Dictionary<string, RedisResult[]> Fields { get; } = new Dictionary<string, RedisResult[]>();

        public long NumDocs { get; }

        public long NumTerms { get; }

        public long NumRecords { get; }

        public double InvertedSzMb { get; }

        public double InvertedCapMb { get; }

        public double InvertedCapOvh { get; }

        public double OffsetVectorsSzMb { get; }

        public double SkipIndexSizeMb { get; }

        public double ScoreIndexSizeMb { get; }

        public double RecordsPerDocAvg { get; }

        public double BytesPerRecordAvg { get; }

        public double OffsetsPerTermAvg { get; }

        public double OffsetBitsPerRecordAvg { get; }

        public string MaxDocId { get; }

        public double DocTableSizeMb { get; }

        public double SortableValueSizeMb { get; }

        public double KeyTableSizeMb { get; }

        public Dictionary<string, RedisResult> GcStats { get; } = new Dictionary<string, RedisResult>();

        public Dictionary<string, RedisResult> CursorStats { get; } = new Dictionary<string, RedisResult>();

        public InfoResult(RedisResult result)
        {
            var results = (RedisResult[])result;

            for (var i = 0; i < results.Length; i += 2)
            {
                var key = (string)results[i];
                var value = results[i + 1];

                if (value.IsNull)
                {
                    continue;
                }

                switch (key)
                {
                    case "index_name":
                        IndexName = (string)value;
                        break;
                    case "fields":
                        var fieldVals = (RedisResult[])value;

                        foreach (RedisResult[] fv in fieldVals)
                        {
                            Fields.Add((string)fv[0], fv);
                        }

                        break;
                    case "num_docs":
                        NumDocs = (long)value;
                        break;
                    case "max_doc_id":
                        MaxDocId = (string)value;
                        break;
                    case "num_terms":
                        NumTerms = (long)value;
                        break;
                    case "num_records":
                        NumRecords = (long)value;
                        break;
                    case "inverted_sz_mb":
                        InvertedSzMb = (double)value;
                        break;
                    case "offset_vectors_sz_mb":
                        OffsetVectorsSzMb = (double)value;
                        break;
                    case "doc_table_size_mb":
                        DocTableSizeMb = (double)value;
                        break;
                    case "sortable_values_size_mb":
                        SortableValueSizeMb = (double)value;
                        break;
                    case "key_table_size_mb":
                        KeyTableSizeMb = (double)value;
                        break;
                    case "records_per_doc_avg":
                        if ((string)value == "-nan")
                        {
                            continue;
                        }
                        RecordsPerDocAvg = (double)value;
                        break;
                    case "bytes_per_record_avg":
                        if ((string)value == "-nan")
                        {
                            continue;
                        }
                        BytesPerRecordAvg = (double)value;
                        break;
                    case "offset_per_term_avg":
                        if ((string)value == "-nan")
                        {
                            continue;
                        }
                        OffsetsPerTermAvg = (double)value;
                        break;
                    case "offset_bits_per_record_avg":
                        if ((string)value == "-nan")
                        {
                            continue;
                        }
                        OffsetBitsPerRecordAvg = (double)value;
                        break;
                    case "gc_stats":
                        var gcStatsValues = (RedisResult[])value;
                        for(var ii = 0; ii < gcStatsValues.Length; ii += 2)
                        {
                            GcStats.Add((string)gcStatsValues[ii], gcStatsValues[ii + 1]);
                        }
                        break;
                    case "cursor_stats":
                        var csValues = (RedisResult[])value;
                        for (var ii = 0; ii < csValues.Length; ii += 2)
                        {
                            CursorStats.Add((string)csValues[ii], csValues[ii + 1]);
                        }
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
