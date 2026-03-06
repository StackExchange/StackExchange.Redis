using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupSubscriber
{
    // for the actual pub/sub tracking, we need to be more subtle; we need to maintain our *own* map of what is subscribed,
    // and only forward messages from the currently live system.
    public void Subscribe(
        RedisChannel channel,
        Action<RedisChannel, RedisValue> handler,
        CommandFlags flags = CommandFlags.None) => parent.SubscribeToAll(channel, handler, flags, asyncState);

    public Task SubscribeAsync(
        RedisChannel channel,
        Action<RedisChannel, RedisValue> handler,
        CommandFlags flags = CommandFlags.None) => parent.SubscribeToAllAsync(channel, handler, flags, asyncState);

    public ChannelMessageQueue Subscribe(
        RedisChannel channel,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException("Soon");

    public Task<ChannelMessageQueue> SubscribeAsync(
        RedisChannel channel,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException("Soon");

    // to do this we'd need to track the *filtered* subscriber per node per subscriber per channel
    public void Unsubscribe(
        RedisChannel channel,
        Action<RedisChannel, RedisValue>? handler = null,
        CommandFlags flags = CommandFlags.None) => throw new NotSupportedException("Removing individual pub/sub handlers from multi-group subscribers is not currently supported");

    public Task UnsubscribeAsync(
        RedisChannel channel,
        Action<RedisChannel, RedisValue>? handler = null,
        CommandFlags flags = CommandFlags.None) => throw new NotSupportedException("Removing individual pub/sub handlers from multi-group subscribers is not currently supported");

    public void UnsubscribeAll(CommandFlags flags = CommandFlags.None) => parent.UnsubscribeFromAll(flags, asyncState);

    public Task UnsubscribeAllAsync(CommandFlags flags = CommandFlags.None) => parent.UnsubscribeFromAllAsync(flags, asyncState);
}

internal sealed partial class MultiGroupMultiplexer
{
    private static Action<RedisChannel, RedisValue> FilteredHandler(MultiGroupMultiplexer parent, ConnectionGroupMember active, Action<RedisChannel, RedisValue> handler)
    {
        // invoke a handler only if the active member is the one we expect
        return (channel, value) =>
        {
            if (ReferenceEquals(parent._active, active.Multiplexer))
            {
                handler(channel, value);
            }
        };
    }

    private readonly struct HandlerTuple(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags, object? asyncState)
    {
        public readonly RedisChannel Channel = channel;
        public readonly Action<RedisChannel, RedisValue> Handler = handler;
        public readonly CommandFlags Flags = flags;
        public readonly object? AsyncState = asyncState;
    }

    private List<HandlerTuple> _handlers = new();

    public void SubscribeToAll(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags, object? asyncState)
    {
        lock (_handlers)
        {
            _handlers.Add(new HandlerTuple(channel, handler, flags, asyncState));
        }
        foreach (var member in _members)
        {
            member.Multiplexer.GetSubscriber(asyncState).Subscribe(channel, FilteredHandler(this, member, handler), flags);
        }
    }

    public async Task SubscribeToAllAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags, object? asyncState)
    {
        lock (_handlers)
        {
            _handlers.Add(new HandlerTuple(channel, handler, flags, asyncState));
        }
        foreach (var member in _members)
        {
            await member.Multiplexer.GetSubscriber(asyncState).SubscribeAsync(channel, FilteredHandler(this, member, handler), flags);
        }
    }

    private HandlerTuple[] LeaseHandlers(out int count)
    {
        HandlerTuple[] lease;
        lock (_handlers)
        {
            count = _handlers.Count;
            if (count == 0) return [];
            lease = ArrayPool<HandlerTuple>.Shared.Rent(count);
            _handlers.CopyTo(lease, 0);
        }

        return lease;
    }

    private async Task AddPubSubHandlersAsync(ConnectionGroupMember member)
    {
        // when adding a connection to an established group, add any missing pub/sub handlers
        var lease = LeaseHandlers(out var count);
        object? asyncState = null;
        ISubscriber? subscriber = null; // try to reuse when possible
        for (int i = 0; i < count; i++)
        {
            var tuple = lease[i];
            if (subscriber is null || tuple.AsyncState != asyncState)
            {
                asyncState = tuple.AsyncState;
                subscriber = member.Multiplexer.GetSubscriber(asyncState);
            }
            await subscriber.SubscribeAsync(tuple.Channel, FilteredHandler(this, member, tuple.Handler), tuple.Flags);
        }
        ArrayPool<HandlerTuple>.Shared.Return(lease);
    }

    public void UnsubscribeFromAll(CommandFlags flags, object? asyncState)
    {
        lock (_handlers)
        {
            _handlers.Clear();
        }
        foreach (var member in _members)
        {
            member.Multiplexer.GetSubscriber(asyncState).UnsubscribeAll(flags);
        }
    }

    public async Task UnsubscribeFromAllAsync(CommandFlags flags, object? asyncState)
    {
        lock (_handlers)
        {
            _handlers.Clear();
        }
        foreach (var member in _members)
        {
            await member.Multiplexer.GetSubscriber(asyncState).UnsubscribeAllAsync(flags);
        }
    }
}
