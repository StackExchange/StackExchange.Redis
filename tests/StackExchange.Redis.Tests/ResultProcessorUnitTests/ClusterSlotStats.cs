using System;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class ClusterSlotStats(ITestOutputHelper log) : ResultProcessorUnitTest(log)
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

    [Fact]
    public void FullMap_Resp2()
        => AssertFullEntry(Execute(FullEntryResp2, ClusterSlotStatsResult.Handler.Instance));

    [Fact]
    public void FullMap_Resp3()
        => AssertFullEntry(Execute(FullEntryResp3, ClusterSlotStatsResult.Handler.Instance));

    [Fact]
    public void KeyCountOnly_OtherMetricsAreNull()
    {
        // key-count only: the other metrics are not being collected, and must come back null
        const string resp = "*2\r\n"
            + "*2\r\n:8000\r\n*2\r\n$9\r\nkey-count\r\n:7\r\n"
            + "*2\r\n:12\r\n*2\r\n$9\r\nkey-count\r\n:3\r\n";

        var result = Execute(resp, ClusterSlotStatsResult.Handler.Instance);
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

    [Fact]
    public void UnknownMetric_IsPreservedAsMetadata()
    {
        // the metric set is documented as extensible; a metric this library has never
        // heard of must survive as metadata, not be dropped
        const string resp = "*1\r\n*2\r\n:0\r\n*4\r\n"
            + "$9\r\nkey-count\r\n:1\r\n"
            + "$11\r\nwidget-usec\r\n:99\r\n";

        var result = Execute(resp, ClusterSlotStatsResult.Handler.Instance);
        Assert.NotNull(result);
        var entry = Assert.Single(result.Slots);
        Assert.Equal(1, entry.KeyCount);
        var extra = Assert.Single(entry.Metadata);
        Assert.Equal("widget-usec", extra.Key);
        Assert.Equal(99, extra.Value);
    }

    [Fact]
    public void EmptyArray_IsEmpty()
    {
        var result = Execute("*0\r\n", ClusterSlotStatsResult.Handler.Instance);
        Assert.NotNull(result);
        Assert.Empty(result.Slots);
    }

    [Fact]
    public void NilReply_IsNull()
        => Assert.Null(Execute("*-1\r\n", ClusterSlotStatsResult.Handler.Instance));

    [Fact]
    public void MalformedEntry_IsDroppedNotFatal()
    {
        // an entry whose slot is not numeric is dropped; its well-formed sibling survives
        const string resp = "*2\r\n"
            + "*2\r\n$3\r\nwat\r\n*2\r\n$9\r\nkey-count\r\n:1\r\n"
            + "*2\r\n:5\r\n*2\r\n$9\r\nkey-count\r\n:2\r\n";

        var result = Execute(resp, ClusterSlotStatsResult.Handler.Instance);
        Assert.NotNull(result);
        var entry = Assert.Single(result.Slots);
        Assert.Equal(5, entry.Slot);
        Assert.Equal(2, entry.KeyCount);
    }

    [Fact]
    public void ScalarReply_IsUnexpected()
        => ExecuteUnexpected("+OK\r\n", ClusterSlotStatsResult.Handler.Instance);
}
