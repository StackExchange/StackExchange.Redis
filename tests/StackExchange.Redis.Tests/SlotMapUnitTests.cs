using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="ServerSelectionStrategy.IsSlotMapEmpty"/> is derived from whether the map has been allocated, which is
/// only sound if nothing can allocate it without writing an owner into it (see #3284/#3285).
/// </summary>
public class SlotMapUnitTests(ITestOutputHelper log)
{
    [Fact]
    public async Task RejectedRangesDoNotAllocateTheMap()
    {
        using var server = new InProcessTestServer(log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig(withPubSub: false, defaultOnly: true));
        var strategy = conn.ServerSelectionStrategy;
        var endpoint = conn.GetServerSnapshot()[0];

        Assert.True(strategy.IsSlotMapEmpty); // standalone: nothing has been applied
        Assert.Equal(0, strategy.CountCoveredSlots());

        strategy.UpdateClusterRange(5, 3, endpoint); // inverted
        strategy.UpdateClusterRange(-1, 3, endpoint); // below range
        strategy.UpdateClusterRange(16380, 16384, endpoint); // above range
        Assert.True(strategy.IsSlotMapEmpty);
        Assert.Equal(0, strategy.CountCoveredSlots());

        strategy.UpdateClusterRange(3, 5, endpoint);
        Assert.False(strategy.IsSlotMapEmpty);
        Assert.Equal(3, strategy.CountCoveredSlots());
    }
}
