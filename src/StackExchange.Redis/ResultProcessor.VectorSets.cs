// ReSharper disable once CheckNamespace

using System;
using Pipelines.Sockets.Unofficial.Arenas;
using RESPite;
using RESPite.Messages;

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
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (!reader.IsAggregate) return false;

            if (reader.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            var quantType = VectorSetQuantization.Unknown;
            string? quantTypeRaw = null;
            int vectorDim = 0, maxLevel = 0;
            long resultSize = 0, vsetUid = 0, hnswMaxNodeUid = 0;

            // capacity for expected keys and quants
            Span<byte> stackBuffer = stackalloc byte[24];

            // Iterate through key-value pairs
            while (reader.TryMoveNext())
            {
                // Read key
                if (!reader.IsScalar) break;

                var len = reader.ScalarLength();
                var testBytes =
                    (len > stackBuffer.Length | reader.IsNull) ? default :
                    reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackBuffer);

                // Move to value
                if (!reader.TryMoveNext()) break;

                // Skip non-scalar values (future-proofing)
                if (!reader.IsScalar)
                {
                    reader.SkipChildren();
                    continue;
                }

                var hash = testBytes.HashCS(); // this still contains the key, even though we've advanced
                switch (hash)
                {
                    case size.Hash when size.Is(hash, testBytes) && reader.TryReadInt64(out var i64):
                        resultSize = i64;
                        break;
                    case vset_uid.Hash when vset_uid.Is(hash, testBytes) && reader.TryReadInt64(out var i64):
                        vsetUid = i64;
                        break;
                    case max_level.Hash when max_level.Is(hash, testBytes) && reader.TryReadInt64(out var i64):
                        maxLevel = checked((int)i64);
                        break;
                    case vector_dim.Hash when vector_dim.Is(hash, testBytes) && reader.TryReadInt64(out var i64):
                        vectorDim = checked((int)i64);
                        break;
                    case quant_type.Hash when quant_type.Is(hash, testBytes):
                        len = reader.ScalarLength();
                        testBytes = (len > stackBuffer.Length | reader.IsNull) ? default :
                            reader.TryGetSpan(out tmp) ? tmp : reader.Buffer(stackBuffer);

                        hash = testBytes.HashCS();
                        switch (hash)
                        {
                            case bin.Hash when bin.Is(hash, testBytes):
                                quantType = VectorSetQuantization.Binary;
                                break;
                            case f32.Hash when f32.Is(hash, testBytes):
                                quantType = VectorSetQuantization.None;
                                break;
                            case int8.Hash when int8.Is(hash, testBytes):
                                quantType = VectorSetQuantization.Int8;
                                break;
                            default:
                                quantTypeRaw = reader.ReadString(); // don't use testBytes - we might have more bytes
                                quantType = VectorSetQuantization.Unknown;
                                break;
                        }
                        break;
                    case hnsw_max_node_uid.Hash when hnsw_max_node_uid.Is(hash, testBytes) && reader.TryReadInt64(out var i64):
                        hnswMaxNodeUid = i64;
                        break;
                }
            }

            SetResult(
                message,
                new VectorSetInfo(quantType, quantTypeRaw, vectorDim, resultSize, maxLevel, vsetUid, hnswMaxNodeUid));
            return true;
        }

#pragma warning disable CS8981, SA1134, SA1300, SA1303, SA1502
        // ReSharper disable InconsistentNaming - to better represent expected literals
        // ReSharper disable IdentifierTypo
        [FastHash] private static partial class bin { }
        [FastHash] private static partial class f32 { }
        [FastHash] private static partial class int8 { }
        [FastHash] private static partial class size { }
        [FastHash] private static partial class vset_uid { }
        [FastHash] private static partial class max_level { }
        [FastHash] private static partial class quant_type { }
        [FastHash] private static partial class vector_dim { }
        [FastHash] private static partial class hnsw_max_node_uid { }
        // ReSharper restore InconsistentNaming
        // ReSharper restore IdentifierTypo
#pragma warning restore CS8981, SA1134, SA1300, SA1303, SA1502
    }
}
