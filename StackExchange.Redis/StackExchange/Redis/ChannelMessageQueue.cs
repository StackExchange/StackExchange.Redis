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
        /// <summary>
        /// See Object.ToString
        /// </summary>
        public override string ToString() => ((string)Channel) + ":" + ((string)Value);

        /// <summary>
        /// See Object.GetHashCode
        /// </summary>
        public override int GetHashCode() => Channel.GetHashCode() ^ Value.GetHashCode();
        /// <summary>
        /// See Object.Equals
        /// </summary>
        public override bool Equals(object obj) => obj is ChannelMessage cm
            && cm.Channel == Channel && cm.Value == Value;
        internal ChannelMessage(RedisChannel channel, RedisValue value)
        {
            Channel = channel;
            Value = value;
        }

        /// <summary>
        /// The channel that the message was broadcast to
        /// </summary>
        public RedisChannel Channel { get; }
        /// <summary>
        /// The value that was broadcast
        /// </summary>
        public RedisValue Value { get; }
    }


    /// <summary>
    /// Represents a message queue of ordered pub/sub notifications
    /// </summary>
    /// <remarks>To create a ChannelMessageQueue, use ISubscriber.Subscribe[Async](RedisKey)</remarks>
    public sealed class ChannelMessageQueue
    {
        private readonly Channel<ChannelMessage> _channel;
        private readonly RedisChannel _redisChannel;
        private RedisSubscriber _parent;

        /// <summary>
        /// See Object.ToString
        /// </summary>
        public override string ToString() => (string)_redisChannel;

        /// <summary>
        /// Indicates if all messages that will be received have been drained from this channel
        /// </summary>
        public bool IsCompleted { get; private set; }

        internal ChannelMessageQueue(RedisChannel redisChannel, RedisSubscriber parent)
        {
            _redisChannel = redisChannel;
            _parent = parent;
            _channel = Channel.CreateUnbounded<ChannelMessage>(s_ChannelOptions);
            _channel.Reader.Completion.ContinueWith(
                (t, state) => ((ChannelMessageQueue)state).IsCompleted = true, this, TaskContinuationOptions.ExecuteSynchronously);
        }
        static readonly UnboundedChannelOptions s_ChannelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        };
        internal void Subscribe(CommandFlags flags) => _parent.Subscribe(_redisChannel, HandleMessage, flags);
        internal Task SubscribeAsync(CommandFlags flags) => _parent.SubscribeAsync(_redisChannel, HandleMessage, flags);

        private void HandleMessage(RedisChannel channel, RedisValue value)
        {
            var writer = _channel.Writer;
            if (channel.IsNull && value.IsNull) // see ForSyncShutdown
            {
                writer.TryComplete();
            }
            else
            {
                writer.TryWrite(new ChannelMessage(channel, value));
            }
        }


        /// <summary>
        /// Consume a message from the channel
        /// </summary>
        public ValueTask<ChannelMessage> ReadAsync(CancellationToken cancellationToken = default)
            => _channel.Reader.ReadAsync(cancellationToken);

        /// <summary>
        /// Attempt to synchronously consume a message from the channel
        /// </summary>
        public bool TryRead(out ChannelMessage item) => _channel.Reader.TryRead(out item);

        /// <summary>
        /// Attempt to query the backlog length of the queue
        /// </summary>
        public bool TryGetCount(out int count)
        {
            // get this using the reflection
            try
            {
                var prop = _channel.GetType().GetProperty("ItemsCountForDebugger", BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop != null)
                {
                    count = (int)prop.GetValue(_channel);
                    return true;
                }
            }
            catch { }
            count = default;
            return false;
        }

        private Delegate _onMessageHandler;
        private void AssertOnMessage(Delegate handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (Interlocked.CompareExchange(ref _onMessageHandler, handler, null) != null)
                throw new InvalidOperationException("Only a single " + nameof(OnMessage) + " is allowed");
        }
        /// <summary>
        /// Create a message loop that processes messages sequentially
        /// </summary>
        public void OnMessage(Action<RedisChannel, RedisValue> handler)
        {
            AssertOnMessage(handler);
            ThreadPool.QueueUserWorkItem(
                state => ((ChannelMessageQueue)state).OnMessageSyncImpl(), this);
        }

        private async void OnMessageSyncImpl()
        {
            var handler = (Action<RedisChannel, RedisValue>)_onMessageHandler;
            while (!IsCompleted)
            {
                ChannelMessage next;
                try { if (!TryRead(out next)) next = await ReadAsync().ConfigureAwait(false); }
                catch (ChannelClosedException) { break; } // expected
                catch (Exception ex)
                {
                    _parent.multiplexer?.OnInternalError(ex);
                    break;
                }

                try { handler.Invoke(next.Channel, next.Value); }
                catch { } // matches MessageCompletable
            }
        }

        /// <summary>
        /// Create a message loop that processes messages sequentially
        /// </summary>
        public void OnMessage(Func<RedisChannel, RedisValue, Task> handler)
        {
            AssertOnMessage(handler);
            ThreadPool.QueueUserWorkItem(
                state => ((ChannelMessageQueue)state).OnMessageAsyncImpl(), this);
        }

        private async void OnMessageAsyncImpl()
        {
            var handler = (Func<RedisChannel, RedisValue, Task>)_onMessageHandler;
            while (!IsCompleted)
            {
                ChannelMessage next;
                try { if (!TryRead(out next)) next = await ReadAsync().ConfigureAwait(false); }
                catch (ChannelClosedException) { break; } // expected
                catch (Exception ex)
                {
                    _parent.multiplexer?.OnInternalError(ex);
                    break;
                }

                try
                {
                    var task = handler.Invoke(next.Channel, next.Value);
                    if (task != null) await task.ConfigureAwait(false);
                }
                catch { } // matches MessageCompletable
            }
        }
        internal void UnsubscribeImpl(Exception error = null, CommandFlags flags = CommandFlags.None)
        {
            var parent = _parent;
            if (parent != null)
            {
                parent.UnsubscribeAsync(_redisChannel, HandleMessage, flags);
                _parent = null;
                _channel.Writer.TryComplete(error);
            }
        }
        internal async Task UnsubscribeAsyncImpl(Exception error = null, CommandFlags flags = CommandFlags.None)
        {
            var parent = _parent;
            if (parent != null)
            {
                await parent.UnsubscribeAsync(_redisChannel, HandleMessage, flags).ConfigureAwait(false);
                _parent = null;
                _channel.Writer.TryComplete(error);
            }
        }

        internal static bool IsOneOf(Action<RedisChannel, RedisValue> handler)
        {
            try
            {
                return handler != null && handler.Target is ChannelMessageQueue
                    && handler.Method.Name == nameof(HandleMessage);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stop receiving messages on this channel
        /// </summary>
        public void Unsubscribe(CommandFlags flags = CommandFlags.None) => UnsubscribeImpl(null, flags);
        /// <summary>
        /// Stop receiving messages on this channel
        /// </summary>
        public Task UnsubscribeAsync(CommandFlags flags = CommandFlags.None) => UnsubscribeAsyncImpl(null, flags);
    }
}
