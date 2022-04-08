using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        private RedisSubscriber? _defaultSubscriber;
        private RedisSubscriber DefaultSubscriber => _defaultSubscriber ??= new RedisSubscriber(this, null);

        private readonly ConcurrentDictionary<RedisChannel, Subscription> subscriptions = new();

        internal ConcurrentDictionary<RedisChannel, Subscription> GetSubscriptions() => subscriptions;
        internal int GetSubscriptionsCount() => subscriptions.Count;

        internal Subscription GetOrAddSubscription(in RedisChannel channel, CommandFlags flags)
        {
            lock (subscriptions)
            {
                if (!subscriptions.TryGetValue(channel, out var sub))
                {
                    sub = new Subscription(flags);
                    subscriptions.TryAdd(channel, sub);
                }
                return sub;
            }
        }
        internal bool TryGetSubscription(in RedisChannel channel, [NotNullWhen(true)] out Subscription? sub) => subscriptions.TryGetValue(channel, out sub);
        internal bool TryRemoveSubscription(in RedisChannel channel, [NotNullWhen(true)] out Subscription? sub)
        {
            lock (subscriptions)
            {
                return subscriptions.TryRemove(channel, out sub);
            }
        }

        /// <summary>
        /// Gets the subscriber counts for a channel.
        /// </summary>
        /// <returns><see langword="true"/> if there's a subscription registered at all.</returns>
        internal bool GetSubscriberCounts(in RedisChannel channel, out int handlers, out int queues)
        {
            if (subscriptions.TryGetValue(channel, out var sub))
            {
                sub.GetSubscriberCounts(out handlers, out queues);
                return true;
            }
            handlers = queues = 0;
            return false;
        }

        /// <summary>
        /// Gets which server, if any, there's a registered subscription to for this channel.
        /// </summary>
        /// <remarks>
        /// This may be null if there is a subscription, but we don't have a connected server at the moment.
        /// This behavior is fine but IsConnected checks, but is a subtle difference in <see cref="ISubscriber.SubscribedEndpoint(RedisChannel)"/>.
        /// </remarks>
        internal ServerEndPoint? GetSubscribedServer(in RedisChannel channel)
        {
            if (!channel.IsNullOrEmpty && subscriptions.TryGetValue(channel, out Subscription? sub))
            {
                return sub.GetCurrentServer();
            }
            return null;
        }

        /// <summary>
        /// Handler that executes whenever a message comes in, this doles out messages to any registered handlers.
        /// </summary>
        internal void OnMessage(in RedisChannel subscription, in RedisChannel channel, in RedisValue payload)
        {
            ICompletable? completable = null;
            ChannelMessageQueue? queues = null;
            if (subscriptions.TryGetValue(subscription, out Subscription? sub))
            {
                completable = sub.ForInvoke(channel, payload, out queues);
            }
            if (queues != null)
            {
                ChannelMessageQueue.WriteAll(ref queues, channel, payload);
            }
            if (completable != null && !completable.TryComplete(false))
            {
                CompleteAsWorker(completable);
            }
        }

        /// <summary>
        /// Updates all subscriptions re-evaluating their state.
        /// This clears the current server if it's not connected, prepping them to reconnect.
        /// </summary>
        internal void UpdateSubscriptions()
        {
            foreach (var pair in subscriptions)
            {
                pair.Value.UpdateServer();
            }
        }

        /// <summary>
        /// Ensures all subscriptions are connected to a server, if possible.
        /// </summary>
        /// <returns>The count of subscriptions attempting to reconnect (same as the count currently not connected).</returns>
        internal long EnsureSubscriptions(CommandFlags flags = CommandFlags.None)
        {
            // TODO: Subscribe with variadic commands to reduce round trips
            long count = 0;
            foreach (var pair in subscriptions)
            {
                if (!pair.Value.IsConnected)
                {
                    count++;
                    DefaultSubscriber.EnsureSubscribedToServer(pair.Value, pair.Key, flags, true);
                }
            }
            return count;
        }

        internal enum SubscriptionAction
        {
            Subscribe,
            Unsubscribe
        }

        /// <summary>
        /// This is the record of a single subscription to a redis server.
        /// It's the singular channel (which may or may not be a pattern), to one or more handlers.
        /// We subscriber to a redis server once (for all messages) and execute 1-many handlers when a message arrives.
        /// </summary>
        internal sealed class Subscription
        {
            private Action<RedisChannel, RedisValue>? _handlers;
            private ChannelMessageQueue? _queues;
            private ServerEndPoint? CurrentServer;
            public CommandFlags Flags { get; }
            public ResultProcessor.TrackSubscriptionsProcessor Processor { get; }

            /// <summary>
            /// Whether the <see cref="CurrentServer"/> we have is connected.
            /// Since we clear <see cref="CurrentServer"/> on a disconnect, this should stay correct.
            /// </summary>
            internal bool IsConnected => CurrentServer?.IsSubscriberConnected == true;

            public Subscription(CommandFlags flags)
            {
                Flags = flags;
                Processor = new ResultProcessor.TrackSubscriptionsProcessor(this);
            }

            /// <summary>
            /// Gets the configured (P)SUBSCRIBE or (P)UNSUBSCRIBE <see cref="Message"/> for an action.
            /// </summary>
            internal Message GetMessage(RedisChannel channel, SubscriptionAction action, CommandFlags flags, bool internalCall)
            {
                var isPattern = channel.IsPatternBased;
                var command = action switch
                {
                    SubscriptionAction.Subscribe when isPattern => RedisCommand.PSUBSCRIBE,
                    SubscriptionAction.Unsubscribe when isPattern => RedisCommand.PUNSUBSCRIBE,

                    SubscriptionAction.Subscribe when !isPattern => RedisCommand.SUBSCRIBE,
                    SubscriptionAction.Unsubscribe when !isPattern => RedisCommand.UNSUBSCRIBE,
                    _ => throw new ArgumentOutOfRangeException(nameof(action), "This would be an impressive boolean feat"),
                };

                // TODO: Consider flags here - we need to pass Fire and Forget, but don't want to intermingle Primary/Replica
                var msg = Message.Create(-1, Flags | flags, command, channel);
                msg.SetForSubscriptionBridge();
                if (internalCall)
                {
                    msg.SetInternalCall();
                }
                return msg;
            }

            public void Add(Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue)
            {
                if (handler != null)
                {
                    _handlers += handler;
                }
                if (queue != null)
                {
                    ChannelMessageQueue.Combine(ref _queues, queue);
                }
            }

            public bool Remove(Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue)
            {
                if (handler != null)
                {
                    _handlers -= handler;
                }
                if (queue != null)
                {
                    ChannelMessageQueue.Remove(ref _queues, queue);
                }
                return _handlers == null & _queues == null;
            }

            public ICompletable? ForInvoke(in RedisChannel channel, in RedisValue message, out ChannelMessageQueue? queues)
            {
                var handlers = _handlers;
                queues = Volatile.Read(ref _queues);
                return handlers == null ? null : new MessageCompletable(channel, message, handlers);
            }

            internal void MarkCompleted()
            {
                _handlers = null;
                ChannelMessageQueue.MarkAllCompleted(ref _queues);
            }

            internal void GetSubscriberCounts(out int handlers, out int queues)
            {
                queues = ChannelMessageQueue.Count(ref _queues);
                var tmp = _handlers;
                if (tmp == null)
                {
                    handlers = 0;
                }
                else if (tmp.IsSingle())
                {
                    handlers = 1;
                }
                else
                {
                    handlers = 0;
                    foreach (var sub in tmp.AsEnumerable()) { handlers++; }
                }
            }

            internal ServerEndPoint? GetCurrentServer() => Volatile.Read(ref CurrentServer);
            internal void SetCurrentServer(ServerEndPoint? server) => CurrentServer = server;

            /// <summary>
            /// Evaluates state and if we're not currently connected, clears the server reference.
            /// </summary>
            internal void UpdateServer()
            {
                if (!IsConnected)
                {
                    CurrentServer = null;
                }
            }
        }
    }

    /// <summary>
    /// A <see cref="RedisBase"/> wrapper for subscription actions.
    /// </summary>
    /// <remarks>
    /// By having most functionality here and state on <see cref="Subscription"/>, we can
    /// use the baseline execution methods to take the normal message paths.
    /// </remarks>
    internal sealed class RedisSubscriber : RedisBase, ISubscriber
    {
        internal RedisSubscriber(ConnectionMultiplexer multiplexer, object? asyncState) : base(multiplexer, asyncState)
        {
        }

        public EndPoint? IdentifyEndpoint(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMSUB, channel);
            msg.SetInternalCall();
            return ExecuteSync(msg, ResultProcessor.ConnectionIdentity);
        }

        public Task<EndPoint?> IdentifyEndpointAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMSUB, channel);
            msg.SetInternalCall();
            return ExecuteAsync(msg, ResultProcessor.ConnectionIdentity);
        }

        /// <summary>
        /// This is *could* we be connected, as in "what's the theoretical endpoint for this channel?",
        /// rather than if we're actually connected and actually listening on that channel.
        /// </summary>
        public bool IsConnected(RedisChannel channel = default)
        {
            var server = multiplexer.GetSubscribedServer(channel) ?? multiplexer.SelectServer(RedisCommand.SUBSCRIBE, CommandFlags.DemandMaster, channel);
            return server?.IsConnected == true && server.IsSubscriberConnected;
        }

        public override TimeSpan Ping(CommandFlags flags = CommandFlags.None)
        {
            var msg = CreatePingMessage(flags);
            return ExecuteSync(msg, ResultProcessor.ResponseTimer);
        }

        public override Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = CreatePingMessage(flags);
            return ExecuteAsync(msg, ResultProcessor.ResponseTimer);
        }

        private Message CreatePingMessage(CommandFlags flags)
        {
            bool usePing = false;
            if (multiplexer.CommandMap.IsAvailable(RedisCommand.PING))
            {
                try { usePing = GetFeatures(default, flags, out _).PingOnSubscriber; }
                catch { }
            }

            Message msg;
            if (usePing)
            {
                msg = ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.PING);
            }
            else
            {
                // can't use regular PING, but we can unsubscribe from something random that we weren't even subscribed to...
                RedisValue channel = multiplexer.UniqueId;
                msg = ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.UNSUBSCRIBE, channel);
            }
            // Ensure the ping is sent over the intended subscriber connection, which wouldn't happen in GetBridge() by default with PING;
            msg.SetForSubscriptionBridge();
            return msg;
        }

        private static void ThrowIfNull(in RedisChannel channel)
        {
            if (channel.IsNullOrEmpty)
            {
                throw new ArgumentNullException(nameof(channel));
            }
        }

        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            ThrowIfNull(channel);
            var msg = Message.Create(-1, flags, RedisCommand.PUBLISH, channel, message);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            ThrowIfNull(channel);
            var msg = Message.Create(-1, flags, RedisCommand.PUBLISH, channel, message);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        void ISubscriber.Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => Subscribe(channel, handler, null, flags);

        public ChannelMessageQueue Subscribe(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var queue = new ChannelMessageQueue(channel, this);
            Subscribe(channel, null, queue, flags);
            return queue;
        }

        public bool Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, CommandFlags flags)
        {
            ThrowIfNull(channel);
            if (handler == null && queue == null) { return true; }

            var sub = multiplexer.GetOrAddSubscription(channel, flags);
            sub.Add(handler, queue);
            return EnsureSubscribedToServer(sub, channel, flags, false);
        }

        internal bool EnsureSubscribedToServer(Subscription sub, RedisChannel channel, CommandFlags flags, bool internalCall)
        {
            if (sub.IsConnected) { return true; }

            // TODO: Cleanup old hangers here?

            sub.SetCurrentServer(null); // we're not appropriately connected, so blank it out for eligible reconnection
            var message = sub.GetMessage(channel, SubscriptionAction.Subscribe, flags, internalCall);
            var selected = multiplexer.SelectServer(message);
            return ExecuteSync(message, sub.Processor, selected);
        }

        Task ISubscriber.SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => SubscribeAsync(channel, handler, null, flags);

        public async Task<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var queue = new ChannelMessageQueue(channel, this);
            await SubscribeAsync(channel, null, queue, flags).ForAwait();
            return queue;
        }

        public Task<bool> SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, CommandFlags flags)
        {
            ThrowIfNull(channel);
            if (handler == null && queue == null) { return CompletedTask<bool>.Default(null); }

            var sub = multiplexer.GetOrAddSubscription(channel, flags);
            sub.Add(handler, queue);
            return EnsureSubscribedToServerAsync(sub, channel, flags, false);
        }

        public Task<bool> EnsureSubscribedToServerAsync(Subscription sub, RedisChannel channel, CommandFlags flags, bool internalCall)
        {
            if (sub.IsConnected) { return CompletedTask<bool>.Default(null); }

            // TODO: Cleanup old hangers here?

            sub.SetCurrentServer(null); // we're not appropriately connected, so blank it out for eligible reconnection
            var message = sub.GetMessage(channel, SubscriptionAction.Subscribe, flags, internalCall);
            var selected = multiplexer.SelectServer(message);
            return ExecuteAsync(message, sub.Processor, selected);
        }

        public EndPoint? SubscribedEndpoint(RedisChannel channel) => multiplexer.GetSubscribedServer(channel)?.EndPoint;

        void ISubscriber.Unsubscribe(RedisChannel channel, Action<RedisChannel, RedisValue>? handler, CommandFlags flags)
            => Unsubscribe(channel, handler, null, flags);

        public bool Unsubscribe(in RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, CommandFlags flags)
        {
            ThrowIfNull(channel);
            // Unregister the subscription handler/queue, and if that returns true (last handler removed), also disconnect from the server
            return UnregisterSubscription(channel, handler, queue, out var sub)
                ? UnsubscribeFromServer(sub, channel, flags, false)
                : true;
        }

        private bool UnsubscribeFromServer(Subscription sub, RedisChannel channel, CommandFlags flags, bool internalCall)
        {
            if (sub.GetCurrentServer() is ServerEndPoint oldOwner)
            {
                var message = sub.GetMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall);
                return multiplexer.ExecuteSyncImpl(message, sub.Processor, oldOwner);
            }
            return false;
        }

        Task ISubscriber.UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue>? handler, CommandFlags flags)
            => UnsubscribeAsync(channel, handler, null, flags);

        public Task<bool> UnsubscribeAsync(in RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, CommandFlags flags)
        {
            ThrowIfNull(channel);
            // Unregister the subscription handler/queue, and if that returns true (last handler removed), also disconnect from the server
            return UnregisterSubscription(channel, handler, queue, out var sub)
                ? UnsubscribeFromServerAsync(sub, channel, flags, asyncState, false)
                : CompletedTask<bool>.Default(asyncState);
        }

        private Task<bool> UnsubscribeFromServerAsync(Subscription sub, RedisChannel channel, CommandFlags flags, object? asyncState, bool internalCall)
        {
            if (sub.GetCurrentServer() is ServerEndPoint oldOwner)
            {
                var message = sub.GetMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall);
                return multiplexer.ExecuteAsyncImpl(message, sub.Processor, asyncState, oldOwner);
            }
            return CompletedTask<bool>.FromResult(true, asyncState);
        }

        /// <summary>
        /// Unregisters a handler or queue and returns if we should remove it from the server.
        /// </summary>
        /// <returns><see langword="true"/> if we should remove the subscription from the server, <see langword="false"/> otherwise.</returns>
        private bool UnregisterSubscription(in RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, [NotNullWhen(true)] out Subscription? sub)
        {
            ThrowIfNull(channel);
            if (multiplexer.TryGetSubscription(channel, out sub))
            {
                if (handler == null & queue == null)
                {
                    // This was a blanket wipe, so clear it completely
                    sub.MarkCompleted();
                    multiplexer.TryRemoveSubscription(channel, out _);
                    return true;
                }
                else if (sub.Remove(handler, queue))
                {
                    // Or this was the last handler and/or queue, which also means unsubscribe
                    multiplexer.TryRemoveSubscription(channel, out _);
                    return true;
                }
            }
            return false;
        }

        public void UnsubscribeAll(CommandFlags flags = CommandFlags.None)
        {
            // TODO: Unsubscribe variadic commands to reduce round trips
            var subs = multiplexer.GetSubscriptions();
            foreach (var pair in subs)
            {
                if (subs.TryRemove(pair.Key, out var sub))
                {
                    sub.MarkCompleted();
                    UnsubscribeFromServer(sub, pair.Key, flags, false);
                }
            }
        }

        public Task UnsubscribeAllAsync(CommandFlags flags = CommandFlags.None)
        {
            // TODO: Unsubscribe variadic commands to reduce round trips
            Task? last = null;
            var subs = multiplexer.GetSubscriptions();
            foreach (var pair in subs)
            {
                if (subs.TryRemove(pair.Key, out var sub))
                {
                    sub.MarkCompleted();
                    last = UnsubscribeFromServerAsync(sub, pair.Key, flags, asyncState, false);
                }
            }
            return last ?? CompletedTask<bool>.Default(asyncState);
        }
    }
}
