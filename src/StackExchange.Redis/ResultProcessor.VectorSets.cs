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
            // the shape lives on the type it produces, so the interpolated surface's handler reads the
            // identical reply the identical way; see VectorSetInfo.Resp.cs
            if (!reader.IsAggregate) return false;

            // fully qualified: the ResultProcessor.VectorSetInfo FIELD shadows the type name in here
            SetResult(message, global::StackExchange.Redis.VectorSetInfo.TryRead(ref reader, out var info) ? info : null);
            return true;
        }
    }
}
