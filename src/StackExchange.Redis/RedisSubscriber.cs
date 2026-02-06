using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial.Arenas;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        private RedisSubscriber? _defaultSubscriber;
        internal RedisSubscriber DefaultSubscriber => _defaultSubscriber ??= new RedisSubscriber(this, null);

        private readonly ConcurrentDictionary<RedisChannel, Subscription> subscriptions = new();

        internal ConcurrentDictionary<RedisChannel, Subscription> GetSubscriptions() => subscriptions;
        ConcurrentDictionary<RedisChannel, Subscription> IInternalConnectionMultiplexer.GetSubscriptions() => GetSubscriptions();

        internal int GetSubscriptionsCount() => subscriptions.Count;
        int IInternalConnectionMultiplexer.GetSubscriptionsCount() => GetSubscriptionsCount();

        internal Subscription GetOrAddSubscription(in RedisChannel channel, CommandFlags flags)
        {
            lock (subscriptions)
            {
                if (!subscriptions.TryGetValue(channel, out var sub))
                {
                    sub = channel.IsMultiNode ? new MultiNodeSubscription(flags) : new SingleNodeSubscription(flags);
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
                return sub.GetAnyCurrentServer();
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

        internal void OnMessage(in RedisChannel subscription, in RedisChannel channel, Sequence<RawResult> payload)
        {
            if (payload.IsSingleSegment)
            {
                foreach (var message in payload.FirstSpan)
                {
                    OnMessage(subscription, channel, message.AsRedisValue());
                }
            }
            else
            {
                foreach (var message in payload)
                {
                    OnMessage(subscription, channel, message.AsRedisValue());
                }
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
                pair.Value.RemoveDisconnectedEndpoints();
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
            var subscriber = DefaultSubscriber;
            foreach (var pair in subscriptions)
            {
                count += pair.Value.EnsureSubscribedToServer(subscriber, pair.Key, flags, true);
            }
            return count;
        }

        internal enum SubscriptionAction
        {
            Subscribe,
            Unsubscribe,
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
                try { usePing = GetFeatures(default, flags, RedisCommand.PING, out _).PingOnSubscriber; }
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
            var msg = Message.Create(-1, flags, channel.GetPublishCommand(), channel, message);
            // if we're actively subscribed: send via that connection (otherwise, follow normal rules)
            return ExecuteSync(msg, ResultProcessor.Int64, server: multiplexer.GetSubscribedServer(channel));
        }

        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            ThrowIfNull(channel);
            var msg = Message.Create(-1, flags, channel.GetPublishCommand(), channel, message);
            // if we're actively subscribed: send via that connection (otherwise, follow normal rules)
            return ExecuteAsync(msg, ResultProcessor.Int64, server: multiplexer.GetSubscribedServer(channel));
        }

        void ISubscriber.Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => Subscribe(channel, handler, null, flags);

        public ChannelMessageQueue Subscribe(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var queue = new ChannelMessageQueue(channel, this);
            Subscribe(channel, null, queue, flags);
            return queue;
        }

        private int Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, CommandFlags flags)
        {
            ThrowIfNull(channel);
            if (handler == null && queue == null) { return 0; }

            var sub = multiplexer.GetOrAddSubscription(channel, flags);
            sub.Add(handler, queue);
            return sub.EnsureSubscribedToServer(this, channel, flags, false);
        }

        internal void ResubscribeToServer(Subscription sub, in RedisChannel channel, ServerEndPoint serverEndPoint, string cause)
        {
            // conditional: only if that's the server we were connected to, or "none"; we don't want to end up duplicated
            if (sub.TryRemoveEndpoint(serverEndPoint) || !sub.IsConnectedAny())
            {
                if (serverEndPoint.IsSubscriberConnected)
                {
                    // we'll *try* for a simple resubscribe, following any -MOVED etc, but if that fails: fall back
                    // to full reconfigure; importantly, note that we've already recorded the disconnect
                    var message = sub.GetSubscriptionMessage(channel, SubscriptionAction.Subscribe, CommandFlags.None, false);
                    _ = ExecuteAsync(message, sub.Processor, serverEndPoint).ContinueWith(
                        t => multiplexer.ReconfigureIfNeeded(serverEndPoint.EndPoint, false, cause: cause),
                        TaskContinuationOptions.OnlyOnFaulted);
                }
                else
                {
                    multiplexer.ReconfigureIfNeeded(serverEndPoint.EndPoint, false, cause: cause);
                }
            }
        }

        Task ISubscriber.SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => SubscribeAsync(channel, handler, null, flags);

        Task<ChannelMessageQueue> ISubscriber.SubscribeAsync(RedisChannel channel, CommandFlags flags) => SubscribeAsync(channel, flags);

        public async Task<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None, ServerEndPoint? server = null)
        {
            var queue = new ChannelMessageQueue(channel, this);
            await SubscribeAsync(channel, null, queue, flags, server).ForAwait();
            return queue;
        }

        private Task<int> SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, CommandFlags flags, ServerEndPoint? server = null)
        {
            ThrowIfNull(channel);
            if (handler == null && queue == null) { return CompletedTask<int>.Default(null); }

            var sub = multiplexer.GetOrAddSubscription(channel, flags);
            sub.Add(handler, queue);
            return sub.EnsureSubscribedToServerAsync(this, channel, flags, false, server);
        }

        public EndPoint? SubscribedEndpoint(RedisChannel channel) => multiplexer.GetSubscribedServer(channel)?.EndPoint;

        void ISubscriber.Unsubscribe(RedisChannel channel, Action<RedisChannel, RedisValue>? handler, CommandFlags flags)
            => Unsubscribe(channel, handler, null, flags);

        public bool Unsubscribe(in RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, CommandFlags flags)
        {
            ThrowIfNull(channel);
            // Unregister the subscription handler/queue, and if that returns true (last handler removed), also disconnect from the server
            // ReSharper disable once SimplifyConditionalTernaryExpression
            return UnregisterSubscription(channel, handler, queue, out var sub)
                ? sub.UnsubscribeFromServer(this, channel, flags, false)
                : true;
        }

        Task ISubscriber.UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue>? handler, CommandFlags flags)
            => UnsubscribeAsync(channel, handler, null, flags);

        public Task<bool> UnsubscribeAsync(in RedisChannel channel, Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue, CommandFlags flags)
        {
            ThrowIfNull(channel);
            // Unregister the subscription handler/queue, and if that returns true (last handler removed), also disconnect from the server
            return UnregisterSubscription(channel, handler, queue, out var sub)
                ? sub.UnsubscribeFromServerAsync(this, channel, flags, asyncState, false)
                : CompletedTask<bool>.Default(asyncState);
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

        // TODO: We need a new api to support SUNSUBSCRIBE all. Calling this now would unsubscribe both sharded and unsharded channels.
        public void UnsubscribeAll(CommandFlags flags = CommandFlags.None)
        {
            // TODO: Unsubscribe variadic commands to reduce round trips
            var subs = multiplexer.GetSubscriptions();
            foreach (var pair in subs)
            {
                if (subs.TryRemove(pair.Key, out var sub))
                {
                    sub.MarkCompleted();
                    sub.UnsubscribeFromServer(this, pair.Key, flags, false);
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
                    last = sub.UnsubscribeFromServerAsync(this, pair.Key, flags, asyncState, false);
                }
            }
            return last ?? CompletedTask<bool>.Default(asyncState);
        }
    }
}
