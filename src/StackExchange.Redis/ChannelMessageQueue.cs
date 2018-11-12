using System;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a message that is broadcast via pub/sub
    /// </summary>
    public readonly struct ChannelMessage
    {
        private readonly ChannelMessageQueue _queue; // this is *smaller* than storing a RedisChannel for the subsribed channel
        /// <summary>
        /// See Object.ToString
        /// </summary>
        public override string ToString() => Channel + ":" + Message;

        /// <summary>
        /// See Object.GetHashCode
        /// </summary>
        public override int GetHashCode() => Channel.GetHashCode() ^ Message.GetHashCode();

        /// <summary>
        /// See Object.Equals
        /// </summary>
        /// <param name="obj">The <see cref="object"/> to compare.</param>
        public override bool Equals(object obj) => obj is ChannelMessage cm
            && cm.Channel == Channel && cm.Message == Message;

        /// <summary>
        /// Create a new <see cref="ChannelMessage"/> representing a message written to a <see cref="ChannelMessageQueue"/>
        /// </summary>
        /// <param name="queue">The <see cref="ChannelMessageQueue"/> associated with this message.</param>
        /// <param name="channel">A <see cref="RedisChannel"/> identifying the channel from which the message was received.</param>
        /// <param name="value">A <see cref="RedisValue"/> representing the value of the message.</param>
        public ChannelMessage(ChannelMessageQueue queue, RedisChannel channel, RedisValue value)
        {
            _queue = queue;
            Channel = channel;
            Message = value;
        }

        /// <summary>
        /// The channel that the subscription was created from
        /// </summary>
        public RedisChannel SubscriptionChannel => _queue.Channel;

        /// <summary>
        /// The channel that the message was broadcast to
        /// </summary>
        public RedisChannel Channel { get; }
        /// <summary>
        /// The value that was broadcast
        /// </summary>
        public RedisValue Message { get; }
    }

    /// <summary>
    /// Represents a message queue of ordered pub/sub notifications
    /// </summary>
    /// <remarks>To create a ChannelMessageQueue, use ISubscriber.Subscribe[Async](RedisKey)</remarks>
    public sealed class ChannelMessageQueue
    {
        private readonly ChannelReader<ChannelMessage> _queue;
        private readonly Action<CommandFlags> _onUnsubscribe;
        private readonly Func<CommandFlags, Task> _onUnsubscribeAsync;
        private readonly Action<Exception> _onInternalError;

        /// <summary>
        /// The Channel that was subscribed for this queue
        /// </summary>
        public RedisChannel Channel { get; }

        /// <summary>
        /// See Object.ToString
        /// </summary>
        public override string ToString() => Channel;

        /// <summary>
        /// An awaitable task the indicates completion of the queue (including drain of data)
        /// </summary>
        public Task Completion => _queue.Completion;

        /// <summary>
        /// Constructs a <see cref="ChannelMessageQueue" /> from a <see cref="System.Threading.Channels.ChannelReader{RedisValue}"/> representing
        /// incoming Redis values on the channel.
        /// </summary>
        /// <param name="channel">The name of the channel this subscription is listening on.</param>
        /// <param name="incomingValues">A channel reader representing the incoming values.</param>
        /// <param name="onUnsubscribe">A delegate to call when <see cref="Unsubscribe(CommandFlags)"/> is called.</param>
        /// <param name="onUnsubscribeAsync">A delegate to call when <see cref="UnsubscribeAsync(CommandFlags)"/> is called.</param>
        /// <param name="onInternalError">REVIEW: Need more context here</param>
        public ChannelMessageQueue(RedisChannel channel, ChannelReader<ChannelMessage> incomingValues, Action<CommandFlags> onUnsubscribe, Func<CommandFlags, Task> onUnsubscribeAsync, Action<Exception> onInternalError)
        {
            Channel = channel;
            _queue = incomingValues;

            // REVIEW: This part is kind of hacky...
            _onUnsubscribe = onUnsubscribe;
            _onUnsubscribeAsync = onUnsubscribeAsync;
            _onInternalError = onInternalError;
        }

        /// <summary>
        /// Consume a message from the channel.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use.</param>
        public ValueTask<ChannelMessage> ReadAsync(CancellationToken cancellationToken = default)
            => _queue.ReadAsync(cancellationToken);

        /// <summary>
        /// Attempt to synchronously consume a message from the channel.
        /// </summary>
        /// <param name="item">The <see cref="ChannelMessage"/> read from the Channel.</param>
        public bool TryRead(out ChannelMessage item)
            => _queue.TryRead(out item);

        /// <summary>
        /// Attempt to query the backlog length of the queue.
        /// </summary>
        /// <param name="count">The (approximate) count of items in the Channel.</param>
        public bool TryGetCount(out int count)
        {
            // get this using the reflection
            try
            {
                var parentField = _queue.GetType().GetField("_parent", BindingFlags.Instance | BindingFlags.NonPublic);
                if (parentField != null)
                {
                    var parent = parentField.GetValue(_queue);
                    var prop = parent.GetType().GetProperty("ItemsCountForDebugger", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (prop != null)
                    {
                        count = (int)prop.GetValue(parent);
                        return true;
                    }
                }
            }
            catch { }
            count = default;
            return false;
        }

        private Delegate _onMessageHandler;
        private void AssertOnMessage(Delegate handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (Interlocked.CompareExchange(ref _onMessageHandler, handler, null) != null)
            {
                throw new InvalidOperationException("Only a single " + nameof(OnMessage) + " is allowed");
            }
        }

        /// <summary>
        /// Create a message loop that processes messages sequentially.
        /// </summary>
        /// <param name="handler">The handler to run when receiving a message.</param>
        public void OnMessage(Action<ChannelMessage> handler)
        {
            AssertOnMessage(handler);

            ThreadPool.QueueUserWorkItem(
                state => ((ChannelMessageQueue)state).OnMessageSyncImpl().RedisFireAndForget(), this);
        }

        private async Task OnMessageSyncImpl()
        {
            var handler = (Action<ChannelMessage>)_onMessageHandler;
            while (!Completion.IsCompleted)
            {
                ChannelMessage next;

                // Keep trying to read values
                while (!TryRead(out next))
                {
                    // If we fail, wait for an item to appear
                    if (!await _queue.WaitToReadAsync())
                    {
                        // Channel is closed
                        break;
                    }

                    // There should be an item available now, but another reader might grab it,
                    // so we keep TryReading in the loop.
                }

                try { handler(next); }
                catch { } // matches MessageCompletable
            }

            if (Completion.IsFaulted)
            {
                _onInternalError(Completion.Exception.InnerException);
            }
        }

        /// <summary>
        /// Create a message loop that processes messages sequentially.
        /// </summary>
        /// <param name="handler">The handler to execute when receiving a message.</param>
        public void OnMessage(Func<ChannelMessage, Task> handler)
        {
            AssertOnMessage(handler);

            ThreadPool.QueueUserWorkItem(
                state => ((ChannelMessageQueue)state).OnMessageAsyncImpl().RedisFireAndForget(), this);
        }

        private async Task OnMessageAsyncImpl()
        {
            var handler = (Func<ChannelMessage, Task>)_onMessageHandler;
            while (!Completion.IsCompleted)
            {
                ChannelMessage next;

                // Keep trying to read values
                while (!TryRead(out next))
                {
                    // If we fail, wait for an item to appear
                    if (!await _queue.WaitToReadAsync())
                    {
                        // Channel is closed
                        break;
                    }

                    // There should be an item available now, but another reader might grab it,
                    // so we keep TryReading in the loop.
                }

                try
                {
                    var task = handler(next);
                    if (task != null && task.Status != TaskStatus.RanToCompletion)
                    {
                        await task.ConfigureAwait(false);
                    }
                }
                catch { } // matches MessageCompletable
            }

            if (Completion.IsFaulted)
            {
                _onInternalError(Completion.Exception.InnerException);
            }
        }

        internal static bool IsOneOf(Action<RedisChannel, RedisValue> handler)
        {
            // REVIEW: Need more context here to properly replace this.
            throw new NotImplementedException();
            //try
            //{
            //    return handler?.Target is ChannelMessageQueue
            //        && handler.Method.Name == nameof(HandleMessage);
            //}
            //catch
            //{
            //    return false;
            //}
        }

        /// <summary>
        /// Stop receiving messages on this channel.
        /// </summary>
        /// <param name="flags">The flags to use when unsubscribing.</param>
        public void Unsubscribe(CommandFlags flags = CommandFlags.None) => _onUnsubscribe(flags);

        /// <summary>
        /// Stop receiving messages on this channel.
        /// </summary>
        /// <param name="flags">The flags to use when unsubscribing.</param>
        public Task UnsubscribeAsync(CommandFlags flags = CommandFlags.None) => _onUnsubscribeAsync(flags);
    }
}
