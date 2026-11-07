using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class RedisServer
{
    public ClusterSlotStatsResult? ClusterSlotStatsBySlotRange(int startSlot, int endSlot, CommandFlags flags = CommandFlags.None)
        => ExecuteSync(GetClusterSlotStatsBySlotRangeMessage(startSlot, endSlot, flags), ClusterSlotStatsResult.Processor, server);

    public Task<ClusterSlotStatsResult?> ClusterSlotStatsBySlotRangeAsync(int startSlot, int endSlot, CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(GetClusterSlotStatsBySlotRangeMessage(startSlot, endSlot, flags), ClusterSlotStatsResult.Processor, server);

    public ClusterSlotStatsResult? ClusterSlotStatsOrderBy(ClusterSlotStatsMetric metric, long limit = 0, Order order = Order.Descending, CommandFlags flags = CommandFlags.None)
        => ExecuteSync(GetClusterSlotStatsOrderByMessage(metric, limit, order, flags), ClusterSlotStatsResult.Processor, server);

    public Task<ClusterSlotStatsResult?> ClusterSlotStatsOrderByAsync(ClusterSlotStatsMetric metric, long limit = 0, Order order = Order.Descending, CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(GetClusterSlotStatsOrderByMessage(metric, limit, order, flags), ClusterSlotStatsResult.Processor, server);

    /// <summary>
    /// Builds <c>CLUSTER SLOT-STATS SLOTSRANGE start end</c>. A node-local read, as
    /// <see cref="GetClusterSlotsMessage"/>: it reports what the answering node tracks for itself, so it is
    /// safe to replay against that node.
    /// </summary>
    internal static Message GetClusterSlotStatsBySlotRangeMessage(int startSlot, int endSlot, CommandFlags flags)
    {
        if (startSlot < SlotRange.MinSlot || startSlot > SlotRange.MaxSlot) throw new ArgumentOutOfRangeException(nameof(startSlot));
        if (endSlot < startSlot || endSlot > SlotRange.MaxSlot) throw new ArgumentOutOfRangeException(nameof(endSlot));
        return Message.Create(
            -1,
            flags.WithCategory(NodeLocalRead),
            RedisCommand.CLUSTER,
            new RedisValue[] { RedisLiterals.SLOT_STATS, RedisLiterals.SLOTSRANGE, startSlot, endSlot });
    }

    /// <summary>
    /// Builds <c>CLUSTER SLOT-STATS ORDERBY metric [LIMIT n] [ASC]</c>; the server defaults (all slots,
    /// descending) are expressed by omission.
    /// </summary>
    internal static Message GetClusterSlotStatsOrderByMessage(ClusterSlotStatsMetric metric, long limit, Order order, CommandFlags flags)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        var metricValue = metric switch
        {
            ClusterSlotStatsMetric.KeyCount => KeyCountMetric,
            ClusterSlotStatsMetric.CpuTime => CpuMetric,
            ClusterSlotStatsMetric.MemoryBytes => MemoryMetric,
            ClusterSlotStatsMetric.NetworkBytesIn => NetworkInMetric,
            ClusterSlotStatsMetric.NetworkBytesOut => NetworkOutMetric,
            _ => throw new ArgumentOutOfRangeException(nameof(metric)),
        };
        flags = flags.WithCategory(NodeLocalRead);
        RedisValue[] values = (limit > 0, order) switch
        {
            (false, Order.Descending) => [RedisLiterals.SLOT_STATS, RedisLiterals.ORDERBY, metricValue],
            (false, _) => [RedisLiterals.SLOT_STATS, RedisLiterals.ORDERBY, metricValue, RedisLiterals.ASC],
            (true, Order.Descending) => [RedisLiterals.SLOT_STATS, RedisLiterals.ORDERBY, metricValue, RedisLiterals.LIMIT, limit],
            (true, _) => [RedisLiterals.SLOT_STATS, RedisLiterals.ORDERBY, metricValue, RedisLiterals.LIMIT, limit, RedisLiterals.ASC],
        };
        return Message.Create(-1, flags, RedisCommand.CLUSTER, values);
    }

    // the ORDERBY metric tokens, as the server spells them (lower-case, hyphenated - unlike the
    // upper-case option literals in RedisLiterals)
    private static readonly RedisValue
        KeyCountMetric = RedisValue.FromRaw("key-count"u8),
        CpuMetric = RedisValue.FromRaw("cpu-usec"u8),
        MemoryMetric = RedisValue.FromRaw("memory-bytes"u8),
        NetworkInMetric = RedisValue.FromRaw("network-bytes-in"u8),
        NetworkOutMetric = RedisValue.FromRaw("network-bytes-out"u8);
}
