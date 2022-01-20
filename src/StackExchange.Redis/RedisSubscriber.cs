using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        private readonly SemaphoreSlim subscriptionsAddLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<RedisChannel, Subscription> subscriptions = new();

        internal int GetSubscriptionsCount() => subscriptions.Count;

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

        internal async Task<bool> AddSubscriptionAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags, object asyncState)
        {
            if (handler != null | queue != null)
            {
                if (!subscriptions.TryGetValue(channel, out Subscription sub))
                {
                    sub = new Subscription(flags);
                    subscriptions.TryAdd(channel, sub);
                    if (!(await sub.SubscribeToServerAsync(this, channel, flags, asyncState, false)))
                    {
                        return false;
                    }
                }
                sub.Add(handler, queue);
            }
            return true;
        }

        internal ServerEndPoint GetSubscribedServer(in RedisChannel channel)
        {
            if (!channel.IsNullOrEmpty && subscriptions.TryGetValue(channel, out Subscription sub))
            {
                return sub.GetCurrentServer();
            }
            return null;
        }

        internal void OnMessage(in RedisChannel subscription, in RedisChannel channel, in RedisValue payload)
        {
            ICompletable completable = null;
            ChannelMessageQueue queues = null;
            if (subscriptions.TryGetValue(subscription, out Subscription sub))
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

        internal Task RemoveAllSubscriptionsAsync(CommandFlags flags, object asyncState)
        {
            Task last = null;
            foreach (var pair in subscriptions)
            {
                if (subscriptions.TryRemove(pair.Key, out var sub))
                {
                    pair.Value.MarkCompleted();
                    last = pair.Value.UnsubscribeFromServerAsync(pair.Key, asyncState, false);
                }
            }
            return last ?? CompletedTask<bool>.Default(asyncState);
        }

        internal Task RemoveSubscriptionAsync(in RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags, object asyncState)
        {
            Task task = null;
            if (subscriptions.TryGetValue(channel, out Subscription sub))
            {
                bool removeChannel;
                if (handler == null & queue == null) // blanket wipe
                {
                    sub.MarkCompleted();
                    removeChannel = true;
                }
                else
                {
                    removeChannel = sub.Remove(handler, queue);
                }
                // If it was the last handler or a blanket wipe, remove it.
                if (removeChannel)
                {
                    subscriptions.TryRemove(channel, out _);
                    task = sub.UnsubscribeFromServerAsync(channel, asyncState, false);
                }
            }
            return task ?? CompletedTask<bool>.Default(asyncState);
        }

        internal void ResendSubscriptions(ServerEndPoint server)
        {
            if (server == null) return;
            foreach (var pair in subscriptions)
            {
                pair.Value.Resubscribe(pair.Key, server);
            }
        }

        internal bool SubscriberConnected(in RedisChannel channel = default(RedisChannel))
        {
            var server = GetSubscribedServer(channel) ?? SelectServer(RedisCommand.SUBSCRIBE, CommandFlags.DemandMaster, channel);
            return server?.IsConnected == true && server.IsSubscriberConnected;
        }

        internal async Task<long> EnsureSubscriptionsAsync()
        {
            long count = 0;
            foreach (var pair in subscriptions)
            {
                if (await pair.Value.EnsureSubscribedAsync(this, pair.Key))
                {
                    count++;
                }
            }
            return count;
        }

        internal sealed class Subscription
        {
            private Action<RedisChannel, RedisValue> _handlers;
            private ChannelMessageQueue _queues;
            private ServerEndPoint CurrentServer;
            public CommandFlags Flags { get; }

            public Subscription(CommandFlags flags) => Flags = flags;

            private Message GetMessage(
                RedisChannel channel,
                RedisCommand command,
                object asyncState,
                bool internalCall,
                out TaskCompletionSource<bool> taskSource)
            {
                var msg = Message.Create(-1, Flags, command, channel);
                msg.SetForSubscriptionBridge();
                if (internalCall)
                {
                    msg.SetInternalCall();
                }

                var source = TaskResultBox<bool>.Create(out taskSource, asyncState);
                msg.SetSource(ResultProcessor.TrackSubscriptions, source);
                return msg;
            }

            public void Add(Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue)
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

            public async Task<bool> SubscribeToServerAsync(ConnectionMultiplexer multiplexer, RedisChannel channel, CommandFlags flags, object asyncState, bool internalCall)
            {
                var command = channel.IsPatternBased ? RedisCommand.PSUBSCRIBE : RedisCommand.SUBSCRIBE;
                var selected = multiplexer.SelectServer(command, flags, channel);

                // Do we have a server already? And is it connected? Then bail out.
                if (CurrentServer?.IsSubscriberConnected == true)
                {
                    return false;
                }
                // Otherwise try and subscribe on the server side
                try
                {
                    var message = GetMessage(channel, command, asyncState, internalCall, out var taskSource);
                    // TODO: Could move this entirely into a processor, e.g. the CurrentServer removal we need below
                    var success = await multiplexer.ExecuteAsyncImpl(message, ResultProcessor.TrackSubscriptions, asyncState, selected);
                    if (!success)
                    {
                        taskSource.SetResult(false);
                    }
                    return await taskSource.Task;
                }
                catch
                {
                    // If there was an exception, clear the owner
                    Interlocked.CompareExchange(ref CurrentServer, null, selected);
                    throw;
                }
            }

            public async Task<bool> UnsubscribeFromServerAsync(RedisChannel channel, object asyncState, bool internalCall)
            {
                var command = channel.IsPatternBased ? RedisCommand.PUNSUBSCRIBE : RedisCommand.UNSUBSCRIBE;
                var oldOwner = Interlocked.Exchange(ref CurrentServer, null);
                if (oldOwner != null)
                {
                    var message = GetMessage(channel, command, asyncState, internalCall, out var taskSource);
                    var success = await oldOwner.Multiplexer.ExecuteAsyncImpl(message, ResultProcessor.TrackSubscriptions, asyncState, oldOwner);
                    if (!success)
                    {
                        taskSource.SetCanceled();
                    }
                    return await taskSource.Task;
                }
                return false;
            }

            internal ServerEndPoint GetCurrentServer() => Volatile.Read(ref CurrentServer);

            internal void Resubscribe(in RedisChannel channel, ServerEndPoint server)
            {
                // Only re-subscribe to the original server
                if (server != null && GetCurrentServer() == server)
                {
                    var cmd = channel.IsPatternBased ? RedisCommand.PSUBSCRIBE : RedisCommand.SUBSCRIBE;
                    var msg = Message.Create(-1, CommandFlags.FireAndForget, cmd, channel);
                    msg.SetInternalCall();
                    server.Multiplexer.ExecuteSyncImpl(msg, ResultProcessor.TrackSubscriptions, server);
                }
            }

            internal async ValueTask<bool> EnsureSubscribedAsync(ConnectionMultiplexer multiplexer, RedisChannel channel)
            {
                bool changed = false;
                var oldOwner = Volatile.Read(ref CurrentServer);
                // If the old server is bad, unsubscribe
                if (oldOwner != null && !oldOwner.IsSelectable(RedisCommand.PSUBSCRIBE))
                {
                    changed = await UnsubscribeFromServerAsync(channel, null, true);
                    oldOwner = null;
                }
                // If we didn't have an owner or just cleared one, subscribe
                if (oldOwner == null)
                {
                    changed = await SubscribeToServerAsync(multiplexer, channel, CommandFlags.FireAndForget, null, true);
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

        public bool IsConnected(RedisChannel channel = default(RedisChannel)) => multiplexer.SubscriberConnected(channel);

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

        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            if (channel.IsNullOrEmpty)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            var msg = Message.Create(-1, flags, RedisCommand.PUBLISH, channel, message);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            if (channel.IsNullOrEmpty)
            {
                throw new ArgumentNullException(nameof(channel));
            }
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
            if (channel.IsNullOrEmpty)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            return multiplexer.AddSubscriptionAsync(channel, handler, queue, flags, asyncState);
        }

        internal bool GetSubscriberCounts(in RedisChannel channel, out int handlers, out int queues)
            => multiplexer.GetSubscriberCounts(channel, out handlers, out queues);

        public async Task<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var queue = new ChannelMessageQueue(channel, this);
            await SubscribeAsync(channel, null, queue, flags).ForAwait();
            return queue;
        }

        public EndPoint SubscribedEndpoint(RedisChannel channel) => multiplexer.GetSubscribedServer(channel)?.EndPoint;

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

        public Task UnsubscribeAllAsync(CommandFlags flags = CommandFlags.None) => multiplexer.RemoveAllSubscriptionsAsync(flags, asyncState);

        Task ISubscriber.UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags)
            => UnsubscribeAsync(channel, handler, null, flags);

        public Task UnsubscribeAsync(in RedisChannel channel, Action<RedisChannel, RedisValue> handler, ChannelMessageQueue queue, CommandFlags flags)
        {
            if (channel.IsNullOrEmpty)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            return multiplexer.RemoveSubscriptionAsync(channel, handler, queue, flags, asyncState);
        }
    }
}
