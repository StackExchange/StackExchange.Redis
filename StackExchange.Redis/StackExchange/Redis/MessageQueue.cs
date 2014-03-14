using System;
using System.Collections.Generic;
using System.Text;

namespace StackExchange.Redis
{
    sealed partial class MessageQueue
    {
        private readonly Queue<Message>
            regular = new Queue<Message>(),
            high = new Queue<Message>();

        public Message Dequeue()
        {
            lock (regular)
            {
                if (high.Count != 0)
                {
                    return high.Dequeue();
                }
                if (regular.Count != 0)
                {
                    return regular.Dequeue();
                }
            }
            return null;
        }

        public object SyncLock {  get {  return regular; } }
        public Message PeekPing(out int queueLength)
        {
            lock (regular)
            {
                Message peeked;
                queueLength = high.Count + regular.Count;
                if (high.Count != 0 && (peeked = high.Peek()).Command == RedisCommand.PING)
                {
                    return peeked;
                }
                if (regular.Count != 0 && (peeked = regular.Peek()).Command == RedisCommand.PING)
                {
                    return peeked;
                }
            }
            return null;
        }

        public void Push(Message message)
        {
            lock (regular)
            {
                (message.IsHighPriority ? high : regular).Enqueue(message);
            }
        }

        internal int Count()
        {
            lock (regular)
            {
                return high.Count + regular.Count;
            }
        }

        internal bool HasWork()
        {
            lock(regular)
            {
                return high.Count != 0 || regular.Count != 0;
            }
        }

        internal void GetStormLog(StringBuilder sb)
        {
            lock(regular)
            {
                int total = 0;
                if (high.Count == 0 && regular.Count == 0) return;
                sb.Append("Unsent: ").Append(high.Count + regular.Count).AppendLine();
                foreach (var item in high)
                {
                    if (++total >= 500) break;
                    item.AppendStormLog(sb);
                    sb.AppendLine();
                }
                foreach (var item in regular)
                {
                    if (++total >= 500) break;

                    item.AppendStormLog(sb);
                    sb.AppendLine();
                }
            }
        }

        internal bool Any()
        {
            lock(regular)
            {
                return high.Count != 0 || regular.Count != 0;
            }
        }
    }
}
