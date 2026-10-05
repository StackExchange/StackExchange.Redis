using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>Whether the new core holds any of this client's subscriptions on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// <b>What the subscriber's <c>Ping</c> needs to know, and the reason it is a different question
        /// from "does that core have a subscription socket".</b> A server that will not answer <c>PING</c>
        /// in subscriber mode is pinged by unsubscribing from something nobody subscribed to - which is a
        /// round trip only where a subscription already exists, because an unsubscribe against a
        /// connection holding none has nothing to confirm. So the ping has to go on the connection that
        /// holds THIS CLIENT's subscriptions, and <c>Subscription</c> is the only thing that knows which
        /// core that is. See design notes D2.5.
        /// </remarks>
        internal bool NewCoreHoldsSubscriptionsOn(EndPoint endpoint)
        {
            foreach (var pair in subscriptions)
            {
                if (pair.Value.IsHeldByNewCoreOn(endpoint)) return true;
            }

            return false;
        }

        /// <summary>Whether this core owns any subscription at all, placed or not.</summary>
        /// <remarks><inheritdoc cref="Subscription.IsOwnedByNewCore" path="/remarks"/></remarks>
        internal bool NewCoreOwnsAnySubscription()
        {
            foreach (var pair in subscriptions)
            {
                if (pair.Value.IsOwnedByNewCore) return true;
            }

            return false;
        }

        /// <summary>Forget every subscription recorded against an endpoint, because its socket is new.</summary>
        /// <param name="endpoint">The endpoint whose subscription connection has just been established.</param>
        /// <remarks>
        /// <b>A fresh socket carries no subscriptions, so a record naming it is stale by definition</b> -
        /// and nothing else notices, which is the gap this closes. Liveness for this core is
        /// "is there a connected subscription socket for that endpoint", which a REPLACEMENT socket
        /// satisfies while carrying nothing; the re-ensure that should follow a reconnect then reads
        /// "already subscribed" and does nothing. The shipped core gets this for free, because its
        /// liveness is the bridge connection's own state and a disconnect clears the records on the way
        /// through.
        /// </remarks>
        internal void ForgetSubscriptionsOn(EndPoint endpoint)
        {
            foreach (var pair in subscriptions)
            {
                // NOT one that is landing right now. A subscribe in flight is very often the reason this
                // socket is being established at all, and its record is not stale - it is about to be
                // confirmed. Forgetting it anyway lets the re-ensure below treat the channel as unplaced
                // and place it somewhere else, which for a key-routed channel means the slot's owner
                // rather than the server the caller explicitly asked for.
                // `ClusterShardedTests.SubscribeToWrongServerAsync(sharded: false)` catches exactly that,
                // intermittently, as the subscription arriving at the right node instead of the chosen one.
                if (pair.Value.HasSendInFlight) continue;

                if (pair.Value.NamesEndpoint(endpoint)
                    && TryResolveServerEndPoint(endpoint) is { } server)
                {
                    pair.Value.TryRemoveEndpoint(server);
                }
            }
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
        /// Whether any subscription names this endpoint, whether or not it is connected there.
        /// </summary>
        /// <remarks>
        /// <b>The registry is asked, not the bridge.</b> A bridge's subscription counter only knows about
        /// subscriptions that bridge itself carried, so a core that does not drive one leaves it at zero -
        /// and a server whose only work is a subscription would then look idle, and be retired out from
        /// under the deliveries it is still receiving. What is true for both cores is the registry entry.
        /// </remarks>
        internal bool AnySubscriptionNames(EndPoint endpoint)
        {
            foreach (var pair in subscriptions)
            {
                if (pair.Value.NamesEndpoint(endpoint)) return true;
            }
            return false;
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
                try
                {
                    count += pair.Value.EnsureSubscribedToServer(subscriber, pair.Key, flags, true);
                }
                catch (Exception ex)
                {
                    OnInternalError(ex);
                }
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
        /// <inheritdoc/>
        /// <remarks>
        /// Straight through to <see cref="RedisBase.GetContext"/>, which for a subscriber is still the
        /// throw: nothing about pub/sub composes through the context surface yet.
        /// </remarks>
        RespContext IRespTarget.Context => GetContext();

        internal RedisSubscriber(ConnectionMultiplexer multiplexer, object? asyncState) : base(multiplexer, asyncState)
        {
        }

        public EndPoint? IdentifyEndpoint(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMSUB, channel);
            msg.SetInternalCall();
            if (TryIdentifyViaNewCore(msg, channel, flags) is { } pending) return pending.GetAwaiter().GetResult();

            return ExecuteSync(msg, ResultProcessor.ConnectionIdentity);
        }

        public Task<EndPoint?> IdentifyEndpointAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMSUB, channel);
            msg.SetInternalCall();
            if (TryIdentifyViaNewCore(msg, channel, flags) is { } pending) return pending;

            return ExecuteAsync(msg, ResultProcessor.ConnectionIdentity);
        }

        /// <summary>
        /// Ask a server about a channel and answer which server that was; null when this core is not
        /// carrying it.
        /// </summary>
        /// <param name="routing">The message that would have been sent, used to choose the server.</param>
        /// <param name="channel">The channel to ask about.</param>
        /// <param name="flags">The caller's flags.</param>
        /// <remarks>
        /// <para>
        /// <b>The answer is the server that was ASKED, which is the same thing the shipped path reports
        /// by a longer route.</b> <c>ResultProcessor.ConnectionIdentity</c> ignores the payload entirely
        /// and reads the endpoint off the connection the reply arrived on - and that connection is
        /// whichever one routing chose. Here the choice is made before the send, so it is already known;
        /// the round trip is what makes it an observation rather than a guess, and it still happens.
        /// </para>
        /// <para>
        /// <b>Safe to answer from the pre-chosen server because <c>PUBSUB NUMSUB</c> cannot be
        /// redirected</b>, being keyless and node-local - which is exactly what made the SUBSCRIBE case
        /// hard and this one easy. A sharded channel does not change that: the shipped path asks
        /// <c>NUMSUB</c> regardless of the channel's kind, so this does too.
        /// </para>
        /// </remarks>
        private Task<EndPoint?>? TryIdentifyViaNewCore(Message routing, in RedisChannel channel, CommandFlags flags)
        {
            if (!ConnectionMultiplexer.NewCoreEngine) return null;
            if (multiplexer.SelectServer(routing) is not { } server) return null;

            var endpoint = server.EndPoint;
            var context = new RespPubSub((RespContext)multiplexer.NewCore.ServerContext(endpoint));
            return Answer(context.SubscriberCountAsync(channel, flags), endpoint);

            static async Task<EndPoint?> Answer(ValueTask<long> pending, EndPoint endpoint)
            {
                _ = await pending.ConfigureAwait(false);
                return endpoint;
            }
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
            if (TryPingViaNewCore(msg, flags) is { } pending) return pending.GetAwaiter().GetResult();

            return ExecuteSync(msg, ResultProcessor.ResponseTimer);
        }

        public override Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = CreatePingMessage(flags);
            if (TryPingViaNewCore(msg, flags) is { } pending) return pending;

            return ExecuteAsync(msg, ResultProcessor.ResponseTimer);
        }

        /// <summary>
        /// Time a round trip on the connection deliveries arrive on; null when that is not this core's.
        /// </summary>
        /// <param name="routing">The message that would have been sent, used to choose the server and the spelling.</param>
        /// <param name="flags">The caller's flags.</param>
        /// <remarks>
        /// <para>
        /// <b>This has to travel on the SUBSCRIBER connection, and that is the whole point of it.</b>
        /// Callers ping the subscriber to flush it - "has my <c>SUBSCRIBE</c> been processed yet?" - so a
        /// ping on any other socket answers a question nobody asked, and a publish issued afterwards can
        /// still overtake the subscribe at the server. <c>PubSubTests.TestBasicPubSubFireAndForget</c> is
        /// that sequence exactly, and a five-second wait cannot recover a message that was never going to
        /// arrive.
        /// </para>
        /// <para>
        /// <b>And only when this core holds subscriptions there</b>, not merely when it has a subscription
        /// socket: the fallback probe for a server that will not answer <c>PING</c> in subscriber mode is
        /// an unsubscribe from something nobody subscribed to, which has nothing to confirm on a
        /// connection holding none. See <c>ConnectionMultiplexer.NewCoreHoldsSubscriptionsOn</c>.
        /// </para>
        /// </remarks>
        private Task<TimeSpan>? TryPingViaNewCore(Message routing, CommandFlags flags)
        {
            if (!ConnectionMultiplexer.NewCoreEngine) return null;
            if (multiplexer.SelectServer(routing) is not { } server) return null;
            if (!multiplexer.NewCoreHoldsSubscriptionsOn(server.EndPoint))
            {
                // No subscriptions here, so no subscriber connection to flush - and handing back null sent the
                // ping down the shipped path, which built a bridge (and dialled it) just to answer. The round
                // trip that remains meaningful is the one on this core's own connection to that server.
                var direct = new RespContext(multiplexer.RawConfig.CommandMap, database: -1)
                    .WithExecutor(multiplexer.NewCore.ServerExecutor(server.EndPoint));
                return Measured(new RespDatabaseContext(direct).PingMeasureAsync(flags));
            }

            var context = multiplexer.NewCore.SubscriptionContext(server.EndPoint);
            if (routing.Command == RedisCommand.PING)
            {
                return Measured(new RespDatabaseContext(context).PingMeasureAsync(flags));
            }

            // the timestamp is taken BEFORE the send, not before the await: a ValueTask handed to a timing
            // helper has already done its writing by the time the helper runs
            var started = Stopwatch.GetTimestamp();
            return Timed(
                new RespPubSub(context).UnsubscribeAsync(RedisChannel.Literal(multiplexer.UniqueId), flags), started);

            static async Task<TimeSpan> Measured(ValueTask<TimeSpan> pending) => await pending.ConfigureAwait(false);

            static async Task<TimeSpan> Timed(ValueTask<long> pending, long started)
            {
                _ = await pending.ConfigureAwait(false);
                return TimeSpan.FromTicks(
                    (long)((TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)
                        * (Stopwatch.GetTimestamp() - started)));
            }
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

        /// <summary>This subscriber's context, when the other core is the one sending.</summary>
        /// <remarks>
        /// <b>Publishing is the one pub/sub member that is simply a command</b>, so it is the one that can
        /// move before the rest of the surface does. It routes itself: <c>PubSub.PublishAsync</c> asks the
        /// executor to resolve the channel, which prefers the server this client already holds a
        /// subscription on - the same rule the shipped path expresses by passing
        /// <c>GetSubscribedServer</c> as the server, and the stronger one for <c>SPUBLISH</c>, where the
        /// slot decides and a subscription elsewhere cannot override it.
        /// <para>
        /// Worth moving on volume alone: an inventory of everything still travelling as a <c>Message</c>
        /// under the engine flag came to 22,542 across the suite, and <b>15,820 of them were this member</b>
        /// - sixty-nine per cent, and the easiest sixty-nine per cent, because subscribing is the part that
        /// registers a handler and outlives the call.
        /// </para>
        /// </remarks>
        private RespDatabaseContext PubSubContext
            => _pubSubContext ??= multiplexer.NewCore.GetDatabase(
                multiplexer.RawConfig.DefaultDatabase.GetValueOrDefault());

        private RespDatabaseContext? _pubSubContext;

        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            ThrowIfNull(channel);

            if (ConnectionMultiplexer.NewCoreEngine)
            {
                var settling = multiplexer.NewCore.SubscriptionsSettling();
                if (!settling.IsCompleted)
                {
                    // bounded, and deliberately: this holds back a publish so a re-placed subscription can
                    // get in front of it, which is worth a wait and is not worth a hang
                    settling.Wait(multiplexer.TimeoutMilliseconds);
                }

                var context = PubSubContext;
                return TransitionalSync.Wait(
                    context.PubSub.PublishAsync(channel, message, flags), multiplexer, context.Raw.Executor);
            }

            var msg = Message.Create(-1, flags, channel.GetPublishCommand(), channel, message);
            // if we're actively subscribed: send via that connection (otherwise, follow normal rules)
            return ExecuteSync(msg, ResultProcessor.Int64, server: multiplexer.GetSubscribedServer(channel));
        }

        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            ThrowIfNull(channel);

            if (ConnectionMultiplexer.NewCoreEngine)
            {
                var settling = multiplexer.NewCore.SubscriptionsSettling();
                return settling.IsCompleted
                    ? PubSubContext.PubSub.PublishAsync(channel, message, flags).AsTask(asyncState, flags)
                    : PublishWhenSettledAsync(settling, channel, message, flags);
            }

            var msg = Message.Create(-1, flags, channel.GetPublishCommand(), channel, message);
            // if we're actively subscribed: send via that connection (otherwise, follow normal rules)
            return ExecuteAsync(msg, ResultProcessor.Int64, server: multiplexer.GetSubscribedServer(channel));
        }

        /// <summary>Publish once the subscriptions being re-placed are back on the wire.</summary>
        /// <param name="settling"><see cref="RespNewCore.SubscriptionsSettling"/>.</param>
        /// <param name="channel">The channel to publish to.</param>
        /// <param name="message">The message to publish.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        private async Task<long> PublishWhenSettledAsync(
            Task settling,
            RedisChannel channel,
            RedisValue message,
            CommandFlags flags)
        {
            // bounded for the reason the synchronous path states; and the publish happens either way,
            // because a publish that reaches nobody is still better than one that never happens
            await Task.WhenAny(settling, Task.Delay(multiplexer.TimeoutMilliseconds)).ConfigureAwait(false);
            return await PubSubContext.PubSub.PublishAsync(channel, message, flags)
                .AsTask(asyncState, flags).ConfigureAwait(false);
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
                    // On the core that owns this subscription, and that is not a refinement under the
                    // engine flag - it is the difference between working and poisoning a connection. The
                    // shipped send below resolves to the INTERACTIVE bridge once there is no subscription
                    // bridge, so under RESP2 it would put the connection carrying ordinary commands into
                    // subscriber mode. Same shape either way: try the simple resubscribe, which follows
                    // any -MOVED, and fall back to a full reconfigure if it faults.
                    var viaNewCore = sub.TrySendViaNewCore(
                        this, channel, SubscriptionAction.Subscribe, CommandFlags.None, serverEndPoint);
                    if (viaNewCore is not null)
                    {
                        _ = viaNewCore.ContinueWith(
                            t => multiplexer.ReconfigureIfNeeded(serverEndPoint.EndPoint, false, cause: cause),
                            TaskContinuationOptions.OnlyOnFaulted);
                        return;
                    }

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
