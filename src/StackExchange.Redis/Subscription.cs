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
            bool internalCall,
            bool trackReply = false)
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
            var effectiveFlags = Flags | flags;
            if (trackReply) effectiveFlags &= ~CommandFlags.FireAndForget;
            var msg = Message.Create(-1, effectiveFlags, command, channel);
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

            return (_handlers == null) & (_queues == null);
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
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly object _stateLock = new();
        private ServerEndPoint? _currentServer;
        private PhysicalConnection? _currentConnection;
        private RestoreOperation? _pendingRestore;
        private System.Collections.Generic.List<CleanupRequest>? _cleanup;
        private bool _unsubscribed;

        internal override bool IsConnectedAny()
        {
            var connection = Volatile.Read(ref _currentConnection);
            return _currentServer is { IsSubscriberConnected: true }
                && (connection is null || ReferenceEquals(connection.BridgeCouldBeNull?.Physical, connection));
        }
        internal override int GetConnectionCount() => IsConnectedAny() ? 1 : 0;
        internal override bool IsConnectedTo(EndPoint endpoint)
            => _currentServer is { IsSubscriberConnected: true } server && server.EndPoint == endpoint;

        internal bool Accepts(PhysicalConnection connection)
            => !Volatile.Read(ref _unsubscribed) && ReferenceEquals(Volatile.Read(ref _currentConnection), connection);

        internal override void AddEndpoint(ServerEndPoint server) { lock (_stateLock) _currentServer = server; }

        internal override bool TryRemoveEndpoint(ServerEndPoint expected)
        {
            lock (_stateLock)
            {
                if (_currentServer != expected) return false;
                _currentServer = null;
                Volatile.Write(ref _currentConnection, null);
                return true;
            }
        }

        internal ServerEndPoint? GetCurrentServer() => Volatile.Read(ref _currentServer);
        internal override ServerEndPoint? GetAnyCurrentServer() => GetCurrentServer();

        internal override void RemoveDisconnectedEndpoints()
        {
            lock (_stateLock)
            {
                if (_currentServer is not null && !IsConnectedAny())
                {
                    _currentServer = null;
                    Volatile.Write(ref _currentConnection, null);
                }
            }
        }

        internal override int EnsureSubscribedToServer(RedisSubscriber subscriber, in RedisChannel channel, CommandFlags flags, bool internalCall)
        {
            bool fireAndForget = (flags & CommandFlags.FireAndForget) != 0;
            if (!Enter(subscriber, fireAndForget)) return 0;
            RestoreOperation? operation = null;
            bool transferred = false;
            try
            {
                operation = BeginRestore(subscriber, channel, flags, internalCall, null);
                if (operation is null) return 0;
                if (fireAndForget)
                {
                    transferred = true;
                    _ = CompleteRestoreAsync(operation).ObserveErrors();
                    return 1;
                }
                subscriber.ExecuteSync(operation.Message, operation.Processor, operation.Server);
                return 1;
            }
            finally
            {
                if (!transferred) EndOperation(operation);
            }
        }

        internal override async Task<int> EnsureSubscribedToServerAsync(RedisSubscriber subscriber, RedisChannel channel, CommandFlags flags, bool internalCall, ServerEndPoint? server = null)
        {
            bool fireAndForget = (flags & CommandFlags.FireAndForget) != 0;
            if (fireAndForget)
            {
                if (!Enter(subscriber, true)) return 0;
            }
            else if (!await _operationLock.WaitAsync(subscriber.multiplexer.AsyncTimeoutMilliseconds).ForAwait())
            {
                throw WaitingTimeout(subscriber);
            }
            RestoreOperation? operation = null;
            bool transferred = false;
            try
            {
                operation = BeginRestore(subscriber, channel, flags, internalCall, server);
                if (operation is null) return 0;
                if (fireAndForget)
                {
                    transferred = true;
                    _ = CompleteRestoreAsync(operation).ObserveErrors();
                    return 1;
                }
                await subscriber.ExecuteAsync(operation.Message, operation.Processor, operation.Server).ForAwait();
                return 1;
            }
            finally { if (!transferred) EndOperation(operation); }
        }

        private bool Enter(RedisSubscriber subscriber, bool fireAndForget)
        {
            if (_operationLock.Wait(fireAndForget ? 0 : subscriber.multiplexer.TimeoutMilliseconds)) return true;
            if (fireAndForget) return false;
            throw WaitingTimeout(subscriber);
        }

        private Exception WaitingTimeout(RedisSubscriber subscriber)
            => ExceptionFactory.Timeout(subscriber.multiplexer, "Timeout waiting for an in-flight subscription operation.", null, _currentServer);

        private RestoreOperation? BeginRestore(RedisSubscriber subscriber, RedisChannel channel, CommandFlags flags, bool internalCall, ServerEndPoint? server)
        {
            lock (_stateLock)
            {
                if (_unsubscribed) return null;
                RemoveIncorrectRouting(subscriber, channel, flags, internalCall);
                if (IsConnectedAny()) return null;
                _currentServer = null;
                Volatile.Write(ref _currentConnection, null);
                // Keep an actual reply task even for fire-and-forget restoration: sending is not acknowledgement.
                var message = GetSubscriptionMessage(channel, SubscriptionAction.Subscribe, flags, internalCall, trackReply: true);
                server ??= subscriber.multiplexer.SelectServer(message);
                return _pendingRestore = new RestoreOperation(this, subscriber, channel, message, server);
            }
        }

        private async Task CompleteRestoreAsync(RestoreOperation operation)
        {
            try { await operation.Subscriber.ExecuteAsync(operation.Message, operation.Processor, operation.Server).ForAwait(); }
            finally { EndOperation(operation); }
        }

        private void EndOperation(RestoreOperation? operation)
        {
            try
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_pendingRestore, operation)) _pendingRestore = null;
                    DrainCleanup();
                }
            }
            finally { _operationLock.Release(); }
        }

        private void OnSubscribed(RestoreOperation operation, PhysicalConnection connection)
        {
            var server = connection.BridgeCouldBeNull?.ServerEndPoint;
            if (server is null) return;
            lock (_stateLock)
            {
                if (ReferenceEquals(_pendingRestore, operation))
                {
                    _currentServer = server;
                    Volatile.Write(ref _currentConnection, connection);
                    return;
                }
                // A timed-out request can still be acknowledged. It must not replace a newer route.
                QueueCleanup(new CleanupRequest(operation.Subscriber, operation.Channel, server));
            }
        }

        private void QueueCleanup(CleanupRequest request)
        {
            lock (_stateLock)
            {
                (_cleanup ??= new()).Add(request);
                DrainCleanup();
            }
        }

        private void DrainCleanup()
        {
            // A redirected restore may not have its final endpoint yet. Reconcile after its acknowledgement.
            if (_pendingRestore is not null || _cleanup is null) return;
            var cleanup = _cleanup;
            _cleanup = null;
            foreach (var request in cleanup)
            {
                var muxer = request.Subscriber.multiplexer;
                if (muxer.TryGetSubscription(request.Channel, out var active) && !ReferenceEquals(active, this))
                {
                    if (active is SingleNodeSubscription single)
                    {
                        single.QueueCleanup(request);
                        continue;
                    }
                    if (active.IsConnectedTo(request.Server.EndPoint)) continue;
                }
                if (!_unsubscribed && ReferenceEquals(_currentServer, request.Server)) continue;
                try
                {
                    var message = GetSubscriptionMessage(request.Channel, SubscriptionAction.Unsubscribe, CommandFlags.FireAndForget, true);
                    muxer.ExecuteAsyncImpl(message, ResultProcessor.TrackSubscriptions, null, request.Server).ObserveErrors();
                }
                catch (Exception ex) { muxer.OnInternalError(ex); }
            }
        }

        internal override bool UnsubscribeFromServer(RedisSubscriber subscriber, in RedisChannel channel, CommandFlags flags, bool internalCall)
        {
            Volatile.Write(ref _unsubscribed, true);
            if ((flags & CommandFlags.FireAndForget) != 0)
            {
                UnsubscribeCoreAsync(subscriber, channel, flags, null, internalCall).ObserveErrors();
                return true;
            }
            Enter(subscriber, false);
            try
            {
                var server = _currentServer;
                if (server is null) return true;
                if (ForwardCleanup(subscriber, channel, server)) return true;
                var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall, trackReply: true);
                return subscriber.multiplexer.ExecuteSyncImpl(message, Processor, server);
            }
            finally { EndOperation(null); }
        }

        internal override Task<bool> UnsubscribeFromServerAsync(RedisSubscriber subscriber, RedisChannel channel, CommandFlags flags, object? asyncState, bool internalCall)
        {
            Volatile.Write(ref _unsubscribed, true);
            var task = UnsubscribeCoreAsync(subscriber, channel, flags, asyncState, internalCall);
            if ((flags & CommandFlags.FireAndForget) == 0) return task;
            task.ObserveErrors();
            return CompletedTask<bool>.FromResult(true, asyncState);
        }

        private async Task<bool> UnsubscribeCoreAsync(RedisSubscriber subscriber, RedisChannel channel, CommandFlags flags, object? asyncState, bool internalCall)
        {
            if (!await _operationLock.WaitAsync(subscriber.multiplexer.AsyncTimeoutMilliseconds).ForAwait()) throw WaitingTimeout(subscriber);
            try
            {
                var server = _currentServer;
                if (server is null) return true;
                if (ForwardCleanup(subscriber, channel, server)) return true;
                var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall, trackReply: true);
                return await subscriber.multiplexer.ExecuteAsyncImpl(message, Processor, asyncState, server).ForAwait();
            }
            finally { EndOperation(null); }
        }

        private bool ForwardCleanup(RedisSubscriber subscriber, RedisChannel channel, ServerEndPoint server)
        {
            if (subscriber.multiplexer.TryGetSubscription(channel, out var active) && !ReferenceEquals(active, this))
            {
                if (active is SingleNodeSubscription single) single.QueueCleanup(new CleanupRequest(subscriber, channel, server));
                else if (!active.IsConnectedTo(server.EndPoint)) QueueCleanup(new CleanupRequest(subscriber, channel, server));
                return true;
            }
            return false;
        }

        private sealed class RestoreOperation
        {
            public RestoreOperation(SingleNodeSubscription owner, RedisSubscriber subscriber, RedisChannel channel, Message message, ServerEndPoint? server)
            {
                Subscriber = subscriber;
                Channel = channel;
                Message = message;
                Server = server;
                Processor = new ResultProcessor.TrackSubscriptionsProcessor(owner, connection => owner.OnSubscribed(this, connection));
            }
            public RedisSubscriber Subscriber { get; }
            public RedisChannel Channel { get; }
            public Message Message { get; }
            public ServerEndPoint? Server { get; }
            public ResultProcessor.TrackSubscriptionsProcessor Processor { get; }
        }

        private sealed class CleanupRequest(RedisSubscriber subscriber, RedisChannel channel, ServerEndPoint server)
        {
            public RedisSubscriber Subscriber { get; } = subscriber;
            public RedisChannel Channel { get; } = channel;
            public ServerEndPoint Server { get; } = server;
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
                    var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, flags | CommandFlags.FireAndForget, internalCall);
                    subscriber.multiplexer.ExecuteSyncImpl(message, Processor, current);
                    _currentServer = null; // pre-emptively disconnect - F+F
                }
            }
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
                if (server.Value is { IsSubscriberConnected: true }) return true;
            }

            return false;
        }

        internal override int GetConnectionCount()
        {
            int count = 0;
            foreach (var server in _servers)
            {
                if (server.Value is { IsSubscriberConnected: true }) count++;
            }

            return count;
        }

        internal override bool IsConnectedTo(EndPoint endpoint)
            => _servers.TryGetValue(endpoint, out var server)
               && server.IsSubscriberConnected;

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
                if (last is { IsSubscriberConnected: true })
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
                if (server.Value.IsSubscriberConnected)
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
                    // make it so
                    var message = GetSubscriptionMessage(channel, change.GetValueOrDefault(), flags, internalCall);
                    subscriber.ExecuteSync(message, Processor, server);
                    delta++;
                }
            }

            return delta;
        }

        private SubscriptionAction? GetSubscriptionChange(ServerEndPoint server, CommandFlags flags)
        {
            // exclude sentinel, and only use replicas if we're explicitly asking for them
            bool useReplica = (Flags & CommandFlags.DemandReplica) != 0;
            bool shouldBeConnected = (server.ServerType != ServerType.Sentinel) & (server.IsReplica == useReplica);
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
                        // make it so
                        var message = GetSubscriptionMessage(channel, change.GetValueOrDefault(), flags, internalCall);
                        await subscriber.ExecuteAsync(message, Processor, loopServer).ForAwait();
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
                var message = GetSubscriptionMessage(channel, SubscriptionAction.Unsubscribe, flags, internalCall);
                any |= await subscriber.ExecuteAsync(message, Processor, server.Value).ForAwait();
            }

            return any;
        }
    }
}
