using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class RedisServer
{
    public ClusterSlotsResult? ClusterSlots(CommandFlags flags = CommandFlags.None)
        => ExecuteSync(GetClusterSlotsMessage(flags), ClusterSlotsResult.Processor);

    public Task<ClusterSlotsResult?> ClusterSlotsAsync(CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(GetClusterSlotsMessage(flags), ClusterSlotsResult.Processor);
}
