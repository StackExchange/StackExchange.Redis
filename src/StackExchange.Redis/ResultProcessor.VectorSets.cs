using Pipelines.Sockets.Unofficial.Arenas;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    // VectorSet result processors
    public static readonly ResultProcessor<Lease<VectorSetLink>?> VectorSetLinksWithScores =
        new VectorSetLinksWithScoresProcessor();

    public static readonly ResultProcessor<Lease<RedisValue>?> VectorSetLinks = new VectorSetLinksProcessor();

    public static ResultProcessor<VectorSetInfo?> VectorSetInfo = new VectorSetInfoProcessor();

    private sealed class VectorSetLinksWithScoresProcessor : FlattenedLeaseProcessor<VectorSetLink>
    {
        protected override long GetArrayLength(in RawResult array) => array.GetItems().Length / 2;

        protected override bool TryReadOne(ref Sequence<RawResult>.Enumerator reader, out VectorSetLink value)
        {
            if (reader.MoveNext())
            {
                ref readonly RawResult first = ref reader.Current;
                if (reader.MoveNext() && reader.Current.TryGetDouble(out var score))
                {
                    value = new VectorSetLink(first.AsRedisValue(), score);
                    return true;
                }
            }

            value = default;
            return false;
        }
    }

    private sealed class VectorSetLinksProcessor : FlattenedLeaseProcessor<RedisValue>
    {
        protected override bool TryReadOne(in RawResult result, out RedisValue value)
        {
            value = result.AsRedisValue();
            return true;
        }
    }

    private sealed partial class VectorSetInfoProcessor : ResultProcessor<VectorSetInfo?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.Resp2TypeArray == ResultType.Array)
            {
                if (result.IsNull)
                {
                    SetResult(message, null);
                    return true;
                }

                var quantType = VectorSetQuantization.Unknown;
                string? quantTypeRaw = null;
                int vectorDim = 0, maxLevel = 0;
                long resultSize = 0, vsetUid = 0, hnswMaxNodeUid = 0;
                var iter = result.GetItems().GetEnumerator();
                while (iter.MoveNext())
                {
                    ref readonly RawResult key = ref iter.Current;
                    if (!iter.MoveNext()) break;
                    ref readonly RawResult value = ref iter.Current;

                    var len = key.Payload.Length;
                    var keyHash = key.Payload.Hash64();
                    switch (key.Payload.Length)
                    {
                        case size.Length when size.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            resultSize = i64;
                            break;
                        case vset_uid.Length when vset_uid.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            vsetUid = i64;
                            break;
                        case max_level.Length when max_level.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            maxLevel = checked((int)i64);
                            break;
                        case vector_dim.Length
                            when vector_dim.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            vectorDim = checked((int)i64);
                            break;
                        case quant_type.Length when quant_type.Is(keyHash, key):
                            var qHash = value.Payload.Hash64();
                            switch (value.Payload.Length)
                            {
                                case bin.Length when bin.Is(qHash, value):
                                    quantType = VectorSetQuantization.Binary;
                                    break;
                                case f32.Length when f32.Is(qHash, value):
                                    quantType = VectorSetQuantization.None;
                                    break;
                                case int8.Length when int8.Is(qHash, value):
                                    quantType = VectorSetQuantization.Int8;
                                    break;
                                default:
                                    quantTypeRaw = value.GetString();
                                    quantType = VectorSetQuantization.Unknown;
                                    break;
                            }

                            break;
                        case hnsw_max_node_uid.Length
                            when hnsw_max_node_uid.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            hnswMaxNodeUid = i64;
                            break;
                    }
                }

                SetResult(
                    message,
                    new VectorSetInfo(quantType, quantTypeRaw, vectorDim, resultSize, maxLevel, vsetUid, hnswMaxNodeUid));
                return true;
            }

            return false;
        }

#pragma warning disable CS8981, SA1134, SA1300, SA1303, SA1502
        // ReSharper disable InconsistentNaming - to better represent expected literals
        // ReSharper disable IdentifierTypo
        [FastHash] public static partial class bin { }
        [FastHash] public static partial class f32 { }
        [FastHash] public static partial class int8 { }
        [FastHash] public static partial class size { }
        [FastHash] public static partial class vset_uid { }
        [FastHash] public static partial class max_level { }
        [FastHash] public static partial class quant_type { }
        [FastHash] public static partial class vector_dim { }
        [FastHash] public static partial class hnsw_max_node_uid { }
        // ReSharper restore InconsistentNaming
        // ReSharper restore IdentifierTypo
#pragma warning restore CS8981, SA1134, SA1300, SA1303, SA1502
    }
}
