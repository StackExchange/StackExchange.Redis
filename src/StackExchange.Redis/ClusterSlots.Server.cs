using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class RedisServer
{
    public ClusterSlotsResult? ClusterSlots(CommandFlags flags = CommandFlags.None)
        => Wait(Context.Diagnostics.ClusterSlots(flags));

    public Task<ClusterSlotsResult?> ClusterSlotsAsync(CommandFlags flags = CommandFlags.None)
        => Context.Diagnostics.ClusterSlots(flags).AsTask(asyncState, flags);
}
