using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class SubscriberWrapper : RedisWrapperBase<ISubscriber>, ISubscriber
    {
        // Stores outer->inner handler mappings. Needed to unsubscribe a specific outer handler.
        private readonly Dictionary<Action<RedisChannel, RedisValue>, Action<RedisChannel, RedisValue>> _mappedHandlers =
            new Dictionary<Action<RedisChannel, RedisValue>, Action<RedisChannel, RedisValue>>();

        // Stores active inner subscriptions. Needed to unsubscribe all.
        private readonly Dictionary<RedisChannel, Action<RedisChannel, RedisValue>> _innerSubscriptions =
            new Dictionary<RedisChannel, Action<RedisChannel, RedisValue>>();

        internal SubscriberWrapper(ISubscriber inner, RedisKey prefix)
            : base(inner, prefix)
        {
        }

        public EndPoint IdentifyEndpoint(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.IdentifyEndpoint(this.ToInner(channel), flags);
        }

        public Task<EndPoint> IdentifyEndpointAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.IdentifyEndpointAsync(this.ToInner(channel), flags);
        }

        public bool IsConnected(RedisChannel channel = default(RedisChannel))
        {
            return this.Inner.IsConnected(this.ToInner(channel));
        }

        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.Publish(this.ToInner(channel), message, flags);
        }

        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.PublishAsync(this.ToInner(channel), message, flags);
        }

        public void Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None)
        {
            var innerChannel = this.ToInner(channel);
            var innerHandler = this.GetInnerHandler(handler);

            this.Inner.Subscribe(innerChannel, innerHandler, flags);
            this.RegisterInnerSubscription(innerChannel, innerHandler);
        }

        public async Task SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None)
        {
            var innerChannel = this.ToInner(channel);
            var innerHandler = this.GetInnerHandler(handler);

            await this.Inner.SubscribeAsync(innerChannel, innerHandler, flags);
            this.RegisterInnerSubscription(innerChannel, innerHandler);
        }

        public EndPoint SubscribedEndpoint(RedisChannel channel)
        {
            return this.Inner.SubscribedEndpoint(this.ToInner(channel));
        }

        public void Unsubscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler = null, CommandFlags flags = CommandFlags.None)
        {
            var innerChannel = this.ToInner(channel);
            var innerHandler = this.GetInnerHandler(handler, remember: false);

            this.Inner.Unsubscribe(innerChannel, innerHandler, flags);
            this.UnregisterInnerSubscription(innerChannel, innerHandler);
        }

        public Task UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler = null, CommandFlags flags = CommandFlags.None)
        {
            var innerChannel = this.ToInner(channel);
            var innerHandler = this.GetInnerHandler(handler, remember: false);
            return this.InnerUnsubscribeAsync(innerChannel, innerHandler, flags);
        }

        public void UnsubscribeAll(CommandFlags flags = CommandFlags.None)
        {
            Task task = this.UnsubscribeAllAsync(flags);

            if ((flags & CommandFlags.FireAndForget) == 0)
            {
                this.Wait(task);
            }
        }

        public Task UnsubscribeAllAsync(CommandFlags flags = CommandFlags.None)
        {
            List<Task> taskList;

            lock (_innerSubscriptions)
            {
                taskList = new List<Task>(_innerSubscriptions.Count);

                foreach (var entry in _innerSubscriptions.ToArray())
                {
                    taskList.Add(this.InnerUnsubscribeAsync(entry.Key, entry.Value, flags));
                }
            }

#if NET40
            return Task.Factory.ContinueWhenAll(taskList.ToArray(), _ => _, TaskContinuationOptions.ExecuteSynchronously);
#else
            return Task.WhenAll(taskList);
#endif
        }

        private async Task InnerUnsubscribeAsync(RedisChannel innerChannel, Action<RedisChannel, RedisValue> innerHandler = null, CommandFlags flags = CommandFlags.None)
        {
            await this.Inner.UnsubscribeAsync(innerChannel, innerHandler, flags);
            this.UnregisterInnerSubscription(innerChannel, innerHandler);
        }

        public TimeSpan Ping(CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.Ping(flags);
        }

#if DEBUG
        public string ClientGetName(CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.ClientGetName(flags);
        }

        public void Quit(CommandFlags flags = CommandFlags.None)
        {
            this.Inner.Quit(flags);
        }
#endif

        internal Action<RedisChannel, RedisValue> GetInnerHandler(Action<RedisChannel, RedisValue> outer, bool remember = true)
        {
            if (outer == null)
            {
                return outer;
            }

            lock (_mappedHandlers)
            {
                Action<RedisChannel, RedisValue> inner;

                if (!_mappedHandlers.TryGetValue(outer, out inner))
                {
                    inner = this.CreateInnerHandler(outer);

                    if (remember)
                    {
                        _mappedHandlers.Add(outer, inner);
                    }
                }

                return inner;
            }
        }

        private bool TryGetOuter(RedisChannel inner, out RedisChannel outer)
        {
            byte[] prefix = this.Prefix.Value;

            if (prefix == null || prefix.Length == 0)
            {
                // Special case when wrapper does not use a prefix (outer == inner)
                outer = inner;
                return true;
            }

            byte[] innerValue = inner.Value;

            // Not matching when inner value is null or shorter than prefix
            if (innerValue == null || innerValue.Length < prefix.Length)
            {
                outer = default(RedisChannel);
                return false;
            }

            // Byte-by-byte comparison of inner value and prefix
            for (int i = 0; i < prefix.Length; ++i)
            {
                if (prefix[i] != innerValue[i])
                {
                    outer = default(RedisChannel);
                    return false; // Not matching!
                }
            }

            // Remove prefix from inner value to construct outer channel value.
            byte[] outerValue = new byte[innerValue.Length - prefix.Length];
            Buffer.BlockCopy(innerValue, prefix.Length, outerValue, 0, outerValue.Length);

            outer = outerValue;
            return true;
        }

        private Action<RedisChannel, RedisValue> CreateInnerHandler(Action<RedisChannel, RedisValue> outerHandler)
        {
            return (innerChannel, value) =>
            {
                RedisChannel outerChannel;

                // Invoke outer handler only if the inner channel can be properly 'unprefixed'
                if (this.TryGetOuter(innerChannel, out outerChannel))
                {
                    outerHandler(outerChannel, value);
                }
            };
        }

        internal bool IsSubscribedToInnerChannel(RedisChannel channel)
        {
            lock (_innerSubscriptions)
            {
                return _innerSubscriptions.ContainsKey(channel);
            }
        }

        internal Action<RedisChannel, RedisValue> GetInnerHandlerForChannel(RedisChannel channel)
        {
            lock (_innerSubscriptions)
            {
                Action<RedisChannel, RedisValue> handler;
                _innerSubscriptions.TryGetValue(channel, out handler);
                return handler;
            }
        }

        internal void RegisterInnerSubscription(RedisChannel channel, Action<RedisChannel, RedisValue> handler)
        {
            this.UpdateInnerSubscription(channel, handler, remove: false);
        }

        internal void UnregisterInnerSubscription(RedisChannel channel, Action<RedisChannel, RedisValue> handler)
        {
            this.UpdateInnerSubscription(channel, handler, remove: true);
        }

        private void UpdateInnerSubscription(RedisChannel channel, Action<RedisChannel, RedisValue> handler, bool remove)
        {
            lock (_innerSubscriptions)
            {
                if (handler == null && remove)
                {
                    _innerSubscriptions.Remove(channel);
                    return;
                }
                else
                {
                    Action<RedisChannel, RedisValue> updated;
                    _innerSubscriptions.TryGetValue(channel, out updated);

                    if (remove)
                    {
                        updated -= handler;
                    }
                    else
                    {
                        updated += handler;
                    }

                    if (updated == null)
                    {
                        _innerSubscriptions.Remove(channel);
                    }
                    else
                    {
                        _innerSubscriptions[channel] = updated;
                    }
                }
            }
        }
    }
}
