using Pipelines.Sockets.Unofficial.Arenas;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    // VectorSet result processors
    public static readonly ResultProcessor<Lease<VectorSetLink>?> VectorSetLinksWithScores = new VectorSetLinksWithScoresProcessor();
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
                    var key = iter.Current;
                    if (!iter.MoveNext()) break;
                    var value = iter.Current;

                    var len = key.Payload.Length;
                    var keyHash = key.Payload.Hash64();
                    switch (key.Payload.Length)
                    {
                        case 4 when keyHash == FastHash._4.size && key.IsEqual(FastHash._4.size_u8) && value.TryGetInt64(out var i64):
                            size = i64;
                            break;
                        case 8 when keyHash == FastHash._8.vset_uid && key.IsEqual(FastHash._8.vset_uid_u8) && value.TryGetInt64(out var i64):
                            vsetUid = i64;
                            break;
                        case 9 when keyHash == FastHash._9.max_level && key.IsEqual(FastHash._9.max_level_u8) && value.TryGetInt64(out var i64):
                            maxLevel = checked((int)i64);
                            break;
                        case 10 when keyHash == FastHash._10.vector_dim && key.IsEqual(FastHash._10.vector_dim_u8) && value.TryGetInt64(out var i64):
                            vectorDim = checked((int)i64);
                            break;
                        case 10 when keyHash == FastHash._10.quant_type && key.IsEqual(FastHash._10.quant_type_u8):
                            var qHash = value.Payload.Hash64();
                            switch (value.Payload.Length)
                            {
                                case 3 when qHash == FastHash._3.bin && value.IsEqual(FastHash._3.bin_u8):
                                    quantType = VectorSetQuantization.Binary;
                                    break;
                                case 3 when qHash == FastHash._3.f32 && value.IsEqual(FastHash._3.f32_u8):
                                    quantType = VectorSetQuantization.None;
                                    break;
                                case 4 when qHash == FastHash._4.int8 && value.IsEqual(FastHash._4.int8_u8):
                                    quantType = VectorSetQuantization.Int8;
                                    break;
                                default:
                                    quantTypeRaw = value.GetString();
                                    quantType = VectorSetQuantization.Unknown;
                                    break;
                            }
                            break;
                        case 17 when keyHash == FastHash._17.hnsw_max_node_uid && key.IsEqual(FastHash._17.hnsw_max_node_uid_u8) && value.TryGetInt64(out var i64):
                            hnswMaxNodeUid = i64;
                            break;
                    }
                }

                SetResult(message, new VectorSetInfo(quantType, quantTypeRaw, vectorDim, size, maxLevel, vsetUid, hnswMaxNodeUid));
                return true;
            }
            return false;
        }
    }
}
