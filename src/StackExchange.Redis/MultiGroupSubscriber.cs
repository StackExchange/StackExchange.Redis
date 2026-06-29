using System;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupSubscriber(MultiGroupMultiplexer parent, object? asyncState) : ISubscriber
{
    // for a lot of things, we can defer through to the active implementation
    private ISubscriber GetActiveSubscriber() => parent.Active.GetSubscriber(asyncState);

    // non-throwing twin of GetActiveSubscriber: returns null when the group currently has no active
    // member, for use by members that have an obvious trivial result when disconnected
    private ISubscriber? TryGetActiveSubscriber() => parent.TryGetActive()?.GetSubscriber(asyncState);

    public IConnectionMultiplexer Multiplexer => parent;

    public bool TryWait(Task task) => GetActiveSubscriber().TryWait(task);

    public void Wait(Task task) => GetActiveSubscriber().Wait(task);

    public T Wait<T>(Task<T> task) => GetActiveSubscriber().Wait(task);

    public void WaitAll(params Task[] tasks) => GetActiveSubscriber().WaitAll(tasks);

    public TimeSpan Ping(CommandFlags flags = CommandFlags.None) => GetActiveSubscriber().Ping(flags);

    public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None) => GetActiveSubscriber().PingAsync(flags);

    public EndPoint? IdentifyEndpoint(RedisChannel channel, CommandFlags flags = CommandFlags.None) =>
        TryGetActiveSubscriber()?.IdentifyEndpoint(channel, flags);

    public Task<EndPoint?> IdentifyEndpointAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None) =>
        TryGetActiveSubscriber()?.IdentifyEndpointAsync(channel, flags) ?? MultiGroupMultiplexer.NoEndpoint;

    public bool IsConnected(RedisChannel channel = default) => TryGetActiveSubscriber()?.IsConnected() ?? false;

    public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        => GetActiveSubscriber().Publish(channel, message, flags);

    public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        => GetActiveSubscriber().PublishAsync(channel, message, flags);

    public EndPoint? SubscribedEndpoint(RedisChannel channel) => TryGetActiveSubscriber()?.SubscribedEndpoint(channel);
}
