using System;
using System.Collections.Generic;
using System.Net;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

public sealed partial class ClusterSlotsResult
{
    internal static readonly ResultProcessor<ClusterSlotsResult?> Processor = new ClusterSlotsResultProcessor();

    /// <summary>
    /// As <see cref="Processor"/>, but also records the result against the server that answered - the
    /// autoconfigure path needs the side effect, callers of <c>CLUSTER SLOTS</c> do not.
    /// </summary>
    internal static readonly ResultProcessor<ClusterSlotsResult?> AutoConfigureProcessor = new ClusterSlotsResultProcessor(autoConfigure: true);

    private sealed class ClusterSlotsResultProcessor(bool autoConfigure = false) : ResultProcessor<ClusterSlotsResult?>
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

            var result = new ClusterSlotsResult(
                AsReadOnly(assignments));

            if (autoConfigure)
            {
                connection.BridgeCouldBeNull?.ServerEndPoint?.SetClusterSlots(result);
            }

            SetResult(message, result);
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
            AsReadOnly(replicas));
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

        IReadOnlyList<KeyValuePair<string, string?>> metadata = [];
        string? ip = null, hostname = null;
        if (children.MoveNext() && children.Value.IsAggregate && !children.Value.IsNull)
        {
            metadata = ReadMetadata(ref children.Value, out ip, out hostname);
        }

        node = new ClusterSlotNode(announced, (int)port, nodeId, ResolveEndPoint(announced, port), ip, hostname, metadata);
        return true;
    }

    // walked pairwise rather than by declared length, since a map reports pairs while an array (RESP2)
    // reports elements, and we do not need to care which we were given.
    //
    // Keys are matched against the known set over the raw bytes, so a recognized key costs no string at all -
    // and since ranges repeat a node whenever its slot ownership is not contiguous, that is one allocation
    // avoided per key per range rather than per node. Only unrecognized keys are materialized, into the
    // collection that exists to preserve them
    private static unsafe IReadOnlyList<KeyValuePair<string, string?>> ReadMetadata(
        ref RespReader reader,
        out string? ip,
        out string? hostname)
    {
        ip = hostname = null;
        List<KeyValuePair<string, string?>>? metadata = null;
        var children = reader.AggregateChildren();
        while (children.MoveNext())
        {
            if (!children.Value.TryParseScalar(&ClusterSlotMetadataKeyMetadata.TryParse, out ClusterSlotMetadataKey key))
            {
                key = ClusterSlotMetadataKey.Unknown;
            }

            // the key is still in the reader until we move on, so an unknown one can be read then
            var unknownKey = key == ClusterSlotMetadataKey.Unknown ? children.Value.ReadString() : null;

            if (!children.MoveNext()) break; // odd count: ignore the dangling key
            var value = children.Value.IsNull ? null : children.Value.ReadString();

            switch (key)
            {
                case ClusterSlotMetadataKey.Ip:
                    ip = value;
                    break;
                case ClusterSlotMetadataKey.Hostname:
                    hostname = value;
                    break;
                default:
                    if (!string.IsNullOrEmpty(unknownKey))
                    {
                        (metadata ??= new List<KeyValuePair<string, string?>>()).Add(new(unknownKey!, value));
                    }
                    break;
            }
        }
        return AsReadOnly(metadata);
    }

    // empty collection expressions to an interface target compile to Array.Empty<T>(), so the common case
    // costs nothing; only a genuinely populated list pays for the wrapper
    private static IReadOnlyList<T> AsReadOnly<T>(List<T>? values)
    {
        if (values is null) return [];
        return values.AsReadOnly();
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
