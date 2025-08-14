using Pipelines.Sockets.Unofficial.Arenas;
using FH = global::StackExchange.Redis.FastHash;

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

    private sealed class VectorSetInfoProcessor : ResultProcessor<VectorSetInfo?>
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
                long size = 0, vsetUid = 0, hnswMaxNodeUid = 0;
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
                        case FH.size.Length when FH.size.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            size = i64;
                            break;
                        case FH.vset_uid.Length when FH.vset_uid.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            vsetUid = i64;
                            break;
                        case FH.max_level.Length when FH.max_level.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            maxLevel = checked((int)i64);
                            break;
                        case FH.vector_dim.Length
                            when FH.vector_dim.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            vectorDim = checked((int)i64);
                            break;
                        case FH.quant_type.Length when FH.quant_type.Is(keyHash, key):
                            var qHash = value.Payload.Hash64();
                            switch (value.Payload.Length)
                            {
                                case FH.bin.Length when FH.bin.Is(qHash, value):
                                    quantType = VectorSetQuantization.Binary;
                                    break;
                                case FH.f32.Length when FH.f32.Is(qHash, value):
                                    quantType = VectorSetQuantization.None;
                                    break;
                                case FH.int8.Length when FH.int8.Is(qHash, value):
                                    quantType = VectorSetQuantization.Int8;
                                    break;
                                default:
                                    quantTypeRaw = value.GetString();
                                    quantType = VectorSetQuantization.Unknown;
                                    break;
                            }

                            break;
                        case FH.hnsw_max_node_uid.Length
                            when FH.hnsw_max_node_uid.Is(keyHash, key) && value.TryGetInt64(out var i64):
                            hnswMaxNodeUid = i64;
                            break;
                    }
                }

                SetResult(
                    message,
                    new VectorSetInfo(quantType, quantTypeRaw, vectorDim, size, maxLevel, vsetUid, hnswMaxNodeUid));
                return true;
            }

            return false;
        }
    }
}
