using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis;

public partial interface IServer
{
    /// <summary>
    /// Obtains per-slot usage statistics from <c>CLUSTER SLOT-STATS SLOTSRANGE</c>, for the slots in an
    /// inclusive range that are assigned to the answering node's shard, in ascending slot order.
    /// </summary>
    /// <param name="startSlot">The first slot of the range (inclusive).</param>
    /// <param name="endSlot">The last slot of the range (inclusive); must not be less than <paramref name="startSlot"/>.</param>
    /// <param name="flags">The command flags to use.</param>
    /// <remarks>
    /// Cluster-only: a non-cluster server answers with an error. Each node reports only the slots its own
    /// shard serves, so slots in the range owned elsewhere are simply absent - ask each node to see the whole
    /// cluster. <c>key-count</c> is always collected; the other metrics appear only when enabled via the
    /// <c>cluster-slot-stats-enabled</c> server configuration - see <see cref="ClusterSlotStatsEntry"/>.
    /// </remarks>
    [Experimental(Experiments.ClusterSlotStats, UrlFormat = Experiments.UrlFormat)]
    ClusterSlotStatsResult? ClusterSlotStatsBySlotRange(int startSlot, int endSlot, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="ClusterSlotStatsBySlotRange(int, int, CommandFlags)"/>
    [Experimental(Experiments.ClusterSlotStats, UrlFormat = Experiments.UrlFormat)]
    Task<ClusterSlotStatsResult?> ClusterSlotStatsBySlotRangeAsync(int startSlot, int endSlot, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Obtains per-slot usage statistics from <c>CLUSTER SLOT-STATS ORDERBY</c>: the answering node's shard's
    /// slots, sorted by a metric (ties broken by ascending slot number).
    /// </summary>
    /// <param name="metric">The metric to sort by. Sorting by a metric whose collection is not enabled on the
    /// server (via <c>cluster-slot-stats-enabled</c>) is refused by the server; <see cref="ClusterSlotStatsMetric.KeyCount"/>
    /// is always available.</param>
    /// <param name="limit">The maximum number of slots to report; zero (the default) reports them all.</param>
    /// <param name="order">The sort direction; the server (and this) defaults to <see cref="Order.Descending"/>,
    /// which puts the busiest slots first.</param>
    /// <param name="flags">The command flags to use.</param>
    /// <remarks><inheritdoc cref="ClusterSlotStatsBySlotRange(int, int, CommandFlags)" path="/remarks"/></remarks>
    [Experimental(Experiments.ClusterSlotStats, UrlFormat = Experiments.UrlFormat)]
    ClusterSlotStatsResult? ClusterSlotStatsOrderBy(ClusterSlotStatsMetric metric, long limit = 0, Order order = Order.Descending, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="ClusterSlotStatsOrderBy(ClusterSlotStatsMetric, long, Order, CommandFlags)"/>
    [Experimental(Experiments.ClusterSlotStats, UrlFormat = Experiments.UrlFormat)]
    Task<ClusterSlotStatsResult?> ClusterSlotStatsOrderByAsync(ClusterSlotStatsMetric metric, long limit = 0, Order order = Order.Descending, CommandFlags flags = CommandFlags.None);
}

/// <summary>
/// A metric tracked per slot by <c>CLUSTER SLOT-STATS</c>.
/// </summary>
[Experimental(Experiments.ClusterSlotStats, UrlFormat = Experiments.UrlFormat)]
public enum ClusterSlotStatsMetric
{
    /// <summary>The number of keys in the slot (<c>key-count</c>); always collected.</summary>
    KeyCount,

    /// <summary>CPU time spent on the slot's keys (<c>cpu-usec</c>); collected when the <c>cpu</c> metric is enabled.</summary>
    CpuTime,

    /// <summary>Memory used by the slot's keys (<c>memory-bytes</c>); collected when the <c>mem</c> metric is
    /// enabled, which additionally requires memory tracking from startup.</summary>
    MemoryBytes,

    /// <summary>Network bytes received for the slot's keys (<c>network-bytes-in</c>); collected when the <c>net</c> metric is enabled.</summary>
    NetworkBytesIn,

    /// <summary>Network bytes sent for the slot's keys (<c>network-bytes-out</c>); collected when the <c>net</c> metric is enabled.</summary>
    NetworkBytesOut,
}

/// <summary>
/// The per-slot usage statistics reported by <c>CLUSTER SLOT-STATS</c>, as described by the node that answered.
/// </summary>
[Experimental(Experiments.ClusterSlotStats, UrlFormat = Experiments.UrlFormat)]
public sealed partial class ClusterSlotStatsResult
{
    internal ClusterSlotStatsResult(IReadOnlyList<ClusterSlotStatsEntry> slots) => Slots = slots;

    /// <summary>
    /// Gets the per-slot entries, in the order the server reported them: ascending slot number for the
    /// <c>SLOTSRANGE</c> form, metric order for the <c>ORDERBY</c> form.
    /// </summary>
    public IReadOnlyList<ClusterSlotStatsEntry> Slots { get; }
}

/// <summary>
/// One slot's usage statistics from <c>CLUSTER SLOT-STATS</c>.
/// </summary>
/// <remarks>
/// Only <see cref="KeyCount"/> is always collected; each other metric is <c>null</c> when the server is not
/// collecting it (the <c>cluster-slot-stats-enabled</c> configuration), which is deliberately distinct from a
/// collected zero.
/// </remarks>
[Experimental(Experiments.ClusterSlotStats, UrlFormat = Experiments.UrlFormat)]
public sealed class ClusterSlotStatsEntry
{
    internal ClusterSlotStatsEntry(
        int slot,
        long keyCount,
        long cpuMicroseconds,
        long memoryBytes,
        long networkBytesIn,
        long networkBytesOut,
        IReadOnlyList<KeyValuePair<string, long>> metadata)
    {
        Slot = slot;
        KeyCount = keyCount;
        CpuMicroseconds = cpuMicroseconds;
        MemoryBytes = memoryBytes < 0 ? null : memoryBytes;
        NetworkBytesIn = networkBytesIn < 0 ? null : networkBytesIn;
        NetworkBytesOut = networkBytesOut < 0 ? null : networkBytesOut;
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the hash slot this entry describes.
    /// </summary>
    public int Slot { get; }

    /// <summary>
    /// Gets the number of keys in the slot.
    /// </summary>
    public long KeyCount { get; }

    internal long CpuMicroseconds { get; } = -1;

    /// <summary>
    /// Gets the CPU time spent serving the slot's keys, or <c>null</c> when the <c>cpu</c> metric is not
    /// being collected.
    /// </summary>
    public TimeSpan? CpuTime => CpuMicroseconds < 0 ? null : HotKeysResult.NonNegativeMicroseconds(CpuMicroseconds);

    /// <summary>
    /// Gets the memory used by the slot's keys, or <c>null</c> when the <c>mem</c> metric is not being
    /// collected.
    /// </summary>
    public long? MemoryBytes { get; }

    /// <summary>
    /// Gets the network bytes received for the slot's keys, or <c>null</c> when the <c>net</c> metric is not
    /// being collected.
    /// </summary>
    public long? NetworkBytesIn { get; }

    /// <summary>
    /// Gets the network bytes sent for the slot's keys, or <c>null</c> when the <c>net</c> metric is not
    /// being collected.
    /// </summary>
    public long? NetworkBytesOut { get; }

    /// <summary>
    /// Gets the metric entries reported for this slot that this library does not recognize. The metric set is
    /// configuration-dependent and has grown before, so these are preserved rather than discarded; the
    /// recognized metrics are surfaced as the typed properties instead of appearing here.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, long>> Metadata { get; }

    /// <inheritdoc/>
    public override string ToString() => $"slot {Slot}: {KeyCount} keys";
}
