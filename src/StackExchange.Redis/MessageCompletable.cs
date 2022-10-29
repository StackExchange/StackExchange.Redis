using System;
using System.Text;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis
{
    internal sealed class MessageCompletable : ICompletable
    {
        private readonly RedisChannel channel;

        private readonly Action<RedisChannel, RedisValue> handler;

        private readonly RedisValue message;
        public MessageCompletable(RedisChannel channel, RedisValue message, Action<RedisChannel, RedisValue> handler)
        {
            this.channel = channel;
            this.message = message;
            this.handler = handler;
        }

        public override string? ToString() => (string?)channel;

        public bool TryComplete(bool isAsync)
        {
            if (isAsync)
            {
                if (handler != null)
                {
                    ConnectionMultiplexer.TraceWithoutContext("Invoking (async)...: " + (string?)channel, "Subscription");
                    if (handler.IsSingle())
                    {
                        try { handler(channel, message); } catch { }
                    }
                    else
                    {
                        foreach (var sub in handler.AsEnumerable())
                        {
                            try { sub.Invoke(channel, message); } catch { }
                        }
                    }
                    ConnectionMultiplexer.TraceWithoutContext("Invoke complete (async)", "Subscription");
                }
                return true;
            }
            else
            {
                return handler == null; // anything async to do?
            }
        }

        void ICompletable.AppendStormLog(StringBuilder sb) => sb.Append("event, pub/sub: ").Append((string?)channel);
    }
}
