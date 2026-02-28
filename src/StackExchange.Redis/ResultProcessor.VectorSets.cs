// ReSharper disable once CheckNamespace

using System;
using RESPite;
using RESPite.Messages;

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
        protected override long GetArrayLength(in RespReader reader) => reader.AggregateLength() / 2;

        protected override bool TryReadOne(ref RespReader reader, out VectorSetLink value)
        {
            if (!reader.IsScalar)
            {
                value = default;
                return false;
            }

            var member = reader.ReadRedisValue();
            if (!reader.TryMoveNext() || !reader.IsScalar || !reader.TryReadDouble(out var score))
            {
                value = default;
                return false;
            }

            value = new VectorSetLink(member, score);
            return true;
        }
    }

    private sealed class VectorSetLinksProcessor : FlattenedLeaseProcessor<RedisValue>
    {
        protected override bool TryReadOne(ref RespReader reader, out RedisValue value)
        {
            if (!reader.IsScalar)
            {
                value = default;
                return false;
            }

            value = reader.ReadRedisValue();
            return true;
        }
    }

    private sealed class LeaseRedisValueProcessor : LeaseProcessor<RedisValue>
    {
        protected override RedisValue TryParse(ref RespReader reader) => reader.ReadRedisValue();
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

            // Iterate through key-value pairs
            while (reader.TryMoveNext())
            {
                // Read key
                if (!reader.IsScalar) break;

                VectorSetInfoField field;
                unsafe
                {
                    if (!reader.TryParseScalar(&VectorSetInfoFieldMetadata.TryParse, out field))
                    {
                        field = VectorSetInfoField.Unknown;
                    }
                }

                // Move to value
                if (!reader.TryMoveNext()) break;

                // Skip non-scalar values (future-proofing)
                if (!reader.IsScalar)
                {
                    reader.SkipChildren();
                    continue;
                }

                switch (field)
                {
                    case VectorSetInfoField.Size when reader.TryReadInt64(out var i64):
                        resultSize = i64;
                        break;
                    case VectorSetInfoField.VsetUid when reader.TryReadInt64(out var i64):
                        vsetUid = i64;
                        break;
                    case VectorSetInfoField.MaxLevel when reader.TryReadInt64(out var i64):
                        maxLevel = checked((int)i64);
                        break;
                    case VectorSetInfoField.VectorDim when reader.TryReadInt64(out var i64):
                        vectorDim = checked((int)i64);
                        break;
                    case VectorSetInfoField.QuantType when reader.TryParseScalar<VectorSetQuantization>(VectorSetQuantizationMetadata.TryParse, out var quantTypeValue)
                                                           && quantTypeValue is not VectorSetQuantization.Unknown:
                        quantType = quantTypeValue;
                        break;
                    case VectorSetInfoField.QuantType:
                        quantTypeRaw = reader.ReadString();
                        quantType = VectorSetQuantization.Unknown;
                        break;
                    case VectorSetInfoField.HnswMaxNodeUid when reader.TryReadInt64(out var i64):
                        hnswMaxNodeUid = i64;
                        break;
                }
            }

            SetResult(
                message,
                new VectorSetInfo(quantType, quantTypeRaw, vectorDim, resultSize, maxLevel, vsetUid, hnswMaxNodeUid));
            return true;
        }
    }
}
