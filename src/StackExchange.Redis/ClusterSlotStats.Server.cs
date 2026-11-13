using System;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

internal partial class RedisServer
{
    // CLUSTER SLOT-STATS: a node-local read, as CLUSTER SLOTS is - it reports what the answering node
    // tracks for itself, so it is safe to replay against that node.
    public ClusterSlotStatsResult? ClusterSlotStatsBySlotRange(int startSlot, int endSlot, CommandFlags flags = CommandFlags.None)
        => Wait(SendClusterSlotStatsBySlotRange(startSlot, endSlot, flags));

    public Task<ClusterSlotStatsResult?> ClusterSlotStatsBySlotRangeAsync(int startSlot, int endSlot, CommandFlags flags = CommandFlags.None)
        => SendClusterSlotStatsBySlotRange(startSlot, endSlot, flags).AsTask(asyncState, flags);

    public ClusterSlotStatsResult? ClusterSlotStatsOrderBy(ClusterSlotStatsMetric metric, long limit = 0, Order order = Order.Descending, CommandFlags flags = CommandFlags.None)
        => Wait(SendClusterSlotStatsOrderBy(metric, limit, order, flags));

    public Task<ClusterSlotStatsResult?> ClusterSlotStatsOrderByAsync(ClusterSlotStatsMetric metric, long limit = 0, Order order = Order.Descending, CommandFlags flags = CommandFlags.None)
        => SendClusterSlotStatsOrderBy(metric, limit, order, flags).AsTask(asyncState, flags);

    /// <summary>Sends <c>CLUSTER SLOT-STATS SLOTSRANGE start end</c>.</summary>
    private ValueTask<ClusterSlotStatsResult?> SendClusterSlotStatsBySlotRange(int startSlot, int endSlot, CommandFlags flags)
        => Context.Raw.SendAsync(
            $"{RedisCommand.CLUSTER}{ClusterSlotStatsSlotsRangeOperand.Create(startSlot, endSlot)}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            ClusterSlotStatsResult.Handler.Instance);

    /// <summary>Sends <c>CLUSTER SLOT-STATS ORDERBY metric [LIMIT n] [ASC]</c>.</summary>
    private ValueTask<ClusterSlotStatsResult?> SendClusterSlotStatsOrderBy(ClusterSlotStatsMetric metric, long limit, Order order, CommandFlags flags)
        => Context.Raw.SendAsync(
            $"{RedisCommand.CLUSTER}{ClusterSlotStatsOrderByOperand.Create(metric, limit, order)}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            ClusterSlotStatsResult.Handler.Instance);

    /// <summary>The arguments of <c>CLUSTER SLOT-STATS SLOTSRANGE</c>: an inclusive slot range.</summary>
    internal readonly struct ClusterSlotStatsSlotsRangeOperand(int startSlot, int endSlot) : IRespArgument
    {
        /// <summary>Validates and builds; the operand itself writes blindly, so the checking happens once, here.</summary>
        internal static ClusterSlotStatsSlotsRangeOperand Create(int startSlot, int endSlot)
        {
            if (startSlot < SlotRange.MinSlot || startSlot > SlotRange.MaxSlot) throw new ArgumentOutOfRangeException(nameof(startSlot));
            if (endSlot < startSlot || endSlot > SlotRange.MaxSlot) throw new ArgumentOutOfRangeException(nameof(endSlot));
            return new(startSlot, endSlot);
        }

        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            handler.AppendFormatted(RespLiterals.SlotStats);
            handler.AppendFormatted(RespLiterals.SlotsRange);
            handler.AppendFormatted((RedisValue)startSlot);
            handler.AppendFormatted((RedisValue)endSlot);
        }
    }

    /// <summary>
    /// The arguments of <c>CLUSTER SLOT-STATS ORDERBY</c>: the metric (as the server spells it, lower-case
    /// and hyphenated), then <c>LIMIT</c> and <c>ASC</c> only when they say something the server default
    /// (all slots, descending) does not.
    /// </summary>
    internal readonly struct ClusterSlotStatsOrderByOperand(ClusterSlotStatsMetric metric, long limit, Order order) : IRespArgument
    {
        /// <inheritdoc cref="ClusterSlotStatsSlotsRangeOperand.Create"/>
        internal static ClusterSlotStatsOrderByOperand Create(ClusterSlotStatsMetric metric, long limit, Order order)
        {
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
            if (metric is < ClusterSlotStatsMetric.KeyCount or > ClusterSlotStatsMetric.NetworkBytesOut) throw new ArgumentOutOfRangeException(nameof(metric));
            return new(metric, limit, order);
        }

        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            handler.AppendFormatted(RespLiterals.SlotStats);
            handler.AppendFormatted(RespLiterals.OrderBy);
            handler.AppendFormatted(metric switch
            {
                ClusterSlotStatsMetric.CpuTime => RespLiterals.CpuUsecMetric,
                ClusterSlotStatsMetric.MemoryBytes => RespLiterals.MemoryBytesMetric,
                ClusterSlotStatsMetric.NetworkBytesIn => RespLiterals.NetworkBytesInMetric,
                ClusterSlotStatsMetric.NetworkBytesOut => RespLiterals.NetworkBytesOutMetric,
                _ => RespLiterals.KeyCountMetric,
            });

            if (limit > 0)
            {
                handler.AppendFormatted(RespLiterals.Limit);
                handler.AppendFormatted((RedisValue)limit);
            }

            if (order != Order.Descending) handler.AppendFormatted(RespLiterals.Asc);
        }
    }
}
