using System;
using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// What <c>CLUSTER SLOT-STATS</c> puts on the wire, byte for byte - the operands carry the conditional
/// clauses, so the shape is pinned here rather than discovered against a live server. The reply-shape
/// matrix lives in <see cref="ResultProcessorUnitTests.ClusterSlotStats"/>.
/// </summary>
public class ClusterSlotStatsRoundTrip
{
    [Fact]
    public void SlotsRange_WritesTheRange()
        => Assert.Equal(
            "*5|$7|CLUSTER|$10|SLOT-STATS|$10|SLOTSRANGE|$3|100|$3|200|",
            Render(RedisServer.ClusterSlotStatsSlotsRangeOperand.Create(100, 200)));

    [Fact]
    public void OrderBy_Defaults_OmitsLimitAndOrder()
        => Assert.Equal(
            "*4|$7|CLUSTER|$10|SLOT-STATS|$7|ORDERBY|$9|key-count|",
            Render(RedisServer.ClusterSlotStatsOrderByOperand.Create(ClusterSlotStatsMetric.KeyCount, 0, Order.Descending)));

    [Fact]
    public void OrderBy_WithLimit_WritesLimit()
        => Assert.Equal(
            "*6|$7|CLUSTER|$10|SLOT-STATS|$7|ORDERBY|$8|cpu-usec|$5|LIMIT|$1|5|",
            Render(RedisServer.ClusterSlotStatsOrderByOperand.Create(ClusterSlotStatsMetric.CpuTime, 5, Order.Descending)));

    [Fact]
    public void OrderBy_AscendingWithLimit_WritesBoth()
        => Assert.Equal(
            "*7|$7|CLUSTER|$10|SLOT-STATS|$7|ORDERBY|$17|network-bytes-out|$5|LIMIT|$1|3|$3|ASC|",
            Render(RedisServer.ClusterSlotStatsOrderByOperand.Create(ClusterSlotStatsMetric.NetworkBytesOut, 3, Order.Ascending)));

    [Fact]
    public void OrderBy_AscendingNoLimit_WritesAscOnly()
        => Assert.Equal(
            "*5|$7|CLUSTER|$10|SLOT-STATS|$7|ORDERBY|$12|memory-bytes|$3|ASC|",
            Render(RedisServer.ClusterSlotStatsOrderByOperand.Create(ClusterSlotStatsMetric.MemoryBytes, 0, Order.Ascending)));

    [Fact]
    public void SlotsRange_InvalidSlots_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.ClusterSlotStatsSlotsRangeOperand.Create(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.ClusterSlotStatsSlotsRangeOperand.Create(0, 16384));
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.ClusterSlotStatsSlotsRangeOperand.Create(200, 100));
    }

    [Fact]
    public void OrderBy_InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.ClusterSlotStatsOrderByOperand.Create(ClusterSlotStatsMetric.KeyCount, -1, Order.Descending));
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.ClusterSlotStatsOrderByOperand.Create((ClusterSlotStatsMetric)42, 0, Order.Descending));
    }

    private static string Render<T>(T operand)
        where T : IRespArgument
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.CLUSTER}{operand}");
        return Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");
    }
}
