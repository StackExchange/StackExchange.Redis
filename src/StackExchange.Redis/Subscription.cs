using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    /// <summary>
    /// This is the record of a single subscription to a redis server.
    /// It's the singular channel (which may or may not be a pattern), to one or more handlers.
    /// We subscriber to a redis server once (for all messages) and execute 1-many handlers when a message arrives.
    /// </summary>
    internal abstract class Subscription
    {
        private Action<RedisChannel, RedisValue>? _handlers;
        private readonly object _handlersLock = new();
        private ChannelMessageQueue? _queues;
        public CommandFlags Flags { get; }
        public ResultProcessor.TrackSubscriptionsProcessor Processor { get; }

        internal abstract bool IsConnectedAny();
        internal abstract bool IsConnectedTo(EndPoint endpoint);

        /// <summary>
        /// Whether this subscription names an endpoint, connected there or not.
        /// </summary>
        /// <remarks>
        /// Deliberately distinct from <see cref="IsConnectedTo"/>, which asks whether the subscription is
        /// <i>live</i> there and answers out of the bridge. Retiring a server this names would abandon the
        /// subscription either way: one whose server is momentarily disconnected still intends to live on
        /// it, and a core that drives no bridge has no connected state here to report at all.
        /// </remarks>
        internal abstract bool NamesEndpoint(EndPoint endpoint);

        internal abstract void AddEndpoint(ServerEndPoint server);

        // conditional clear
        internal abstract bool TryRemoveEndpoint(ServerEndPoint expected);

        internal abstract void RemoveDisconnectedEndpoints();

        // returns the number of changes required
        internal abstract int EnsureSubscribedToServer(
            RedisSubscriber subscriber,
            in RedisChannel channel,
            CommandFlags flags,
            bool internalCall);

        // returns the number of changes required
        internal abstract Task<int> EnsureSubscribedToServerAsync(
            RedisSubscriber subscriber,
            RedisChannel channel,
            CommandFlags flags,
            bool internalCall,
            ServerEndPoint? server = null);

        internal abstract bool UnsubscribeFromServer(
            RedisSubscriber subscriber,
            in RedisChannel channel,
            CommandFlags flags,
            bool internalCall);

        internal abstract Task<bool> UnsubscribeFromServerAsync(
            RedisSubscriber subscriber,
            RedisChannel channel,
            CommandFlags flags,
            object? asyncState,
            bool internalCall);

        internal abstract int GetConnectionCount();

        internal abstract ServerEndPoint? GetAnyCurrentServer();

        public Subscription(CommandFlags flags)
        {
            Flags = flags;
            Processor = new ResultProcessor.TrackSubscriptionsProcessor(this);
        }

        /// <summary>
        /// Gets the configured (P)SUBSCRIBE or (P)UNSUBSCRIBE <see cref="Message"/> for an action.
        /// </summary>
        internal Message GetSubscriptionMessage(
            in RedisChannel channel,
            SubscriptionAction action,
            CommandFlags flags,
            bool internalCall)
        {
            const RedisChannel.RedisChannelOptions OPTIONS_MASK = ~(
                RedisChannel.RedisChannelOptions.KeyRouted | RedisChannel.RedisChannelOptions.IgnoreChannelPrefix);
            var command =
                action switch // note that the Routed flag doesn't impact the message here - just the routing
                {
                    SubscriptionAction.Subscribe => (channel.Options & OPTIONS_MASK) switch
                    {
                        RedisChannel.RedisChannelOptions.None => RedisCommand.SUBSCRIBE,
                        RedisChannel.RedisChannelOptions.MultiNode => RedisCommand.SUBSCRIBE,
                        RedisChannel.RedisChannelOptions.Pattern => RedisCommand.PSUBSCRIBE,
                        RedisChannel.RedisChannelOptions.Pattern | RedisChannel.RedisChannelOptions.MultiNode =>
                            RedisCommand.PSUBSCRIBE,
                        RedisChannel.RedisChannelOptions.Sharded => RedisCommand.SSUBSCRIBE,
                        _ => Unknown(action, channel.Options),
                    },
                    SubscriptionAction.Unsubscribe => (channel.Options & OPTIONS_MASK) switch
                    {
                        RedisChannel.RedisChannelOptions.None => RedisCommand.UNSUBSCRIBE,
                        RedisChannel.RedisChannelOptions.MultiNode => RedisCommand.UNSUBSCRIBE,
                        RedisChannel.RedisChannelOptions.Pattern => RedisCommand.PUNSUBSCRIBE,
                        RedisChannel.RedisChannelOptions.Pattern | RedisChannel.RedisChannelOptions.MultiNode =>
                            RedisCommand.PUNSUBSCRIBE,
                        RedisChannel.RedisChannelOptions.Sharded => RedisCommand.SUNSUBSCRIBE,
                        _ => Unknown(action, channel.Options),
                    },
                    _ => Unknown(action, channel.Options),
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

        private RedisCommand Unknown(SubscriptionAction action, RedisChannel.RedisChannelOptions options)
            => throw new ArgumentException(
                $"Unable to determine pub/sub operation for '{action}' against '{options}'");

        /// <summary>Whether the NEW core's connection is the one holding this subscription.</summary>
        /// <remarks>
        /// <para>
        /// <b>Whether a subscription is live is a question about ONE connection</b>, and while both cores
        /// exist the two give different answers: <see cref="ServerEndPoint.IsSubscriberConnected"/>
        /// describes the shipped bridge's socket, <c>RespNewCore.IsSubscriptionConnected</c> this core's.
        /// Ask the wrong one and a subscription reads as live because a connection it is not on happens to
        /// be up - after which nothing ever re-subscribes it, which is a silently dead subscription rather
        /// than an error.
        /// </para>
        /// <para>
        /// Recorded by whoever actually sent the (un)subscribe, in both directions: a subscription can
        /// MOVE between cores - a protocol downgrade re-homes one - so the shipped processor clears it
        /// again when a bridge establishes one. See design notes D2.5.
        /// </para>
        /// </remarks>
        private volatile bool _onNewCore;

        /// <summary>Where this core has a subscribe in flight; null when none is.</summary>
        /// <remarks>
        /// <para>
        /// <b>Separate from the placed endpoint, because "we are carrying this" and "the server has
        /// confirmed it" are different questions and conflating them loses subscriptions.</b> The ping
        /// gate wants the first - a fire-and-forget subscribe is flushed by a ping that has to go on the
        /// socket the subscribe went out on, and that is true from the moment it is written. But
        /// <c>IsConnectedAny</c> wants the second: answer it optimistically and a later
        /// <c>EnsureSubscribedToServer</c> skips as "already subscribed", so a subscribe that never landed
        /// is never retried. `Resp3HandshakeTests` reads that as a publish finding no subscribers at all.
        /// </para>
        /// <para>
        /// So the endpoint is still recorded on CONFIRMATION, and this is what the ping asks about in the
        /// window before it.
        /// </para>
        /// </remarks>
        private volatile ServerEndPoint? _sendingVia;

        /// <summary>Record that the shipped path holds this subscription.</summary>
        /// <remarks><inheritdoc cref="_onNewCore" path="/remarks/para[2]"/></remarks>
        internal void OnSubscribedViaBridge() => _onNewCore = false;

        /// <summary>Whether this subscription is live on a server, asked of whoever holds it.</summary>
        /// <param name="server">The server, or null when none is recorded.</param>
        /// <remarks><inheritdoc cref="_onNewCore" path="/remarks/para[1]"/></remarks>
        private protected bool IsLiveOn(ServerEndPoint? server)
        {
            if (server is null) return false;

            return _onNewCore
                ? server.Multiplexer.NewCore.IsSubscriptionConnected(server.EndPoint)
                : server.IsSubscriberConnected;
        }

        /// <summary>Whether this subscription is held by the new core on one endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// Asked by the subscriber's <c>Ping</c>, which needs the connection that holds THIS CLIENT's
        /// subscriptions rather than merely a subscription-shaped one: its fallback probe is to
        /// unsubscribe from something nobody subscribed to, which is only a round trip where a
        /// subscription exists.
        /// </remarks>
        internal bool IsHeldByNewCoreOn(EndPoint endpoint)
            => _onNewCore && (NamesEndpoint(endpoint) || Equals(_sendingVia?.EndPoint, endpoint));

        /// <summary>Whether a (un)subscribe for this subscription is on the wire right now.</summary>
        /// <remarks>
        /// <b>"Not live yet" and "nobody is doing anything about it" are different, and conflating them
        /// multiplies subscriptions.</b> The endpoint is recorded only on confirmation - deliberately, so
        /// that an unlanded subscribe stays retryable - which leaves a window where `IsConnectedAny` says
        /// no while a subscribe is already in flight. Every `EnsureSubscribedToServer` arriving in that
        /// window starts ANOTHER one, and for an unrouted channel `SelectServer` spreads them across
        /// nodes, so the client ends up subscribed on several at once. In a cluster a non-sharded
        /// `PUBLISH` is broadcast to every node, so each of those delivers: ten publishes, twenty
        /// deliveries, and the publishing node still reporting one subscriber -
        /// `ClusterTests.ClusterPubSub(withKeyPrefix: true)`. This core's establish-time re-subscribe
        /// feeds the same loop, which is how it reached three nodes rather than two.
        /// </remarks>
        private protected bool HasSendInFlight => _sendingVia is not null;

        /// <summary>Whether some connection is already carrying this subscription.</summary>
        /// <remarks>
        /// Asked so that ownership can be STICKY: a subscription already placed keeps going to the core
        /// that placed it, because the two cores' unsubscribes do not reach each other's sockets.
        /// </remarks>
        private protected abstract bool IsPlaced { get; }

        /// <summary>
        /// Send one (un)subscribe through the new core and record what it did; null when this core is not
        /// taking it, in which case the caller falls through to the shipped path.
        /// </summary>
        /// <param name="subscriber">Owns the multiplexer, and so the core.</param>
        /// <param name="channel">The channel or pattern.</param>
        /// <param name="action">Whether this subscribes or unsubscribes.</param>
        /// <param name="flags">The caller's flags.</param>
        /// <param name="server">The server to send to.</param>
        /// <remarks>
        /// <para>
        /// <b>Three declines, each one a lesson from a failing test rather than caution.</b> A channel
        /// that can be REDIRECTED stays on the shipped path: a sharded or key-routed subscribe sent to the
        /// wrong node answers <c>-MOVED</c> and then lives on the node it was redirected TO, so the server
        /// chosen before the send is the wrong answer and recording it is worse than not knowing
        /// (<c>ClusterShardedTests.SubscribeToWrongServerAsync</c>). A subscription that would SHARE the
        /// ordinary connection stays there too; see <c>RespNewCore.WouldSubscribeOnItsOwnSocket</c>. And
        /// no server selected is "ours, nothing to do yet" rather than "not ours": falling through there
        /// let the BRIDGE subscribe and then this core subscribe as well, which the server reports as two
        /// subscribers and which delivers everything twice (<c>Resp3HandshakeTests</c>, as
        /// <c>PUBLISH => :2</c>).
        /// </para>
        /// <para>
        /// <b>The bookkeeping is the caller's rather than inferred from the reply.</b> The shipped path
        /// reads the server off the connection its confirmation arrived on; here the server was chosen
        /// before the send, so for the channels this accepts it is simply known - which is also why the
        /// redirect case had to be declined rather than guessed at.
        /// </para>
        /// </remarks>
        internal Task? TrySendViaNewCore(
            RedisSubscriber subscriber,
            in RedisChannel channel,
            SubscriptionAction action,
            CommandFlags flags,
            ServerEndPoint? server)
        {
            if (!ConnectionMultiplexer.NewCoreEngine) return null;

            // refused before the no-server shortcut: "you have turned SUBSCRIBE off" is an answer the
            // caller gets now, not one that waits for a server, and a subscribe that quietly succeeded
            // against a disabled command would register a handler nothing can ever feed
            var command = PubSub.SubscribeCommand(channel, action == SubscriptionAction.Subscribe);
            subscriber.multiplexer.CommandMap.AssertAvailable(command);

            if (server is null) return Task.CompletedTask;

            // OWNERSHIP IS STICKY, and that is the difference between this working and leaking a
            // subscription. Whether this core would take a FRESH subscription changes over time - it
            // depends on the negotiated protocol, which is unknown at first - so deciding per call let one
            // core subscribe and the other unsubscribe, and the channel stayed subscribed on a connection
            // nobody was tracking. `Issue1101Tests.ExecuteWithUnsubscribe*` reads that as
            // "expected 0 subscribers, found 1" after unsubscribing everything.
            //
            // So: something already holding this subscription decides where the next command for it goes,
            // in either direction. Only a subscription nothing holds is free to be placed, and then it is
            // placed where the socket choice cannot change underneath it.
            // copied out of the `in` parameter, because the local function below cannot capture one
            var target = channel;

            // in-flight counts as placed, or two calls race each other onto different cores
            if (IsPlaced || _sendingVia is not null) return _onNewCore ? SendViaNewCore() : null;

            // A subscription that would SHARE the ordinary connection still goes to the shipped path, and
            // the write-time reroute does NOT replace this - the two do different jobs. Choosing the right
            // socket when the protocol is already known is the fast path; the reroute is the safety net
            // for a send composed before the protocol was known and written after it settled otherwise.
            //
            // Measured by removing this: `Resp3DowngradeTests` then fails on TIMING rather than
            // correctness - nothing is poisoned, but rerouting at the write has to DIAL the subscription
            // socket first, and the in-process server's transcript shows `PUBLISH => :0` landing before
            // the re-subscribe arrives. The shipped core does not pay that because it dials its
            // subscription bridge the moment a downgrade is detected. Doing the same here is what unblocks
            // removing the decline; see design notes D2.5.
            var core = subscriber.multiplexer.NewCore;
            if (!core.WouldSubscribeOnItsOwnSocket(server.EndPoint)) return null;

            return SendViaNewCore();

            Task SendViaNewCore()
            {
                var context = new RespPubSub(subscriber.multiplexer.NewCore.SubscriptionContext(server.EndPoint));

                // Recorded at SEND time for a subscribe, not on completion, and that is the
                // fire-and-forget case taken seriously rather than an optimisation: a caller who declines
                // the outcome then immediately pings the subscriber to flush it, and the ping has to find
                // this core holding the subscription or it goes out on the other one's socket and flushes
                // nothing - PubSubTests.TestBasicPubSubFireAndForget. Where it turns out wrong, the next
                // EnsureSubscriptions corrects it, because IsLiveOn then answers false.
                if (action == SubscriptionAction.Subscribe)
                {
                    // ownership now, so a fire-and-forget caller's flush finds the right socket; the
                    // ENDPOINT only on confirmation, so nothing reads this as "already subscribed"
                    _onNewCore = true;
                    _sendingVia = server;
                    return Settle(
                        context.SubscribeAsync(target, Flags | flags),
                        this,
                        server,
                        place: true,
                        subscriber: subscriber,
                        placed: target,
                        command: command,
                        flags: Flags | flags);
                }

                return Settle(context.UnsubscribeAsync(target, Flags | flags), this, server);
            }

            static async Task Settle(
                ValueTask<long> pending,
                Subscription? self = null,
                ServerEndPoint? server = null,
                bool place = false,
                RedisSubscriber? subscriber = null,
                RedisChannel placed = default,
                RedisCommand command = default,
                CommandFlags flags = default)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                finally
                {
                    if (place && self is not null) self._sendingVia = null;
                }

                if (self is null || server is null) return;

                if (!place)
                {
                    self.TryRemoveEndpoint(server);
                    return;
                }

                // WHERE IT ENDED UP, which is not always where it was aimed: a sharded or key-routed
                // subscribe sent to the wrong node answers -MOVED, and following that both moves the
                // subscription and teaches this core where the slot went. So the slot's owner afterwards
                // is the answer, and the endpoint is re-resolved rather than assumed. A channel that is
                // not key-routed has no slot and answers null, in which case it is where it was aimed.
                if (subscriber is not null
                    && subscriber.multiplexer.NewCore.EndpointForChannel(placed, command, flags) is { } landed
                    && !Equals(landed, server.EndPoint)
                    && subscriber.multiplexer.GetServerEndPoint(landed, ServerProvenance.Configured, activate: false)
                        is { } moved)
                {
                    self.AddEndpoint(moved);
                    return;
                }

                self.AddEndpoint(server);
            }
        }

        /// <summary>Block for a send, unless the caller declined the outcome.</summary>
        /// <param name="subscriber">Supplies the timeout.</param>
        /// <param name="sent">The send.</param>
        /// <param name="flags">The caller's flags.</param>
        /// <remarks>
        /// <b>Fire-and-forget must not block here, and that is a deadlock rather than a slowdown.</b>
        /// <c>EnsureSubscriptions</c> is called fire-and-forget from inside a <c>SetResultCore</c> - the
        /// shipped code says so where it calls it - so a synchronous wait on that path waits for a reply
        /// that cannot be read until the reader it is blocking returns. The send has already been issued
        /// by the time this is reached.
        /// </remarks>
        private protected static void WaitUnlessDeclined(RedisSubscriber subscriber, Task sent, CommandFlags flags)
        {
            if ((flags & CommandFlags.FireAndForget) != 0) return;

            sent.Wait(subscriber.multiplexer.TimeoutMilliseconds);
        }

        /// <summary>Await a send and answer true, for the callers that promise a bool.</summary>
        /// <param name="sent">The send.</param>
        private protected static async Task<bool> AsTrue(Task sent)
        {
            await sent.ForAwait();
            return true;
        }

        public void Add(Action<RedisChannel, RedisValue>? handler, ChannelMessageQueue? queue)
        {
            if (handler != null)
            {
                lock (_handlersLock)
                {
                    _handlers += handler;
                }
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
                lock (_handlersLock)
                {
                    _handlers -= handler;
                }
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
            lock (_handlersLock)
            {
                _handlers = null;
            }

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
    }

    // used for most subscriptions; routed to a single node
    internal sealed class SingleNodeSubscription(CommandFlags flags) : Subscription(flags)
    {
        internal override bool IsConnectedAny() => IsLiveOn(_currentServer);

        internal override int GetConnectionCount() => IsConnectedAny() ? 1 : 0;

        internal override bool IsConnectedTo(EndPoint endpoint)
        {
            var server = _currentServer;
            return server is not null && server.EndPoint == endpoint && IsLiveOn(server);
        }

        internal override void AddEndpoint(ServerEndPoint server) => _currentServer = server;

        /// <inheritdoc/>
        private protected override bool IsPlaced => Volatile.Read(ref _currentServer) is not null;

        internal override bool NamesEndpoint(EndPoint endpoint)
            => Volatile.Read(ref _currentServer) is { } server && server.EndPoint == endpoint;

        internal override bool TryRemoveEndpoint(ServerEndPoint expected)
        {
            if (_currentServer == expected)
            {
                _currentServer = null;
                return true;
            }

            return false;
        }

        internal override bool UnsubscribeFromServer(
            RedisSubscriber subscriber,
            in RedisChannel channel,
            CommandFlags flags,
            bool internalCall)
        {
            var server = _currentServer;
            if (server is not null)
            {
                if (TrySendViaNewCore(subscriber, channel, SubscriptionAction.Unsubscribe, flags, server) is { } sent)
                {
                    WaitUnlessDeclined(subscriber, sent, flags);
                    return true;
                }

                var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall);
                return subscriber.multiplexer.ExecuteSyncImpl(message, Processor, server);
            }

            return true;
        }

        internal override Task<bool> UnsubscribeFromServerAsync(
            RedisSubscriber subscriber,
            RedisChannel channel,
            CommandFlags flags,
            object? asyncState,
            bool internalCall)
        {
            var server = _currentServer;
            if (server is not null)
            {
                if (TrySendViaNewCore(subscriber, channel, SubscriptionAction.Unsubscribe, flags, server) is { } sent)
                {
                    return AsTrue(sent);
                }

                var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall);
                return subscriber.multiplexer.ExecuteAsyncImpl(message, Processor, asyncState, server);
            }

            return CompletedTask<bool>.FromResult(true, asyncState);
        }

        private ServerEndPoint? _currentServer;
        internal ServerEndPoint? GetCurrentServer() => Volatile.Read(ref _currentServer);

        internal override ServerEndPoint? GetAnyCurrentServer() => Volatile.Read(ref _currentServer);

        /// <summary>
        /// Evaluates state and if we're not currently connected, clears the server reference.
        /// </summary>
        internal override void RemoveDisconnectedEndpoints()
        {
            var server = _currentServer;
            if (server is not null && !IsLiveOn(server))
            {
                _currentServer = null;
            }
        }

        internal override int EnsureSubscribedToServer(
            RedisSubscriber subscriber,
            in RedisChannel channel,
            CommandFlags flags,
            bool internalCall)
        {
            RemoveIncorrectRouting(subscriber, in channel, flags, internalCall);
            if (IsConnectedAny() || HasSendInFlight) return 0;

            // we're not appropriately connected, so blank it out for eligible reconnection
            _currentServer = null;
            var message = GetSubscriptionMessage(channel, SubscriptionAction.Subscribe, flags, internalCall);
            var selected = subscriber.multiplexer.SelectServer(message);

            // the message is still BUILT, because it is what routes: SelectServer reads the command, the
            // flags and the channel off it, and routing a subscription must not drift between the two
            if (TrySendViaNewCore(subscriber, channel, SubscriptionAction.Subscribe, flags, selected) is { } sent)
            {
                WaitUnlessDeclined(subscriber, sent, flags);
                return 1;
            }

            _ = subscriber.ExecuteSync(message, Processor, selected);
            return 1;
        }

        private void RemoveIncorrectRouting(RedisSubscriber subscriber, in RedisChannel channel, CommandFlags flags, bool internalCall)
        {
            // only applies to cluster, when using key-routed channels (sharded, explicit key-routed, or
            // a single-key keyspace notification); is the subscribed server still handling that channel?
            if (channel.IsKeyRouted && _currentServer is { ServerType: ServerType.Cluster } current)
            {
                // if we consider replicas, there can be multiple valid target servers; we can't ask
                // "is this the correct server?", but we can ask "is it suitable?", based on the slot
                if (!subscriber.multiplexer.ServerSelectionStrategy.CanServeSlot(_currentServer, channel))
                {
                    var fireAndForget = flags | CommandFlags.FireAndForget;
                    if (TrySendViaNewCore(subscriber, channel, SubscriptionAction.Unsubscribe, fireAndForget, current) is null)
                    {
                        var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, fireAndForget, internalCall);
                        subscriber.multiplexer.ExecuteSyncImpl(message, Processor, current);
                    }

                    _currentServer = null; // pre-emptively disconnect - F+F
                }
            }
        }

        internal override async Task<int> EnsureSubscribedToServerAsync(
            RedisSubscriber subscriber,
            RedisChannel channel,
            CommandFlags flags,
            bool internalCall,
            ServerEndPoint? server = null)
        {
            RemoveIncorrectRouting(subscriber, in channel, flags, internalCall);
            if (IsConnectedAny() || HasSendInFlight) return 0;

            // we're not appropriately connected, so blank it out for eligible reconnection
            _currentServer = null;
            var message = GetSubscriptionMessage(channel, SubscriptionAction.Subscribe, flags, internalCall);
            server ??= subscriber.multiplexer.SelectServer(message);

            if (TrySendViaNewCore(subscriber, channel, SubscriptionAction.Subscribe, flags, server) is { } sent)
            {
                await sent.ForAwait();
                return 1;
            }

            await subscriber.ExecuteAsync(message, Processor, server).ForAwait();
            return 1;
        }
    }

    // used for keyspace subscriptions, which are routed to multiple nodes
    internal sealed class MultiNodeSubscription(CommandFlags flags) : Subscription(flags)
    {
        private readonly ConcurrentDictionary<EndPoint, ServerEndPoint> _servers = new();

        internal override bool IsConnectedAny()
        {
            foreach (var server in _servers)
            {
                if (IsLiveOn(server.Value)) return true;
            }

            return false;
        }

        internal override int GetConnectionCount()
        {
            int count = 0;
            foreach (var server in _servers)
            {
                if (IsLiveOn(server.Value)) count++;
            }

            return count;
        }

        internal override bool IsConnectedTo(EndPoint endpoint)
            => _servers.TryGetValue(endpoint, out var server) && IsLiveOn(server);

        internal override bool NamesEndpoint(EndPoint endpoint) => _servers.ContainsKey(endpoint);

        /// <inheritdoc/>
        private protected override bool IsPlaced => !_servers.IsEmpty;

        internal override void AddEndpoint(ServerEndPoint server)
        {
            var ep = server.EndPoint;
            if (!_servers.TryAdd(ep, server))
            {
                _servers[ep] = server;
            }
        }

        internal override bool TryRemoveEndpoint(ServerEndPoint expected)
        {
            return _servers.TryRemove(expected.EndPoint, out _);
        }

        internal override ServerEndPoint? GetAnyCurrentServer()
        {
            ServerEndPoint? last = null;
            // prefer actively connected servers, but settle for anything
            foreach (var server in _servers)
            {
                last = server.Value;
                if (IsLiveOn(last))
                {
                    break;
                }
            }

            return last;
        }

        internal override void RemoveDisconnectedEndpoints()
        {
            // This looks more complicated than it is, because of avoiding mutating the collection
            // while iterating; instead, buffer any removals in a scratch buffer, and remove them in a second pass.
            EndPoint[] scratch = [];
            int count = 0;
            foreach (var server in _servers)
            {
                // NOT connected is what gets removed - this read `IsSubscriberConnected` with no negation,
                // which is inverted against both this method's name and the single-node sibling. The
                // effect was benign because GetSubscriptionChange re-checks liveness rather than trusting
                // the record, so the cost was redundant SUBSCRIBEs on reconfigure rather than a lost
                // subscription; it is still the opposite of what it says.
                if (!IsLiveOn(server.Value))
                {
                    // flag for removal
                    if (scratch.Length == count) // need to resize the scratch buffer, using the pool
                    {
                        // let the array pool worry about min-sizing etc
                        var newLease = ArrayPool<EndPoint>.Shared.Rent(count + 1);
                        scratch.CopyTo(newLease, 0);
                        ArrayPool<EndPoint>.Shared.Return(scratch);
                        scratch = newLease;
                    }

                    scratch[count++] = server.Key;
                }
            }

            // did we find anything to remove?
            if (count != 0)
            {
                foreach (var ep in scratch.AsSpan(0, count))
                {
                    _servers.TryRemove(ep, out _);
                }
            }

            ArrayPool<EndPoint>.Shared.Return(scratch);
        }

        internal override int EnsureSubscribedToServer(
            RedisSubscriber subscriber,
            in RedisChannel channel,
            CommandFlags flags,
            bool internalCall)
        {
            int delta = 0;
            var muxer = subscriber.multiplexer;
            foreach (var server in muxer.GetServerSnapshot())
            {
                var change = GetSubscriptionChange(server, flags);
                if (change is not null)
                {
                    if (TrySendViaNewCore(subscriber, channel, change.GetValueOrDefault(), flags, server) is { } sent)
                    {
                        WaitUnlessDeclined(subscriber, sent, flags);
                    }
                    else
                    {
                        var message = GetSubscriptionMessage(channel, change.GetValueOrDefault(), flags, internalCall);
                        subscriber.ExecuteSync(message, Processor, server);
                    }

                    delta++;
                }
            }

            return delta;
        }

        private SubscriptionAction? GetSubscriptionChange(ServerEndPoint server, CommandFlags flags)
        {
            // exclude sentinel, and only use replicas if we're explicitly asking for them
            bool useReplica = (Flags & CommandFlags.DemandReplica) != 0;
            bool shouldBeConnected = server.ServerType != ServerType.Sentinel & server.IsReplica == useReplica;
            if (shouldBeConnected == IsConnectedTo(server.EndPoint))
            {
                return null;
            }
            return shouldBeConnected ? SubscriptionAction.Subscribe : SubscriptionAction.Unsubscribe;
        }

        internal override async Task<int> EnsureSubscribedToServerAsync(
            RedisSubscriber subscriber,
            RedisChannel channel,
            CommandFlags flags,
            bool internalCall,
            ServerEndPoint? server = null)
        {
            int delta = 0;
            var muxer = subscriber.multiplexer;
            var snapshot = muxer.GetServerSnaphotMemory();
            var len = snapshot.Length;
            for (int i = 0; i < len; i++)
            {
                var loopServer = snapshot.Span[i]; // spans and async do not mix well
                if (server is null || server == loopServer) // either "all" or "just the one we passed in"
                {
                    var change = GetSubscriptionChange(loopServer, flags);
                    if (change is not null)
                    {
                        if (TrySendViaNewCore(subscriber, channel, change.GetValueOrDefault(), flags, loopServer) is { } sent)
                        {
                            await sent.ForAwait();
                        }
                        else
                        {
                            var message = GetSubscriptionMessage(channel, change.GetValueOrDefault(), flags, internalCall);
                            await subscriber.ExecuteAsync(message, Processor, loopServer).ForAwait();
                        }

                        delta++;
                    }
                }
            }

            return delta;
        }

        internal override bool UnsubscribeFromServer(
            RedisSubscriber subscriber,
            in RedisChannel channel,
            CommandFlags flags,
            bool internalCall)
        {
            bool any = false;
            foreach (var server in _servers)
            {
                if (TrySendViaNewCore(subscriber, channel, SubscriptionAction.Unsubscribe, flags, server.Value) is { } sent)
                {
                    WaitUnlessDeclined(subscriber, sent, flags);
                    any = true;
                    continue;
                }

                var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall);
                any |= subscriber.ExecuteSync(message, Processor, server.Value);
            }

            return any;
        }

        internal override async Task<bool> UnsubscribeFromServerAsync(
            RedisSubscriber subscriber,
            RedisChannel channel,
            CommandFlags flags,
            object? asyncState,
            bool internalCall)
        {
            bool any = false;
            foreach (var server in _servers)
            {
                if (TrySendViaNewCore(subscriber, channel, SubscriptionAction.Unsubscribe, flags, server.Value) is { } sent)
                {
                    await sent.ForAwait();
                    any = true;
                    continue;
                }

                var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall);
                any |= await subscriber.ExecuteAsync(message, Processor, server.Value).ForAwait();
            }

            return any;
        }
    }
}
