using System;
using System.Collections.Generic;
using System.Net;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

public sealed partial class ClusterSlotsResult
{
    internal static readonly ResultProcessor<ClusterSlotsResult?> Processor = new ClusterSlotsResultProcessor();

    private sealed class ClusterSlotsResultProcessor : ResultProcessor<ClusterSlotsResult?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsNull)
            {
                SetResult(message, null);
                return true;
            }
            if (!reader.IsAggregate) return false;

            // the reply is an array of [from, to, primary, replica...]; parse leniently, since bad topology
            // data does silent damage - drop anything malformed rather than failing the whole reply
            List<ClusterSlotAssignment>? assignments = null;
            var ranges = reader.AggregateChildren();
            while (ranges.MoveNext())
            {
                if (TryParseAssignment(ref ranges.Value, out var assignment))
                {
                    (assignments ??= new List<ClusterSlotAssignment>()).Add(assignment);
                }
            }

            SetResult(message, new ClusterSlotsResult(
                assignments?.AsReadOnly() ?? (IList<ClusterSlotAssignment>)Array.Empty<ClusterSlotAssignment>()));
            return true;
        }
    }

    private static bool TryParseAssignment(ref RespReader reader, out ClusterSlotAssignment assignment)
    {
        assignment = null!;
        if (!reader.IsAggregate || reader.IsNull) return false;

        var children = reader.AggregateChildren();
        if (!children.MoveNext() || !children.Value.TryReadInt64(out var from)) return false;
        if (!children.MoveNext() || !children.Value.TryReadInt64(out var to)) return false;
        if (from < SlotRange.MinSlot || to > SlotRange.MaxSlot || to < from) return false;

        ClusterSlotNode? primary = null;
        List<ClusterSlotNode>? replicas = null;
        while (children.MoveNext())
        {
            if (!TryParseNode(ref children.Value, out var node)) continue;
            if (primary is null)
            {
                primary = node;
            }
            else
            {
                (replicas ??= new List<ClusterSlotNode>()).Add(node);
            }
        }

        if (primary is null) return false; // a range with no primary tells us nothing

        assignment = new ClusterSlotAssignment(
            new SlotRange((int)from, (int)to),
            primary,
            replicas?.AsReadOnly() ?? (IList<ClusterSlotNode>)Array.Empty<ClusterSlotNode>());
        return true;
    }

    private static bool TryParseNode(ref RespReader reader, out ClusterSlotNode node)
    {
        node = null!;
        if (!reader.IsAggregate || reader.IsNull) return false;

        var children = reader.AggregateChildren();

        // element 0 is positionally "the endpoint"; its content is whichever form the answering node
        // prefers, so it may be a hostname, an empty string, "?", or the RESP null - never assume an address
        if (!children.MoveNext()) return false;
        string? announced = children.Value.IsNull ? null : children.Value.ReadString();

        if (!children.MoveNext() || !children.Value.TryReadInt64(out var port)) return false;

        // the node id arrived in 4.0 and the metadata map in 7.0, so both are absent on older servers
        string? nodeId = children.MoveNext() ? children.Value.ReadString() : null;

        IList<KeyValuePair<string, string?>> metadata = Array.Empty<KeyValuePair<string, string?>>();
        if (children.MoveNext() && children.Value.IsAggregate && !children.Value.IsNull)
        {
            metadata = ReadMetadata(ref children.Value);
        }

        node = new ClusterSlotNode(announced, (int)port, nodeId, ResolveEndPoint(announced, port), metadata);
        return true;
    }

    // walked pairwise rather than by declared length, since a map reports pairs while an array (RESP2)
    // reports elements, and we do not need to care which we were given
    private static IList<KeyValuePair<string, string?>> ReadMetadata(ref RespReader reader)
    {
        List<KeyValuePair<string, string?>>? metadata = null;
        var children = reader.AggregateChildren();
        while (children.MoveNext())
        {
            var key = children.Value.ReadString();
            if (!children.MoveNext()) break; // odd count: ignore the dangling key
            var value = children.Value.IsNull ? null : children.Value.ReadString();
            if (!string.IsNullOrEmpty(key))
            {
                (metadata ??= new List<KeyValuePair<string, string?>>()).Add(new(key!, value));
            }
        }
        return metadata?.AsReadOnly() ?? (IList<KeyValuePair<string, string?>>)Array.Empty<KeyValuePair<string, string?>>();
    }

    private static EndPoint? ResolveEndPoint(string? announced, long port)
    {
        // null and "" both mean "no endpoint reported"; "?" means an explicitly unknown node. None of the
        // three can be turned into an endpoint here - substituting the connection's own address is a
        // decision for the caller, and is outright wrong for "?"
        if (announced is null or "" or "?") return null;
        if (port is < 1 or > ushort.MaxValue) return null;

        return Format.TryParseEndPoint(announced, port.ToString(), out var endpoint) ? endpoint : null;
    }
}
