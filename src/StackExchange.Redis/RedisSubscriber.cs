using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        private readonly Dictionary<RedisChannel, Subscription> subscriptions = new Dictionary<RedisChannel, Subscription>();

        internal static bool TryCompleteHandler<T>(EventHandler<T> handler, object sender, T args, bool isAsync) where T : EventArgs
        {
            if (handler == null) return true;
            if (isAsync)
            {
                foreach (EventHandler<T> sub in handler.GetInvocationList())
                {
                    try
                    { sub.Invoke(sender, args); }
                    catch
                    { }
                }
                return true;
            }
            return false;
        }

        internal Task AddSubscription(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags, object asyncState)
        {
            if (handler != null)
            {
                bool asAsync = !ChannelMessageQueue.IsOneOf(handler);
                lock (subscriptions)
                {
                    if (subscriptions.TryGetValue(channel, out Subscription sub))
                    {
                        sub.Add(asAsync, handler);
                    }
                    else
                    {
                        sub = new Subscription(asAsync, handler);
                        subscriptions.Add(channel, sub);
                        var task = sub.SubscribeToServer(this, channel, flags, asyncState, false);
                        if (task != null) return task;
                    }
                }
            }
            return CompletedTask<bool>.Default(asyncState);
        }

        internal ServerEndPoint GetSubscribedServer(RedisChannel channel)
        {
            if (!channel.IsNullOrEmpty)
            {
                lock (subscriptions)
                {
                    if (subscriptions.TryGetValue(channel, out Subscription sub))
                    {
                        return sub.GetOwner();
                    }
                }
            }
            return null;
        }

        internal void OnMessage(RedisChannel subscription, RedisChannel channel, RedisValue payload)
        {
            ICompletable completable = null;
            lock (subscriptions)
            {
                if (subscriptions.TryGetValue(subscription, out Subscription sub))
                {
                    completable = sub.ForInvoke(channel, payload);
                }
            }
            if (completable != null) UnprocessableCompletionManager.CompleteSyncOrAsync(completable);
        }

        internal Task RemoveAllSubscriptions(CommandFlags flags, object asyncState)
        {
            Task last = CompletedTask<bool>.Default(asyncState);
            lock (subscriptions)
            {
                foreach (var pair in subscriptions)
                {
                    var msg = pair.Value.ForSyncShutdown();
                    if (msg != null) UnprocessableCompletionManager.CompleteSyncOrAsync(msg);
                    pair.Value.Remove(true, null);
                    pair.Value.Remove(false, null);

                    var task = pair.Value.UnsubscribeFromServer(pair.Key, flags, asyncState, false);
                    if (task != null) last = task;
                }
                subscriptions.Clear();
            }
            return last;
        }

        internal Task RemoveSubscription(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags, object asyncState)
        {
            lock (subscriptions)
            {
                bool asAsync = !ChannelMessageQueue.IsOneOf(handler);
                if (subscriptions.TryGetValue(channel, out Subscription sub) && sub.Remove(asAsync, handler))
                {
                    subscriptions.Remove(channel);
                    var task = sub.UnsubscribeFromServer(channel, flags, asyncState, false);
                    if (task != null) return task;
                }
            }
            return CompletedTask<bool>.Default(asyncState);
        }

        internal void ResendSubscriptions(ServerEndPoint server)
        {
            if (server == null) return;
            lock (subscriptions)
            {
                foreach (var pair in subscriptions)
                {
                    pair.Value.Resubscribe(pair.Key, server);
                }
            }
        }

        internal bool SubscriberConnected(RedisChannel channel = default(RedisChannel))
        {
            var server = GetSubscribedServer(channel);
            if (server != null) return server.IsConnected;

            server = SelectServer(RedisCommand.SUBSCRIBE, CommandFlags.DemandMaster, default(RedisKey));
            return server?.IsConnected == true;
        }

        internal long ValidateSubscriptions()
        {
            lock (subscriptions)
            {
                long count = 0;
                foreach (var pair in subscriptions)
                {
                    if (pair.Value.Validate(this, pair.Key)) count++;
                }
                return count;
            }
        }

        private sealed class Subscription
        {
            private Action<RedisChannel, RedisValue> _asyncHandler, _syncHandler;
            private ServerEndPoint owner;

            public Subscription(bool asAsync, Action<RedisChannel, RedisValue> value)
            {
                if (asAsync) _asyncHandler = value;
                else _syncHandler = value;
            }

            public void Add(bool asAsync, Action<RedisChannel, RedisValue> value)
            {
                if (asAsync) _asyncHandler += value;
                else _syncHandler += value;
            }

            public ICompletable ForSyncShutdown()
            {
                var syncHandler = _syncHandler;
                return syncHandler == null ? null : new MessageCompletable(default, default, syncHandler, null);
            }
            public ICompletable ForInvoke(RedisChannel channel, RedisValue message)
            {
                var syncHandler = _syncHandler;
                var asyncHandler = _asyncHandler;
                return (syncHandler == null && asyncHandler == null) ? null : new MessageCompletable(channel, message, syncHandler, asyncHandler);
            }

            public bool Remove(bool asAsync, Action<RedisChannel, RedisValue> value)
            {
                if (value == null)
                { // treat as blanket wipe
                    if (asAsync) _asyncHandler = null;
                    else _syncHandler = null;
                }
                else
                {
                    if (asAsync) _asyncHandler -= value;
                    else _syncHandler -= value;
                }
                return _syncHandler == null && _asyncHandler == null;
            }

            public Task SubscribeToServer(ConnectionMultiplexer multiplexer, RedisChannel channel, CommandFlags flags, object asyncState, bool internalCall)
            {
                var cmd = channel.IsPatternBased ? RedisCommand.PSUBSCRIBE : RedisCommand.SUBSCRIBE;
                var selected = multiplexer.SelectServer(cmd, flags, default(RedisKey));

                if (selected == null || Interlocked.CompareExchange(ref owner, selected, null) != null) return null;

                var msg = Message.Create(-1, flags, cmd, channel);
                if (internalCall) msg.SetInternalCall();
                return selected.WriteDirectAsync(msg, ResultProcessor.TrackSubscriptions, asyncState);
            }

            public Task UnsubscribeFromServer(RedisChannel channel, CommandFlags flags, object asyncState, bool internalCall)
            {
                var oldOwner = Interlocked.Exchange(ref owner, null);
                if (oldOwner == null) return null;

                var cmd = channel.IsPatternBased ? RedisCommand.PUNSUBSCRIBE : RedisCommand.UNSUBSCRIBE;
                var msg = Message.Create(-1, flags, cmd, channel);
                if (internalCall) msg.SetInternalCall();
                return oldOwner.WriteDirectAsync(msg, ResultProcessor.TrackSubscriptions, asyncState);
            }

            internal ServerEndPoint GetOwner() => Volatile.Read(ref owner);

            internal void Resubscribe(RedisChannel channel, ServerEndPoint server)
            {
                if (server != null && Interlocked.CompareExchange(ref owner, server, server) == server)
                {
                    var cmd = channel.IsPatternBased ? RedisCommand.PSUBSCRIBE : RedisCommand.SUBSCRIBE;
                    var msg = Message.Create(-1, CommandFlags.FireAndForget, cmd, channel);
                    msg.SetInternalCall();
                    server.WriteDirectFireAndForget(msg, ResultProcessor.TrackSubscriptions);
                }
            }

            internal bool Validate(ConnectionMultiplexer multiplexer, RedisChannel channel)
            {
                bool changed = false;
                var oldOwner = Volatile.Read(ref owner);
                if (oldOwner != null && !oldOwner.IsSelectable(RedisCommand.PSUBSCRIBE))
                {
                    if (UnsubscribeFromServer(channel, CommandFlags.FireAndForget, null, true) != null)
                    {
                        changed = true;
                    }
                    oldOwner = null;
                }
                if (oldOwner == null && SubscribeToServer(multiplexer, channel, CommandFlags.FireAndForget, null, true) != null)
                {
                    changed = true;
                }
                return changed;
            }
        }

        internal string GetConnectionName(EndPoint endPoint, ConnectionType connectionType)
            => GetServerEndPoint(endPoint)?.GetBridge(connectionType, false)?.PhysicalName;

        internal event Action<string, Exception, string> MessageFaulted;
        internal event Action<bool> Closing;
        internal event Action<string> PreTransactionExec, TransactionLog, InfoMessage;
        internal event Action<EndPoint, ConnectionType> Connecting;
        internal event Action<EndPoint, ConnectionType> Resurrecting;

        [Conditional("VERBOSE")]
        internal void OnMessageFaulted(Message msg, Exception fault, [CallerMemberName] string origin = default, [CallerFilePath] string path = default, [CallerLineNumber] int lineNumber = default)
        {
            MessageFaulted?.Invoke(msg?.CommandAndKey, fault, $"{origin} ({path}#{lineNumber})");
        }
        [Conditional("VERBOSE")]
        internal void OnInfoMessage(string message)
        {
            InfoMessage?.Invoke(message);
        }
        [Conditional("VERBOSE")]
        internal void OnClosing(bool complete)
        {
            Closing?.Invoke(complete);
        }
        [Conditional("VERBOSE")]
        internal void OnConnecting(EndPoint endpoint, ConnectionType connectionType)
        {
            Connecting?.Invoke(endpoint, connectionType);
        }
        [Conditional("VERBOSE")]
        internal void OnResurrecting(EndPoint endpoint, ConnectionType connectionType)
        {
            Resurrecting.Invoke(endpoint, connectionType);
        }
        [Conditional("VERBOSE")]
        internal void OnPreTransactionExec(Message message)
        {
            PreTransactionExec?.Invoke(message.CommandAndKey);
        }
        [Conditional("VERBOSE")]
        internal void OnTransactionLog(string message)
        {
            TransactionLog?.Invoke(message);
        }
    }

    internal sealed class RedisSubscriber : RedisBase, ISubscriber
    {
        internal RedisSubscriber(ConnectionMultiplexer multiplexer, object asyncState) : base(multiplexer, asyncState)
        {
        }

        public EndPoint IdentifyEndpoint(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMSUB, channel);
            msg.SetInternalCall();
            return ExecuteSync(msg, ResultProcessor.ConnectionIdentity);
        }

        public Task<EndPoint> IdentifyEndpointAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMSUB, channel);
            msg.SetInternalCall();
            return ExecuteAsync(msg, ResultProcessor.ConnectionIdentity);
        }

        public bool IsConnected(RedisChannel channel = default(RedisChannel))
        {
            return multiplexer.SubscriberConnected(channel);
        }

        public override TimeSpan Ping(CommandFlags flags = CommandFlags.None)
        {
            var msg = CreatePingMessage(flags, out var server);
            return ExecuteSync(msg, ResultProcessor.ResponseTimer, server);
        }

        public override Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = CreatePingMessage(flags, out var server);
            return ExecuteAsync(msg, ResultProcessor.ResponseTimer, server);
        }

        private Message CreatePingMessage(CommandFlags flags, out ServerEndPoint server)
        {
            bool usePing = false;
            server = null;
            if (multiplexer.CommandMap.IsAvailable(RedisCommand.PING))
            {
                try { usePing = GetFeatures(default, flags, out server).PingOnSubscriber; }
                catch { }
            }

            if (usePing)
            {
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.PING);
            }
            else
            {
                // can't use regular PING, but we can unsubscribe from something random that we weren't even subscribed to...
                RedisValue channel = multiplexer.UniqueId;
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.UNSUBSCRIBE, channel);
            }
        }

        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            if (channel.IsNullOrEmpty) throw new ArgumentNullException(nameof(channel));
            var msg = Message.Create(-1, flags, RedisCommand.PUBLISH, channel, message);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            if (channel.IsNullOrEmpty) throw new ArgumentNullException(nameof(channel));
            var msg = Message.Create(-1, flags, RedisCommand.PUBLISH, channel, message);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public void Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None)
        {
            var task = SubscribeAsync(channel, handler, flags);
            if ((flags & CommandFlags.FireAndForget) == 0) Wait(task);
        }

        public ChannelMessageQueue Subscribe(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var c = new ChannelMessageQueue(channel, this);
            c.Subscribe(flags);
            return c;
        }

        public Task SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None)
        {
            if (channel.IsNullOrEmpty) throw new ArgumentNullException(nameof(channel));
            return multiplexer.AddSubscription(channel, handler, flags, asyncState);
        }

        public async Task<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var c = new ChannelMessageQueue(channel, this);
            await c.SubscribeAsync(flags).ForAwait();
            return c;
        }

        public EndPoint SubscribedEndpoint(RedisChannel channel)
        {
            var server = multiplexer.GetSubscribedServer(channel);
            return server?.EndPoint;
        }

        public void Unsubscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler = null, CommandFlags flags = CommandFlags.None)
        {
            var task = UnsubscribeAsync(channel, handler, flags);
            if ((flags & CommandFlags.FireAndForget) == 0) Wait(task);
        }

        public void UnsubscribeAll(CommandFlags flags = CommandFlags.None)
        {
            var task = UnsubscribeAllAsync(flags);
            if ((flags & CommandFlags.FireAndForget) == 0) Wait(task);
        }

        public Task UnsubscribeAllAsync(CommandFlags flags = CommandFlags.None)
        {
            return multiplexer.RemoveAllSubscriptions(flags, asyncState);
        }

        public Task UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler = null, CommandFlags flags = CommandFlags.None)
        {
            if (channel.IsNullOrEmpty) throw new ArgumentNullException(nameof(channel));
            return multiplexer.RemoveSubscription(channel, handler, flags, asyncState);
        }
    }
}
