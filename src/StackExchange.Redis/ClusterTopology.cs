using System;
using System.Collections.Generic;
using System.Net;

namespace StackExchange.Redis;

/// <summary>
/// An internal view of the cluster keyed on node-id, built from <c>CLUSTER SLOTS</c>.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the node-id deliberately: it is the only identity in a reply that does not depend on which node
/// answered. A node's *names* (address, hostname) vary by the answering node's preferred endpoint type, so
/// keying on any of them means two replies describing one node can produce two entries. Keyed on the id,
/// they merge, and the names become attributes.
/// </para>
/// <para>
/// Currently populated in parallel with the <c>CLUSTER NODES</c> view that still drives routing, so that the
/// two can be compared before anything depends on this one.
/// </para>
/// </remarks>
internal sealed class ClusterTopology
{
    private readonly Dictionary<string, ClusterTopologyNode> _byNodeId;

    private ClusterTopology(Dictionary<string, ClusterTopologyNode> byNodeId) => _byNodeId = byNodeId;

    public IReadOnlyCollection<ClusterTopologyNode> Nodes => _byNodeId.Values;

    public ClusterTopologyNode? this[string nodeId] => _byNodeId.TryGetValue(nodeId, out var node) ? node : null;

    /// <summary>
    /// Builds the view, or <c>null</c> if the reply carried nothing usable. Nodes without an id are skipped:
    /// pre-4.0 servers do not report one, and without it we cannot merge identities reliably.
    /// </summary>
    public static ClusterTopology? From(ClusterSlotsResult? slots)
    {
        if (slots is null || slots.Assignments.Count == 0) return null;

        Dictionary<string, ClusterTopologyNode>? byNodeId = null;
        foreach (var assignment in slots.Assignments)
        {
            Add(ref byNodeId, assignment.Primary, assignment.Slots, primaryNodeId: null);
            foreach (var replica in assignment.Replicas)
            {
                Add(ref byNodeId, replica, assignment.Slots, primaryNodeId: assignment.Primary.NodeId);
            }
        }

        return byNodeId is null ? null : new ClusterTopology(byNodeId);

        static void Add(
            ref Dictionary<string, ClusterTopologyNode>? byNodeId,
            ClusterSlotNode node,
            SlotRange slots,
            string? primaryNodeId)
        {
            if (string.IsNullOrEmpty(node.NodeId)) return;

            byNodeId ??= new Dictionary<string, ClusterTopologyNode>(StringComparer.Ordinal);
            if (!byNodeId.TryGetValue(node.NodeId!, out var existing))
            {
                byNodeId.Add(node.NodeId!, existing = new ClusterTopologyNode(node, primaryNodeId));
            }
            else
            {
                // non-contiguous ownership repeats a node across ranges, so merging must be idempotent
                existing.Merge(node);
            }

            if (primaryNodeId is null) existing.AddSlots(slots);
        }
    }
}

/// <summary>
/// One node in a <see cref="ClusterTopology"/>: an identity, the names it is known by, and what it serves.
/// </summary>
internal sealed class ClusterTopologyNode
{
    private List<SlotRange>? _slots;
    private List<EndPoint>? _identities;

    internal ClusterTopologyNode(ClusterSlotNode node, string? primaryNodeId)
    {
        NodeId = node.NodeId!;
        Port = node.Port;
        PrimaryNodeId = primaryNodeId;
        Merge(node);
    }

    public string NodeId { get; }

    public int Port { get; }

    /// <summary>The node-id of this node's primary, if it was reported as a replica.</summary>
    public string? PrimaryNodeId { get; }

    public bool IsReplica => PrimaryNodeId is not null;

    /// <summary>The address this node is known by, if any reply has supplied one.</summary>
    public string? Ip { get; private set; }

    /// <summary>The hostname this node is known by, if any reply has supplied one.</summary>
    public string? Hostname { get; private set; }

    /// <summary>
    /// Every endpoint this node is known to answer to. More than one means the same node arrived under
    /// different names, which is exactly the case that must not produce duplicate <c>ServerEndPoint</c>s.
    /// </summary>
    public IReadOnlyList<EndPoint> Identities => _identities ?? [];

    /// <summary>The slots this node serves as primary; empty for a replica.</summary>
    public IReadOnlyList<SlotRange> Slots => _slots ?? [];

    /// <summary>
    /// Fold in another sighting of this node. Names accumulate rather than overwrite: a reply that renders
    /// the node by address supplies the hostname as metadata and vice versa, so successive replies from
    /// differently-configured nodes each contribute a piece.
    /// </summary>
    internal void Merge(ClusterSlotNode node)
    {
        // the identity set is the union of the primary field and the metadata, and the metadata is
        // documented as the *complement* of the primary - so taking only the metadata loses whichever form
        // the answering node happened to prefer. The primary field must be classified by its content, since
        // positionally it is just "the endpoint" and may hold either form
        if (NullIfEmpty(node.AnnouncedEndpoint) is { } announced && announced != "?")
        {
            if (IPAddress.TryParse(announced, out _))
            {
                Ip ??= announced;
            }
            else
            {
                Hostname ??= announced;
            }
        }

        Ip ??= NullIfEmpty(node.Ip);
        Hostname ??= NullIfEmpty(node.Hostname);

        // the announced endpoint is only an identity if it was usable at all, which excludes the
        // "?" / empty / absent placeholders
        AddIdentity(node.EndPoint);
        if (Ip is not null) AddIdentity(new IPEndPointOrDns(Ip, Port).ToEndPoint());
        if (Hostname is not null) AddIdentity(new DnsEndPoint(Hostname, Port));

        static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }

    internal void AddSlots(SlotRange slots)
    {
        _slots ??= new List<SlotRange>();
        if (!_slots.Contains(slots)) _slots.Add(slots);
    }

    private void AddIdentity(EndPoint? endpoint)
    {
        if (endpoint is null) return;
        _identities ??= new List<EndPoint>();
        if (!_identities.Contains(endpoint)) _identities.Add(endpoint);
    }

    public override string ToString() => $"{NodeId} {Ip ?? Hostname ?? "(unnamed)"}:{Port}{(IsReplica ? " (replica)" : "")}";

    /// <summary>Parses a reported address, falling back to a name if it is not an address after all.</summary>
    private readonly struct IPEndPointOrDns(string host, int port)
    {
        public EndPoint ToEndPoint() =>
            IPAddress.TryParse(host, out var address) ? new IPEndPoint(address, port) : new DnsEndPoint(host, port);
    }
}
