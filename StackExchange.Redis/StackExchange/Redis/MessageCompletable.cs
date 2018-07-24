using System;
using System.Text;

namespace StackExchange.Redis
{
    internal sealed class MessageCompletable : ICompletable
    {
        private readonly RedisChannel channel;

        private readonly Action<RedisChannel, RedisValue> syncHandler, asyncHandler;

        private readonly RedisValue message;
        public MessageCompletable(RedisChannel channel, RedisValue message, Action<RedisChannel, RedisValue> syncHandler, Action<RedisChannel, RedisValue> asyncHandler)
        {
            this.channel = channel;
            this.message = message;
            this.syncHandler = syncHandler;
            this.asyncHandler = asyncHandler;
        }

        public override string ToString() => (string)channel;

        public bool TryComplete(bool isAsync)
        {
            if (isAsync)
            {
                if (asyncHandler != null)
                {
                    ConnectionMultiplexer.TraceWithoutContext("Invoking (async)...: " + (string)channel, "Subscription");
                    foreach (Action<RedisChannel, RedisValue> sub in asyncHandler.GetInvocationList())
                    {
                        try { sub.Invoke(channel, message); }
                        catch { }
                    }
                    ConnectionMultiplexer.TraceWithoutContext("Invoke complete (async)", "Subscription");
                }
                return true;
            }
            else
            {
                if (syncHandler != null)
                {
                    ConnectionMultiplexer.TraceWithoutContext("Invoking (sync)...: " + (string)channel, "Subscription");
                    foreach (Action<RedisChannel, RedisValue> sub in syncHandler.GetInvocationList())
                    {
                        try { sub.Invoke(channel, message); }
                        catch { }
                    }
                    ConnectionMultiplexer.TraceWithoutContext("Invoke complete (sync)", "Subscription");
                }
                return asyncHandler == null; // anything async to do?
            }
        }

        void ICompletable.AppendStormLog(StringBuilder sb)
        {
            sb.Append("event, pub/sub: ").Append((string)channel);
        }
    }
}
