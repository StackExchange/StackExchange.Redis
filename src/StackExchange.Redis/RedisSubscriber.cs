using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        private readonly Dictionary<RedisChannel, Subscription> subscriptions = new Dictionary<RedisChannel, Subscription>();

        internal static void CompleteAsWorker(ICompletable completable)
        {
            if (completable != null) ThreadPool.QueueUserWorkItem(s_CompleteAsWorker, completable);
        }

        private static readonly WaitCallback s_CompleteAsWorker = s => ((ICompletable)s).TryComplete(true);

        internal static bool TryCompleteHandler<T>(EventHandler<T> handler, object sender, T args, bool isAsync) where T : EventArgs, ICompletable
        {
            if (handler == null) return true;
            if (isAsync)
            {
                if (handler.IsSingle())
                {
                    try { handler(sender, args); } catch { }
                }
                else
                {
                    foreach (EventHandler<T> sub in handler.AsEnumerable())
                    {
                        try { sub(sender, args); } catch { }
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        internal bool GetSubscriberCounts(in RedisChannel channel, out int handlers, out int queues)
        {
            Subscription sub;
            lock (subscriptions)
            {
                if (!subscriptions.TryGetValue(channel, out sub)) sub = null;
            }
            if (sub != null)
            {
                sub.GetSubscriberCounts(out handlers, out queues);
                return true;
            }
            handlers = queues = 0;
            return false;
        }

        internal Task AddSubscription(in RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags, object asyncState)
        {
            Task task = null;
            if (handler != null | queue != null)
            {
                lock (subscriptions)
                {
                    if (!subscriptions.TryGetValue(channel, out Subscription sub))
                    {
                        sub = new Subscription();
                        subscriptions.Add(channel, sub);
                        task = sub.SubscribeToServer(this, channel, flags, asyncState, false);
                    }
                    sub.Add(handler, queue);
                }
            }
            return task ?? CompletedTask<bool>.Default(asyncState);
        }

        internal ServerEndPoint GetSubscribedServer(in RedisChannel channel)
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

        internal void OnMessage(in RedisChannel subscription, in RedisChannel channel, in RedisValue payload)
        {
            ICompletable completable = null;
            ChannelMessageQueue queues = null;
            Subscription sub;
            lock (subscriptions)
            {
                if (subscriptions.TryGetValue(subscription, out sub))
                {
                    completable = sub.ForInvoke(channel, payload, out queues);
                }
            }
            if (queues != null) ChannelMessageQueue.WriteAll(ref queues, channel, payload);
            if (completable != null && !completable.TryComplete(false)) ConnectionMultiplexer.CompleteAsWorker(completable);
        }

        internal Task RemoveAllSubscriptions(CommandFlags flags, object asyncState)
        {
            Task last = null;
            lock (subscriptions)
            {
                foreach (var pair in subscriptions)
                {
                    pair.Value.MarkCompleted();
                    var task = pair.Value.UnsubscribeFromServer(pair.Key, flags, asyncState, false);
                    if (task != null) last = task;
                }
                subscriptions.Clear();
            }
            return last ?? CompletedTask<bool>.Default(asyncState);
        }

        internal Task RemoveSubscription(in RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags, object asyncState)
        {
            Task task = null;
            lock (subscriptions)
            {
                if (subscriptions.TryGetValue(channel, out Subscription sub))
                {
                    bool remove;
                    if (handler == null & queue == null) // blanket wipe
                    {
                        sub.MarkCompleted();
                        remove = true;
                    }
                    else
                    {
                        remove = sub.Remove(handler, queue);
                    }
                    if (remove)
                    {
                        subscriptions.Remove(channel);
                        task = sub.UnsubscribeFromServer(channel, flags, asyncState, false);
                    }
                }
            }
            return task ?? CompletedTask<bool>.Default(asyncState);
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

        internal bool SubscriberConnected(in RedisChannel channel = default(RedisChannel))
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

        internal sealed class Subscription
        {
            private Action<RedisChannel, RedisValue> _handlers;
            private ChannelMessageQueue _queues;
            private ServerEndPoint owner;

            public void Add(Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue)
            {
                if (handler != null) _handlers += handler;
                if (queue != null) ChannelMessageQueue.Combine(ref _queues, queue);
            }

            public ICompletable ForInvoke(in RedisChannel channel, in RedisValue message, out ChannelMessageQueue queues)
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

            public bool Remove(Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue)
            {
                if (handler != null) _handlers -= handler;
                if (queue != null) ChannelMessageQueue.Remove(ref _queues, queue);
                return _handlers == null & _queues == null;
            }

            public Task SubscribeToServer(ConnectionMultiplexer multiplexer, in RedisChannel channel, CommandFlags flags, object asyncState, bool internalCall)
            {
                var selected = multiplexer.SelectServer(RedisCommand.SUBSCRIBE, flags, default(RedisKey));
                var bridge = selected?.GetBridge(ConnectionType.Subscription, true);
                if (bridge == null) return null;

                // note: check we can create the message validly *before* we swap the owner over (Interlocked)
                var state = PendingSubscriptionState.Create(channel, this, flags, true, internalCall, asyncState, selected.IsReplica);

                if (Interlocked.CompareExchange(ref owner, selected, null) != null) return null;
                try
                {
                    if (!bridge.TryEnqueueBackgroundSubscriptionWrite(state))
                    {
                        state.Abort();
                        return null;
                    }
                    return state.Task;
                }
                catch
                {
                    // clear the owner if it is still us
                    Interlocked.CompareExchange(ref owner, null, selected);
                    throw;
                }
            }

            public Task UnsubscribeFromServer(in RedisChannel channel, CommandFlags flags, object asyncState, bool internalCall)
            {
                var oldOwner = Interlocked.Exchange(ref owner, null);
                var bridge = oldOwner?.GetBridge(ConnectionType.Subscription, false);
                if (bridge == null) return null;

                var state = PendingSubscriptionState.Create(channel, this, flags, false, internalCall, asyncState, oldOwner.IsReplica);

                if (!bridge.TryEnqueueBackgroundSubscriptionWrite(state))
                {
                    state.Abort();
                    return null;
                }
                return state.Task;
            }

            internal readonly struct PendingSubscriptionState
            {
                public override string ToString() => Message.ToString();
                public Subscription Subscription { get; }
                public Message Message { get; }
                public bool IsReplica { get; }
                public Task Task => _taskSource.Task;
                private readonly TaskCompletionSource<bool> _taskSource;

                public static PendingSubscriptionState Create(RedisChannel channel, Subscription subscription, CommandFlags flags, bool subscribe, bool internalCall, object asyncState, bool isReplica)
                    => new PendingSubscriptionState(asyncState, channel, subscription, flags, subscribe, internalCall, isReplica);

                public void Abort() => _taskSource.TrySetCanceled();
                public void Fail(Exception ex) => _taskSource.TrySetException(ex);

                private PendingSubscriptionState(object asyncState, RedisChannel channel, Subscription subscription, CommandFlags flags, bool subscribe, bool internalCall, bool isReplica)
                {
                    var cmd = subscribe
                        ? (channel.IsPatternBased ? RedisCommand.PSUBSCRIBE : RedisCommand.SUBSCRIBE)
                        : (channel.IsPatternBased ? RedisCommand.PUNSUBSCRIBE : RedisCommand.UNSUBSCRIBE);
                    var msg = Message.Create(-1, flags, cmd, channel);
                    if (internalCall) msg.SetInternalCall();

                    var source = TaskResultBox<bool>.Create(out _taskSource, asyncState);
                    msg.SetSource(ResultProcessor.TrackSubscriptions, source);

                    Subscription = subscription;
                    Message = msg;
                    IsReplica = isReplica;
                }
            }

            internal ServerEndPoint GetOwner() => Volatile.Read(ref owner);

            internal void Resubscribe(in RedisChannel channel, ServerEndPoint server)
            {
                if (server != null && Interlocked.CompareExchange(ref owner, server, server) == server)
                {
                    var cmd = channel.IsPatternBased ? RedisCommand.PSUBSCRIBE : RedisCommand.SUBSCRIBE;
                    var msg = Message.Create(-1, CommandFlags.FireAndForget, cmd, channel);
                    msg.SetInternalCall();
#pragma warning disable CS0618
                    server.WriteDirectFireAndForgetSync(msg, ResultProcessor.TrackSubscriptions);
#pragma warning restore CS0618
                }
            }

            internal bool Validate(ConnectionMultiplexer multiplexer, in RedisChannel channel)
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

        void ISubscriber.Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => Subscribe(channel, handler, null, flags);

        public void Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags)
        {
            var task = SubscribeAsync(channel, handler, queue, flags);
            if ((flags & CommandFlags.FireAndForget) == 0) Wait(task);
        }

        public ChannelMessageQueue Subscribe(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var queue = new ChannelMessageQueue(channel, this);
            Subscribe(channel, null, queue, flags);
            return queue;
        }

        Task ISubscriber.SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => SubscribeAsync(channel, handler, null, flags);

        public Task SubscribeAsync(in RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags)
        {
            if (channel.IsNullOrEmpty) throw new ArgumentNullException(nameof(channel));
            return multiplexer.AddSubscription(channel, handler, queue, flags, asyncState);
        }

        internal bool GetSubscriberCounts(in RedisChannel channel, out int handlers, out int queues)
            => multiplexer.GetSubscriberCounts(channel, out handlers, out queues);

        public async Task<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var queue = new ChannelMessageQueue(channel, this);
            await SubscribeAsync(channel, null, queue, flags).ForAwait();
            return queue;
        }

        public EndPoint SubscribedEndpoint(RedisChannel channel)
        {
            var server = multiplexer.GetSubscribedServer(channel);
            return server?.EndPoint;
        }

        void ISubscriber.Unsubscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => Unsubscribe(channel, handler, null, flags);
        public void Unsubscribe(in RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags)
        {
            var task = UnsubscribeAsync(channel, handler, queue, flags);
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

        Task ISubscriber.UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => UnsubscribeAsync(channel, handler, null, flags);
        public Task UnsubscribeAsync(in RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags)
        {
            if (channel.IsNullOrEmpty) throw new ArgumentNullException(nameof(channel));
            return multiplexer.RemoveSubscription(channel, handler, queue, flags, asyncState);
        }
    }
}
