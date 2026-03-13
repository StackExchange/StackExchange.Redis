using Pipelines.Sockets.Unofficial.Arenas;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    // VectorSet result processors
    public static readonly ResultProcessor<Lease<VectorSetLink>?> VectorSetLinksWithScores =
        new VectorSetLinksWithScoresProcessor();

    public static readonly ResultProcessor<Lease<RedisValue>?> VectorSetLinks = new VectorSetLinksProcessor();

    public static readonly ResultProcessor<Lease<RedisValue>?> LeaseRedisValue = new LeaseRedisValueProcessor();

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

    private sealed class LeaseRedisValueProcessor : LeaseProcessor<RedisValue>
    {
        protected override bool TryParse(in RawResult raw, out RedisValue parsed)
        {
            parsed = raw.AsRedisValue();
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
                    if (!iter.Current.TryParse(VectorSetInfoFieldMetadata.TryParse, out VectorSetInfoField field))
                        field = VectorSetInfoField.Unknown;

                    if (!iter.MoveNext()) break;
                    ref readonly RawResult value = ref iter.Current;

                    switch (field)
                    {
                        case VectorSetInfoField.Size when value.TryGetInt64(out var i64):
                            resultSize = i64;
                            break;
                        case VectorSetInfoField.VsetUid when value.TryGetInt64(out var i64):
                            vsetUid = i64;
                            break;
                        case VectorSetInfoField.MaxLevel when value.TryGetInt64(out var i64):
                            maxLevel = checked((int)i64);
                            break;
                        case VectorSetInfoField.VectorDim when value.TryGetInt64(out var i64):
                            vectorDim = checked((int)i64);
                            break;
                        case VectorSetInfoField.QuantType
                            when value.TryParse(VectorSetQuantizationMetadata.TryParse, out VectorSetQuantization quantTypeValue)
                                && quantTypeValue is not VectorSetQuantization.Unknown:
                            quantType = quantTypeValue;
                            break;
                        case VectorSetInfoField.QuantType:
                            quantTypeRaw = value.GetString();
                            quantType = VectorSetQuantization.Unknown;
                            break;
                        case VectorSetInfoField.HnswMaxNodeUid when value.TryGetInt64(out var i64):
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
    }
}
