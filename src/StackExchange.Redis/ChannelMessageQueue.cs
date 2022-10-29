using System;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a message that is broadcast via publish/subscribe.
    /// </summary>
    public readonly struct ChannelMessage
    {
        // this is *smaller* than storing a RedisChannel for the subscribed channel
        private readonly ChannelMessageQueue _queue;

        /// <summary>
        /// The Channel:Message string representation.
        /// </summary>
        public override string ToString() => ((string?)Channel) + ":" + ((string?)Message);

        /// <inheritdoc/>
        public override int GetHashCode() => Channel.GetHashCode() ^ Message.GetHashCode();

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ChannelMessage cm
            && cm.Channel == Channel && cm.Message == Message;

        internal ChannelMessage(ChannelMessageQueue queue, in RedisChannel channel, in RedisValue value)
        {
            _queue = queue;
            Channel = channel;
            Message = value;
        }

        /// <summary>
        /// The channel that the subscription was created from.
        /// </summary>
        public RedisChannel SubscriptionChannel => _queue.Channel;

        /// <summary>
        /// The channel that the message was broadcast to.
        /// </summary>
        public RedisChannel Channel { get; }

        /// <summary>
        /// The value that was broadcast.
        /// </summary>
        public RedisValue Message { get; }

        /// <summary>
        /// Checks if 2 messages are .Equal()
        /// </summary>
        public static bool operator ==(ChannelMessage left, ChannelMessage right) => left.Equals(right);

        /// <summary>
        /// Checks if 2 messages are not .Equal()
        /// </summary>
        public static bool operator !=(ChannelMessage left, ChannelMessage right) => !left.Equals(right);
    }

    /// <summary>
    /// Represents a message queue of ordered pub/sub notifications.
    /// </summary>
    /// <remarks>
    /// To create a ChannelMessageQueue, use <see cref="ISubscriber.Subscribe(RedisChannel, CommandFlags)"/>
    /// or <see cref="ISubscriber.SubscribeAsync(RedisChannel, CommandFlags)"/>.
    /// </remarks>
    public sealed class ChannelMessageQueue
    {
        private readonly Channel<ChannelMessage> _queue;
        /// <summary>
        /// The Channel that was subscribed for this queue.
        /// </summary>
        public RedisChannel Channel { get; }
        private RedisSubscriber? _parent;

        /// <summary>
        /// The string representation of this channel.
        /// </summary>
        public override string? ToString() => (string?)Channel;

        /// <summary>
        /// An awaitable task the indicates completion of the queue (including drain of data).
        /// </summary>
        public Task Completion => _queue.Reader.Completion;

        internal ChannelMessageQueue(in RedisChannel redisChannel, RedisSubscriber parent)
        {
            Channel = redisChannel;
            _parent = parent;
            _queue = System.Threading.Channels.Channel.CreateUnbounded<ChannelMessage>(s_ChannelOptions);
        }

        private static readonly UnboundedChannelOptions s_ChannelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        };

        private void Write(in RedisChannel channel, in RedisValue value)
        {
            var writer = _queue.Writer;
            writer.TryWrite(new ChannelMessage(this, channel, value));
        }

        /// <summary>
        /// Consume a message from the channel.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use.</param>
        public ValueTask<ChannelMessage> ReadAsync(CancellationToken cancellationToken = default)
            => _queue.Reader.ReadAsync(cancellationToken);

        /// <summary>
        /// Attempt to synchronously consume a message from the channel.
        /// </summary>
        /// <param name="item">The <see cref="ChannelMessage"/> read from the Channel.</param>
        public bool TryRead(out ChannelMessage item) => _queue.Reader.TryRead(out item);

        /// <summary>
        /// Attempt to query the backlog length of the queue.
        /// </summary>
        /// <param name="count">The (approximate) count of items in the Channel.</param>
        public bool TryGetCount(out int count)
        {
            // get this using the reflection
            try
            {
                var prop = _queue.GetType().GetProperty("ItemsCountForDebugger", BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop is not null)
                {
                    count = (int)prop.GetValue(_queue)!;
                    return true;
                }
            }
            catch { }
            count = default;
            return false;
        }

        private Delegate? _onMessageHandler;
        private void AssertOnMessage(Delegate handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (Interlocked.CompareExchange(ref _onMessageHandler, handler, null) != null)
                throw new InvalidOperationException("Only a single " + nameof(OnMessage) + " is allowed");
        }

        /// <summary>
        /// Create a message loop that processes messages sequentially.
        /// </summary>
        /// <param name="handler">The handler to run when receiving a message.</param>
        public void OnMessage(Action<ChannelMessage> handler)
        {
            AssertOnMessage(handler);

            ThreadPool.QueueUserWorkItem(
                state => ((ChannelMessageQueue)state!).OnMessageSyncImpl().RedisFireAndForget(), this);
        }

        private async Task OnMessageSyncImpl()
        {
            var handler = (Action<ChannelMessage>?)_onMessageHandler;
            while (!Completion.IsCompleted)
            {
                ChannelMessage next;
                try { if (!TryRead(out next)) next = await ReadAsync().ForAwait(); }
                catch (ChannelClosedException) { break; } // expected
                catch (Exception ex)
                {
                    _parent?.multiplexer?.OnInternalError(ex);
                    break;
                }

                try { handler?.Invoke(next); }
                catch { } // matches MessageCompletable
            }
        }

        internal static void Combine(ref ChannelMessageQueue? head, ChannelMessageQueue queue)
        {
            if (queue != null)
            {
                // insert at the start of the linked-list
                ChannelMessageQueue? old;
                do
                {
                    old = Volatile.Read(ref head);
                    queue._next = old;
                } while (Interlocked.CompareExchange(ref head, queue, old) != old);
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
                state => ((ChannelMessageQueue)state!).OnMessageAsyncImpl().RedisFireAndForget(), this);
        }

        internal static void Remove(ref ChannelMessageQueue? head, ChannelMessageQueue queue)
        {
            if (queue is null)
            {
                return;
            }

            bool found;
            do // if we fail due to a conflict, re-do from start
            {
                var current = Volatile.Read(ref head);
                if (current == null) return; // no queue? nothing to do
                if (current == queue)
                {
                    found = true;
                    // found at the head - then we need to change the head
                    if (Interlocked.CompareExchange(ref head, Volatile.Read(ref current._next), current) == current)
                    {
                        return; // success
                    }
                }
                else
                {
                    ChannelMessageQueue? previous = current;
                    current = Volatile.Read(ref previous._next);
                    found = false;
                    do
                    {
                        if (current == queue)
                        {
                            found = true;
                            // found it, not at the head; remove the node
                            if (Interlocked.CompareExchange(ref previous._next, Volatile.Read(ref current._next), current) == current)
                            {
                                return; // success
                            }
                            else
                            {
                                break; // exit the inner loop, and repeat the outer loop
                            }
                        }
                        previous = current;
                        current = Volatile.Read(ref previous!._next);
                    } while (current != null);
                }
            } while (found);
        }

        internal static int Count(ref ChannelMessageQueue? head)
        {
            var current = Volatile.Read(ref head);
            int count = 0;
            while (current != null)
            {
                count++;
                current = Volatile.Read(ref current._next);
            }
            return count;
        }

        internal static void WriteAll(ref ChannelMessageQueue head, in RedisChannel channel, in RedisValue message)
        {
            var current = Volatile.Read(ref head);
            while (current != null)
            {
                current.Write(channel, message);
                current = Volatile.Read(ref current._next);
            }
        }

        private ChannelMessageQueue? _next;

        private async Task OnMessageAsyncImpl()
        {
            var handler = (Func<ChannelMessage, Task>?)_onMessageHandler;
            while (!Completion.IsCompleted)
            {
                ChannelMessage next;
                try { if (!TryRead(out next)) next = await ReadAsync().ForAwait(); }
                catch (ChannelClosedException) { break; } // expected
                catch (Exception ex)
                {
                    _parent?.multiplexer?.OnInternalError(ex);
                    break;
                }

                try
                {
                    var task = handler?.Invoke(next);
                    if (task != null && task.Status != TaskStatus.RanToCompletion) await task.ForAwait();
                }
                catch { } // matches MessageCompletable
            }
        }

        internal static void MarkAllCompleted(ref ChannelMessageQueue? head)
        {
            var current = Interlocked.Exchange(ref head, null);
            while (current != null)
            {
                current.MarkCompleted();
                current = Volatile.Read(ref current._next);
            }
        }

        private void MarkCompleted(Exception? error = null)
        {
            _parent = null;
            _queue.Writer.TryComplete(error);
        }

        internal void UnsubscribeImpl(Exception? error = null, CommandFlags flags = CommandFlags.None)
        {
            var parent = _parent;
            _parent = null;
            if (parent != null)
            {
                parent.UnsubscribeAsync(Channel, null, this, flags);
            }
            _queue.Writer.TryComplete(error);
        }

        internal async Task UnsubscribeAsyncImpl(Exception? error = null, CommandFlags flags = CommandFlags.None)
        {
            var parent = _parent;
            _parent = null;
            if (parent != null)
            {
                await parent.UnsubscribeAsync(Channel, null, this, flags).ForAwait();
            }
            _queue.Writer.TryComplete(error);
        }

        /// <summary>
        /// Stop receiving messages on this channel.
        /// </summary>
        /// <param name="flags">The flags to use when unsubscribing.</param>
        public void Unsubscribe(CommandFlags flags = CommandFlags.None) => UnsubscribeImpl(null, flags);

        /// <summary>
        /// Stop receiving messages on this channel.
        /// </summary>
        /// <param name="flags">The flags to use when unsubscribing.</param>
        public Task UnsubscribeAsync(CommandFlags flags = CommandFlags.None) => UnsubscribeAsyncImpl(null, flags);
    }
}
