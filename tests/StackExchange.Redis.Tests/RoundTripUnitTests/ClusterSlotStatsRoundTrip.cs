using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class ClusterSlotStatsRoundTrip(ITestOutputHelper log)
{
    // a RESP2 reply with every documented metric present
    private const string FullEntryResp2 =
        "*1\r\n*2\r\n:100\r\n*10\r\n"
        + "$9\r\nkey-count\r\n:42\r\n"
        + "$8\r\ncpu-usec\r\n:1500\r\n"
        + "$12\r\nmemory-bytes\r\n:2048\r\n"
        + "$16\r\nnetwork-bytes-in\r\n:300\r\n"
        + "$17\r\nnetwork-bytes-out\r\n:400\r\n";

    // the same payload as a RESP3 map
    private const string FullEntryResp3 =
        "*1\r\n*2\r\n:100\r\n%5\r\n"
        + "$9\r\nkey-count\r\n:42\r\n"
        + "$8\r\ncpu-usec\r\n:1500\r\n"
        + "$12\r\nmemory-bytes\r\n:2048\r\n"
        + "$16\r\nnetwork-bytes-in\r\n:300\r\n"
        + "$17\r\nnetwork-bytes-out\r\n:400\r\n";

    private static void AssertFullEntry(ClusterSlotStatsResult? result)
    {
        Assert.NotNull(result);
        var entry = Assert.Single(result.Slots);
        Assert.Equal(100, entry.Slot);
        Assert.Equal(42, entry.KeyCount);
        Assert.Equal(TimeSpan.FromTicks(1500 * 10), entry.CpuTime); // 1500µs; 1 tick = 0.1µs
        Assert.Equal(2048, entry.MemoryBytes);
        Assert.Equal(300, entry.NetworkBytesIn);
        Assert.Equal(400, entry.NetworkBytesOut);
        Assert.Empty(entry.Metadata);
    }

    [Fact(Timeout = 1000)]
    public async Task SlotsRange_FullMap_Resp2_RoundTrips()
    {
        var msg = RedisServer.GetClusterSlotStatsBySlotRangeMessage(100, 200, CommandFlags.None);
        const string requestResp = "*5\r\n$7\r\nCLUSTER\r\n$10\r\nSLOT-STATS\r\n$10\r\nSLOTSRANGE\r\n$3\r\n100\r\n$3\r\n200\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ClusterSlotStatsResult.Processor, requestResp, FullEntryResp2, log: log);
        AssertFullEntry(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SlotsRange_FullMap_Resp3_RoundTrips()
    {
        var msg = RedisServer.GetClusterSlotStatsBySlotRangeMessage(100, 200, CommandFlags.None);
        const string requestResp = "*5\r\n$7\r\nCLUSTER\r\n$10\r\nSLOT-STATS\r\n$10\r\nSLOTSRANGE\r\n$3\r\n100\r\n$3\r\n200\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ClusterSlotStatsResult.Processor, requestResp, FullEntryResp3, protocol: RedisProtocol.Resp3, log: log);
        AssertFullEntry(result);
    }

    [Fact(Timeout = 1000)]
    public async Task OrderBy_Defaults_OmitsLimitAndOrder()
    {
        // the server defaults (all slots, descending) are expressed by omission
        var msg = RedisServer.GetClusterSlotStatsOrderByMessage(ClusterSlotStatsMetric.KeyCount, 0, Order.Descending, CommandFlags.None);
        const string requestResp = "*4\r\n$7\r\nCLUSTER\r\n$10\r\nSLOT-STATS\r\n$7\r\nORDERBY\r\n$9\r\nkey-count\r\n";

        // key-count only: the other metrics are not being collected, and must come back null
        const string responseResp = "*2\r\n"
            + "*2\r\n:8000\r\n*2\r\n$9\r\nkey-count\r\n:7\r\n"
            + "*2\r\n:12\r\n*2\r\n$9\r\nkey-count\r\n:3\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ClusterSlotStatsResult.Processor, requestResp, responseResp, log: log);
        Assert.NotNull(result);
        Assert.Equal(2, result.Slots.Count);
        Assert.Equal(8000, result.Slots[0].Slot); // server order is preserved, not re-sorted
        Assert.Equal(7, result.Slots[0].KeyCount);
        Assert.Null(result.Slots[0].CpuTime);
        Assert.Null(result.Slots[0].MemoryBytes);
        Assert.Null(result.Slots[0].NetworkBytesIn);
        Assert.Null(result.Slots[0].NetworkBytesOut);
        Assert.Equal(12, result.Slots[1].Slot);
        Assert.Equal(3, result.Slots[1].KeyCount);
    }

    [Fact(Timeout = 1000)]
    public async Task OrderBy_WithLimit_WritesLimit()
    {
        var msg = RedisServer.GetClusterSlotStatsOrderByMessage(ClusterSlotStatsMetric.CpuTime, 5, Order.Descending, CommandFlags.None);
        const string requestResp = "*6\r\n$7\r\nCLUSTER\r\n$10\r\nSLOT-STATS\r\n$7\r\nORDERBY\r\n$8\r\ncpu-usec\r\n$5\r\nLIMIT\r\n$1\r\n5\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ClusterSlotStatsResult.Processor, requestResp, "*0\r\n", log: log);
        Assert.NotNull(result);
        Assert.Empty(result.Slots);
    }

    [Fact(Timeout = 1000)]
    public async Task OrderBy_AscendingWithLimit_WritesBoth()
    {
        var msg = RedisServer.GetClusterSlotStatsOrderByMessage(ClusterSlotStatsMetric.NetworkBytesOut, 3, Order.Ascending, CommandFlags.None);
        const string requestResp = "*7\r\n$7\r\nCLUSTER\r\n$10\r\nSLOT-STATS\r\n$7\r\nORDERBY\r\n$17\r\nnetwork-bytes-out\r\n$5\r\nLIMIT\r\n$1\r\n3\r\n$3\r\nASC\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ClusterSlotStatsResult.Processor, requestResp, "*0\r\n", log: log);
        Assert.NotNull(result);
        Assert.Empty(result.Slots);
    }

    [Fact(Timeout = 1000)]
    public async Task OrderBy_AscendingNoLimit_WritesAscOnly()
    {
        var msg = RedisServer.GetClusterSlotStatsOrderByMessage(ClusterSlotStatsMetric.MemoryBytes, 0, Order.Ascending, CommandFlags.None);
        const string requestResp = "*5\r\n$7\r\nCLUSTER\r\n$10\r\nSLOT-STATS\r\n$7\r\nORDERBY\r\n$12\r\nmemory-bytes\r\n$3\r\nASC\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ClusterSlotStatsResult.Processor, requestResp, "*0\r\n", log: log);
        Assert.NotNull(result);
        Assert.Empty(result.Slots);
    }

    [Fact(Timeout = 1000)]
    public async Task UnknownMetric_IsPreservedAsMetadata()
    {
        // the metric set is documented as extensible; a metric this library has never
        // heard of must survive as metadata, not be dropped
        var msg = RedisServer.GetClusterSlotStatsBySlotRangeMessage(0, 0, CommandFlags.None);
        const string requestResp = "*5\r\n$7\r\nCLUSTER\r\n$10\r\nSLOT-STATS\r\n$10\r\nSLOTSRANGE\r\n$1\r\n0\r\n$1\r\n0\r\n";
        const string responseResp = "*1\r\n*2\r\n:0\r\n*4\r\n"
            + "$9\r\nkey-count\r\n:1\r\n"
            + "$11\r\nwidget-usec\r\n:99\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ClusterSlotStatsResult.Processor, requestResp, responseResp, log: log);
        Assert.NotNull(result);
        var entry = Assert.Single(result.Slots);
        Assert.Equal(1, entry.KeyCount);
        var extra = Assert.Single(entry.Metadata);
        Assert.Equal("widget-usec", extra.Key);
        Assert.Equal(99, extra.Value);
    }

    [Fact(Timeout = 1000)]
    public async Task NilReply_IsNull()
    {
        var msg = RedisServer.GetClusterSlotStatsBySlotRangeMessage(0, 100, CommandFlags.None);
        const string requestResp = "*5\r\n$7\r\nCLUSTER\r\n$10\r\nSLOT-STATS\r\n$10\r\nSLOTSRANGE\r\n$1\r\n0\r\n$3\r\n100\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ClusterSlotStatsResult.Processor, requestResp, "*-1\r\n", log: log);
        Assert.Null(result);
    }

    [Fact]
    public void SlotsRange_InvalidSlots_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.GetClusterSlotStatsBySlotRangeMessage(-1, 100, CommandFlags.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.GetClusterSlotStatsBySlotRangeMessage(0, 16384, CommandFlags.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.GetClusterSlotStatsBySlotRangeMessage(200, 100, CommandFlags.None));
    }

    [Fact]
    public void OrderBy_InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.GetClusterSlotStatsOrderByMessage(ClusterSlotStatsMetric.KeyCount, -1, Order.Descending, CommandFlags.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisServer.GetClusterSlotStatsOrderByMessage((ClusterSlotStatsMetric)42, 0, Order.Descending, CommandFlags.None));
    }
}
