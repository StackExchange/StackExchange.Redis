using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

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
            RedisChannel channel,
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
        internal override bool IsConnectedAny() => _currentServer is { IsSubscriberConnected: true };

        internal override int GetConnectionCount() => IsConnectedAny() ? 1 : 0;

        internal override bool IsConnectedTo(EndPoint endpoint)
        {
            var server = _currentServer;
            return server is { IsSubscriberConnected: true } && server.EndPoint == endpoint;
        }

        internal override void AddEndpoint(ServerEndPoint server) => _currentServer = server;

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
            if (server is { IsSubscriberConnected: false })
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
            if (IsConnectedAny()) return 0;

            // we're not appropriately connected, so blank it out for eligible reconnection
            _currentServer = null;
            var message = GetSubscriptionMessage(channel, SubscriptionAction.Subscribe, flags, internalCall);
            var selected = subscriber.multiplexer.SelectServer(message);
            _ = subscriber.ExecuteSync(message, Processor, selected);
            return 1;
        }

        internal override async Task<int> EnsureSubscribedToServerAsync(
            RedisSubscriber subscriber,
            RedisChannel channel,
            CommandFlags flags,
            bool internalCall,
            ServerEndPoint? server = null)
        {
            if (IsConnectedAny()) return 0;

            // we're not appropriately connected, so blank it out for eligible reconnection
            _currentServer = null;
            var message = GetSubscriptionMessage(channel, SubscriptionAction.Subscribe, flags, internalCall);
            server ??= subscriber.multiplexer.SelectServer(message);
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
