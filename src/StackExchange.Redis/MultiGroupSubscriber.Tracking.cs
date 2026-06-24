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
        CommandFlags flags = CommandFlags.None) => parent.SubscribeToAll(new(channel, handler, flags, asyncState));

    public Task SubscribeAsync(
        RedisChannel channel,
        Action<RedisChannel, RedisValue> handler,
        CommandFlags flags = CommandFlags.None) => parent.SubscribeToAllAsync(new(channel, handler, flags, asyncState));

    public ChannelMessageQueue Subscribe(
        RedisChannel channel,
        CommandFlags flags = CommandFlags.None)
    {
        var tuple = new MultiGroupMultiplexer.HandlerTuple(channel, flags, asyncState);
        parent.SubscribeToAll(tuple);
        return tuple.Queue!;
    }

    public async Task<ChannelMessageQueue> SubscribeAsync(
        RedisChannel channel,
        CommandFlags flags = CommandFlags.None)
    {
        var tuple = new MultiGroupMultiplexer.HandlerTuple(channel, flags, asyncState);
        await parent.SubscribeToAllAsync(tuple);
        return tuple.Queue!;
    }

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

    private static void ForwardFilteredMessages(
        MultiGroupMultiplexer parent,
        ConnectionGroupMember active,
        ChannelMessageQueue writeTo,
        ChannelMessageQueue readFrom)
    {
        // Create an async worker; note we can't just use a handler-style callback, because the
        // key point of ChannelMessageQueue is to preserve order, and our callback implementation explicitly
        // does not guarantee anything about order.
        _ = Task.Run(() => ForwardFilteredMessagesAsync(parent, active, writeTo, readFrom));
        static async Task ForwardFilteredMessagesAsync(
            MultiGroupMultiplexer parent,
            ConnectionGroupMember active,
            ChannelMessageQueue writeTo,
            ChannelMessageQueue readFrom)
        {
            try
            {
                while (await readFrom.WaitToReadAsync())
                {
                    while (readFrom.TryRead(out var message))
                    {
                        if (ReferenceEquals(parent._active, active.Multiplexer))
                        {
                            // Because of the switchover being imperfect, we can't guarantee exactly one writer, so
                            // we need to be synchronized; in reality, it will *almost never* be contended, so
                            // this isn't a bottleneck - so we will pay the price of a lock here, and keep the
                            // queue in single-writer mode.
                            writeTo.SynchronizedWrite(message.Channel, message.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                parent.OnInternalError(ex);
            }
        }
    }

    internal readonly struct HandlerTuple
    {
        public readonly RedisChannel Channel;
        public Action<RedisChannel, RedisValue>? Handler => _handlerOrQueue as Action<RedisChannel, RedisValue>;
        public ChannelMessageQueue? Queue => _handlerOrQueue as ChannelMessageQueue;
        public readonly CommandFlags Flags;
        public readonly object? AsyncState;

        private readonly object _handlerOrQueue;

        public HandlerTuple(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags, object? asyncState)
        {
            Channel = channel;
            _handlerOrQueue = handler;
            Flags = flags;
            AsyncState = asyncState;
        }

        public HandlerTuple(RedisChannel channel, CommandFlags flags, object? asyncState)
        {
            Channel = channel;
            // note: multi-writer because we can't rule out race conditions at switchover
            _handlerOrQueue = new ChannelMessageQueue(channel, null);
            Flags = flags;
            AsyncState = asyncState;
        }
    }

    private List<HandlerTuple> _handlers = new();

    internal void SubscribeToAll(HandlerTuple tuple)
    {
        lock (_handlers)
        {
            _handlers.Add(tuple);
        }
        foreach (var member in _members)
        {
            var sub = member.Multiplexer.GetSubscriber(tuple.AsyncState);
            if (tuple.Handler is not null)
            {
                sub.Subscribe(tuple.Channel, FilteredHandler(this, member, tuple.Handler), tuple.Flags);
            }
            else if (tuple.Queue is not null)
            {
                var from = sub.Subscribe(tuple.Channel, tuple.Flags);
                ForwardFilteredMessages(this, member, tuple.Queue, from);
            }
        }
    }

    internal async Task SubscribeToAllAsync(HandlerTuple tuple)
    {
        lock (_handlers)
        {
            _handlers.Add(tuple);
        }
        foreach (var member in _members)
        {
            var sub = member.Multiplexer.GetSubscriber(tuple.AsyncState);
            if (tuple.Handler is not null)
            {
                await sub.SubscribeAsync(tuple.Channel, FilteredHandler(this, member, tuple.Handler), tuple.Flags);
            }
            else if (tuple.Queue is not null)
            {
                var from = await sub.SubscribeAsync(tuple.Channel, tuple.Flags);
                ForwardFilteredMessages(this, member, tuple.Queue, from);
            }
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
        ISubscriber? sub = null; // try to reuse when possible
        for (int i = 0; i < count; i++)
        {
            var tuple = lease[i];
            if (sub is null || tuple.AsyncState != asyncState)
            {
                asyncState = tuple.AsyncState;
                sub = member.Multiplexer.GetSubscriber(asyncState);
            }
            if (tuple.Handler is not null)
            {
                await sub.SubscribeAsync(tuple.Channel, FilteredHandler(this, member, tuple.Handler), tuple.Flags);
            }
            else if (tuple.Queue is not null)
            {
                var from = await sub.SubscribeAsync(tuple.Channel, tuple.Flags);
                ForwardFilteredMessages(this, member, tuple.Queue, from);
            }
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
