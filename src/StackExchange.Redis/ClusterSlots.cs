using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public partial interface IServer
{
    /// <summary>
    /// Obtains the slot-to-node mapping reported by <c>CLUSTER SLOTS</c>.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    /// <remarks>
    /// This is a different view of the topology to <see cref="ClusterNodes(CommandFlags)"/>, not merely a
    /// different encoding of it: the naming form used for each node is chosen by the node answering the
    /// command, so two servers can describe the same cluster differently. See <see cref="ClusterSlotNode"/>.
    /// </remarks>
    ClusterSlotsResult? ClusterSlots(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Obtains the slot-to-node mapping reported by <c>CLUSTER SLOTS</c>.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    /// <remarks>
    /// This is a different view of the topology to <see cref="ClusterNodesAsync(CommandFlags)"/>, not merely
    /// a different encoding of it: the naming form used for each node is chosen by the node answering the
    /// command, so two servers can describe the same cluster differently. See <see cref="ClusterSlotNode"/>.
    /// </remarks>
    Task<ClusterSlotsResult?> ClusterSlotsAsync(CommandFlags flags = CommandFlags.None);
}

/// <summary>
/// The slot-to-node mapping reported by <c>CLUSTER SLOTS</c>, as described by the server that answered.
/// </summary>
public sealed partial class ClusterSlotsResult
{
    internal ClusterSlotsResult(IReadOnlyList<ClusterSlotAssignment> assignments) => Assignments = assignments;

    /// <summary>
    /// Gets the slot ranges reported, in the order the server reported them. Ranges may repeat a node when
    /// its slot ownership is not contiguous, so this is not a per-node list.
    /// </summary>
    public IReadOnlyList<ClusterSlotAssignment> Assignments { get; }
}

/// <summary>
/// One slot range from <c>CLUSTER SLOTS</c>, and the nodes serving it.
/// </summary>
public sealed class ClusterSlotAssignment
{
    internal ClusterSlotAssignment(SlotRange slots, ClusterSlotNode primary, IReadOnlyList<ClusterSlotNode> replicas)
    {
        Slots = slots;
        Primary = primary;
        Replicas = replicas;
    }

    /// <summary>
    /// Gets the range of slots covered by this assignment.
    /// </summary>
    public SlotRange Slots { get; }

    /// <summary>
    /// Gets the node serving this range as primary.
    /// </summary>
    public ClusterSlotNode Primary { get; }

    /// <summary>
    /// Gets the nodes replicating this range, if any.
    /// </summary>
    public IReadOnlyList<ClusterSlotNode> Replicas { get; }
}

/// <summary>
/// A node as described by <c>CLUSTER SLOTS</c>.
/// </summary>
/// <remarks>
/// The endpoint field is positional rather than typed: its *content* is whichever naming form the answering
/// node prefers, so it may hold an address, a hostname, or one of the documented placeholders. Prefer
/// <see cref="EndPoint"/>, which is populated only when the reported value is usable, and consult
/// <see cref="Ip"/>/<see cref="Hostname"/> for the complementary form the server supplies alongside it.
/// </remarks>
public sealed class ClusterSlotNode
{
    internal ClusterSlotNode(
        string? announcedEndpoint,
        int port,
        string? nodeId,
        EndPoint? endPoint,
        string? ip,
        string? hostname,
        IReadOnlyList<KeyValuePair<string, string?>> metadata)
    {
        AnnouncedEndpoint = announcedEndpoint;
        Port = port;
        NodeId = nodeId;
        EndPoint = endPoint;
        Ip = ip;
        Hostname = hostname;
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the endpoint exactly as reported, which may be an address, a hostname, an empty string, or
    /// <c>"?"</c>; <c>null</c> when the server reported no endpoint at all.
    /// </summary>
    /// <remarks>
    /// The three unusable values do not mean the same thing. <c>null</c> means the server does not know this
    /// node's address, typically because it is behind a load balancer: connect to the endpoint the command
    /// was sent to, using <see cref="Port"/>. An empty string means the node does not know its own address,
    /// and may be treated the same way. <c>"?"</c> means hostnames are preferred but this node announced
    /// none - it identifies an *unknown* node, so unlike the other two it must **not** be assumed to be the
    /// node that answered.
    /// </remarks>
    public string? AnnouncedEndpoint { get; }

    /// <summary>
    /// Gets the port reported for this node.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the node identifier, or <c>null</c> for servers old enough not to report one.
    /// </summary>
    /// <remarks>
    /// This is the only identity here that does not depend on which node answered, so it is the reliable key
    /// when reconciling nodes across replies.
    /// </remarks>
    public string? NodeId { get; }

    /// <summary>
    /// Gets the endpoint for this node, or <c>null</c> when the reported value is not usable as one - see
    /// <see cref="AnnouncedEndpoint"/> for what that means and how to recover.
    /// </summary>
    public EndPoint? EndPoint { get; }

    /// <summary>
    /// Gets the address of this node when the server supplies it as metadata, which it does whenever the
    /// answering node prefers some other naming form.
    /// </summary>
    public string? Ip { get; }

    /// <summary>
    /// Gets the announced hostname of this node when the server supplies it as metadata, which it does when
    /// the node has one and the answering node prefers some other naming form.
    /// </summary>
    public string? Hostname { get; }

    /// <summary>
    /// Gets the metadata entries reported for this node that this library does not recognize. The set is
    /// documented as extensible, so these are preserved rather than discarded; the recognized keys are
    /// surfaced as <see cref="Ip"/> and <see cref="Hostname"/> instead of appearing here, which also means a
    /// recognized key costs no allocation.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Metadata { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{AnnouncedEndpoint ?? "(unknown)"}:{Port}";
}
