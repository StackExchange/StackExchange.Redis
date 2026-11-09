using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>CLUSTER SLOT-STATS</c> against a real cluster; the exact wire bytes and reply-shape matrix live in
/// <see cref="RoundTripUnitTests.ClusterSlotStatsRoundTrip"/>. Runs per protocol, since the per-slot metrics
/// are a map under RESP3 and flat pairs under RESP2.
/// </summary>
[RunPerProtocol]
public class ClusterSlotStatsTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts + ",connectTimeout=10000";

    private static void RequireSlotStats(IServer server)
        => Assert.SkipUnless(server.Features.ClusterSlotStats, $"CLUSTER SLOT-STATS needs 8.2, server is {server.Version}");

    [Fact]
    public async Task RoutedWriteIsCountedInItsSlot()
    {
        await using var conn = Create(allowAdmin: true);
        RedisKey key = Me();
        var server = conn.GetServer(key); // the primary that owns the key
        RequireSlotStats(server);

        await conn.GetDatabase().StringSetAsync(key, "x");
        var slot = conn.HashSlot(key);

        var result = await server.ClusterSlotStatsBySlotRangeAsync(slot, slot);
        Assert.NotNull(result);
        var entry = Assert.Single(result.Slots);
        Assert.Equal(slot, entry.Slot);
        Log($"slot {slot}: {entry.KeyCount} keys");
        Assert.True(entry.KeyCount >= 1, "the slot we just wrote to should hold at least our key");
    }

    [Fact]
    public async Task WholeRangeReportsOnlyTheNodesOwnSlots()
    {
        await using var conn = Create(allowAdmin: true);
        var server = conn.GetServer(conn.GetEndPoints()[0]);
        RequireSlotStats(server);

        var result = await server.ClusterSlotStatsBySlotRangeAsync(SlotRange.MinSlot, SlotRange.MaxSlot);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Slots);
        Log($"{result.Slots.Count} slots reported by {server.EndPoint}");

        // ascending slot order, each slot at most once, all in range
        for (int i = 1; i < result.Slots.Count; i++)
        {
            Assert.True(result.Slots[i - 1].Slot < result.Slots[i].Slot, "SLOTSRANGE results should be in ascending slot order");
        }
        Assert.All(result.Slots, s => Assert.InRange(s.Slot, SlotRange.MinSlot, SlotRange.MaxSlot));
    }

    [Fact]
    public async Task OrderByHonoursLimitAndOrder()
    {
        await using var conn = Create(allowAdmin: true);
        var server = conn.GetServer(conn.GetEndPoints()[0]);
        RequireSlotStats(server);

        // make sure at least two slots on this node have different key counts
        var db = conn.GetDatabase();
        for (int i = 0; i < 10; i++)
        {
            await db.StringSetAsync($"{Me()}:{i}", i);
        }

        var top = await server.ClusterSlotStatsOrderByAsync(ClusterSlotStatsMetric.KeyCount, limit: 5);
        Assert.NotNull(top);
        Assert.InRange(top.Slots.Count, 1, 5);
        for (int i = 1; i < top.Slots.Count; i++)
        {
            Assert.True(top.Slots[i - 1].KeyCount >= top.Slots[i].KeyCount, "descending is the default");
        }

        var bottom = await server.ClusterSlotStatsOrderByAsync(ClusterSlotStatsMetric.KeyCount, limit: 5, order: Order.Ascending);
        Assert.NotNull(bottom);
        Assert.InRange(bottom.Slots.Count, 1, 5);
        for (int i = 1; i < bottom.Slots.Count; i++)
        {
            Assert.True(bottom.Slots[i - 1].KeyCount <= bottom.Slots[i].KeyCount, "ascending when asked");
        }

        Log($"top: {string.Join(", ", top.Slots.Select(s => s.ToString()))}");
        Log($"bottom: {string.Join(", ", bottom.Slots.Select(s => s.ToString()))}");
    }

    [Fact]
    public async Task EnablingMetricsSurfacesThem()
    {
        await using var conn = Create(allowAdmin: true, shared: false);
        RedisKey key = Me();
        var server = conn.GetServer(key);
        RequireSlotStats(server);

        var original = (await server.ConfigGetAsync("cluster-slot-stats-enabled")).Single().Value;
        Log($"cluster-slot-stats-enabled was: '{original}'");
        try
        {
            // cpu and net can be enabled at runtime; mem needs memory tracking from server startup
            await server.ConfigSetAsync("cluster-slot-stats-enabled", "cpu net");

            // generate some traffic on a slot this node owns, after enabling
            var db = conn.GetDatabase();
            var slot = conn.HashSlot(key);
            for (int i = 0; i < 20; i++)
            {
                await db.StringSetAsync(key, i);
                await db.StringGetAsync(key);
            }

            var result = await server.ClusterSlotStatsBySlotRangeAsync(slot, slot);
            Assert.NotNull(result);
            var entry = Assert.Single(result.Slots);
            Log(entry.ToString());
            Log($"cpu: {entry.CpuTime}, in: {entry.NetworkBytesIn}, out: {entry.NetworkBytesOut}, mem: {entry.MemoryBytes}");

            Assert.NotNull(entry.CpuTime);
            Assert.NotNull(entry.NetworkBytesIn);
            Assert.NotNull(entry.NetworkBytesOut);
            Assert.True(entry.NetworkBytesIn > 0, "we sent this slot twenty SETs; bytes-in should be non-zero");
            Assert.Null(entry.MemoryBytes); // not enabled - null, not zero
        }
        finally
        {
            await server.ConfigSetAsync("cluster-slot-stats-enabled", original);
        }
    }

    [Fact]
    public async Task OrderByDisabledMetricIsRefused()
    {
        await using var conn = Create(allowAdmin: true);
        var server = conn.GetServer(conn.GetEndPoints()[0]);
        RequireSlotStats(server);

        var enabled = (await server.ConfigGetAsync("cluster-slot-stats-enabled")).Single().Value.ToString();
        Assert.SkipWhen(enabled.Contains("mem") || enabled.Contains("yes"), $"memory collection is enabled ('{enabled}'), so the server would not refuse it");

        var ex = await Assert.ThrowsAsync<RedisServerException>(
            () => server.ClusterSlotStatsOrderByAsync(ClusterSlotStatsMetric.MemoryBytes));
        Log(ex.Message);
        Assert.Contains("ORDERBY", ex.Message);
    }

    [Fact]
    public async Task StandaloneServerRefuses()
    {
        await using var conn = Create(configuration: TestConfig.Current.PrimaryServerAndPort, allowAdmin: true, shared: false);
        var server = conn.GetServer(conn.GetEndPoints()[0]);
        RequireSlotStats(server);

        var ex = await Assert.ThrowsAsync<RedisServerException>(
            () => server.ClusterSlotStatsBySlotRangeAsync(0, 100));
        Log(ex.Message);
        Assert.Contains("cluster support disabled", ex.Message);
    }
}
