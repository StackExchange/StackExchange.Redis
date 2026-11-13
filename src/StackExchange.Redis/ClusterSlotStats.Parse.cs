using System.Collections.Generic;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

public sealed partial class ClusterSlotStatsResult
{
    /// <summary>Reads a <c>CLUSTER SLOT-STATS</c> reply on the RESP context.</summary>
    internal sealed class Handler : IRespHandler<ClusterSlotStatsResult?>
    {
        internal static readonly Handler Instance = new();

        public ClusterSlotStatsResult? Parse(ref RespReader reader)
        {
            if (reader.IsNull) return null;
            if (!reader.IsAggregate) throw new RespException("Unexpected CLUSTER SLOT-STATS reply.");

            // the reply is an array of [slot, metric-map]; parse leniently, dropping anything
            // malformed rather than failing the whole reply
            List<ClusterSlotStatsEntry>? entries = null;
            var slots = reader.AggregateChildren();
            while (slots.MoveNext())
            {
                if (TryParseEntry(ref slots.Value, out var entry))
                {
                    (entries ??= new List<ClusterSlotStatsEntry>()).Add(entry);
                }
            }

            return new ClusterSlotStatsResult(AsReadOnly(entries));
        }
    }

    private static bool TryParseEntry(ref RespReader reader, out ClusterSlotStatsEntry entry)
    {
        entry = null!;
        if (!reader.IsAggregate || reader.IsNull) return false;

        var children = reader.AggregateChildren();
        if (!children.MoveNext() || !children.Value.TryReadInt64(out var slot)) return false;
        if (slot < SlotRange.MinSlot || slot > SlotRange.MaxSlot) return false;
        if (!children.MoveNext() || !children.Value.IsAggregate || children.Value.IsNull) return false;

        long keyCount = 0, cpuMicroseconds = -1, memoryBytes = -1, networkBytesIn = -1, networkBytesOut = -1;
        List<KeyValuePair<string, long>>? metadata = null;

        // the metric map: flat pairs under RESP2, a map under RESP3; read nominally, never
        // positionally - the set is configuration-dependent and documented as extensible
        var fields = children.Value.AggregateChildren();
        while (fields.MoveNext() && fields.Value.IsScalar)
        {
            ClusterSlotStatsField field;
            string? unknownName = null;
            unsafe
            {
                if (!fields.Value.TryParseScalar(&ClusterSlotStatsFieldMetadata.TryParse, out field))
                {
                    field = ClusterSlotStatsField.Unknown;
                }
            }
            if (field == ClusterSlotStatsField.Unknown) unknownName = fields.Value.ReadString();
            if (!fields.MoveNext()) break; // metric name with no value: out of data
            if (!fields.Value.TryReadInt64(out var value)) continue; // a non-numeric metric: skipped

            switch (field)
            {
                case ClusterSlotStatsField.KeyCount:
                    keyCount = value;
                    break;
                case ClusterSlotStatsField.CpuMicroseconds:
                    cpuMicroseconds = value;
                    break;
                case ClusterSlotStatsField.MemoryBytes:
                    memoryBytes = value;
                    break;
                case ClusterSlotStatsField.NetworkBytesIn:
                    networkBytesIn = value;
                    break;
                case ClusterSlotStatsField.NetworkBytesOut:
                    networkBytesOut = value;
                    break;
                default:
                    // a metric added by a future server: preserved, not discarded
                    if (unknownName is not null)
                    {
                        (metadata ??= new List<KeyValuePair<string, long>>()).Add(new(unknownName, value));
                    }
                    break;
            }
        }

        entry = new ClusterSlotStatsEntry(
            (int)slot,
            keyCount,
            cpuMicroseconds,
            memoryBytes,
            networkBytesIn,
            networkBytesOut,
            AsReadOnly(metadata));
        return true;
    }

    // empty collection expressions to an interface target compile to Array.Empty<T>(), so the common case
    // costs nothing; only a genuinely populated list pays for the wrapper
    private static IReadOnlyList<T> AsReadOnly<T>(List<T>? values)
    {
        if (values is null) return [];
        return values.AsReadOnly();
    }
}
