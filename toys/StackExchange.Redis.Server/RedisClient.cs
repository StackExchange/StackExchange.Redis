using System;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace StackExchange.Redis.Server
{
    public class RedisClient(RedisServer.Node node) : IDisposable
    {
        public RedisServer.Node Node => node;
        internal int SkipReplies { get; set; }
        internal bool ShouldSkipResponse()
        {
            if (SkipReplies > 0)
            {
                SkipReplies--;
                return true;
            }
            return false;
        }
        private HashSet<RedisChannel> _subscripions;
        public int SubscriptionCount => _subscripions?.Count ?? 0;
        internal int Subscribe(RedisChannel channel)
        {
            if (_subscripions == null) _subscripions = new HashSet<RedisChannel>();
            _subscripions.Add(channel);
            return _subscripions.Count;
        }
        internal int Unsubscribe(RedisChannel channel)
        {
            if (_subscripions == null) return 0;
            _subscripions.Remove(channel);
            return _subscripions.Count;
        }
        public int Database { get; set; }
        public string Name { get; set; }
        internal IDuplexPipe LinkedPipe { get; set; }
        public bool Closed { get; internal set; }
        public int Id { get; internal set; }
        public bool IsAuthenticated { get; internal set; }
        public RedisProtocol Protocol { get; internal set; } = RedisProtocol.Resp2;
        public long ProtocolVersion => Protocol is RedisProtocol.Resp2 ? 2 : 3;

        public void Dispose()
        {
            Closed = true;
            var pipe = LinkedPipe;
            LinkedPipe = null;
            if (pipe != null)
            {
                try { pipe.Input.CancelPendingRead(); } catch { }
                try { pipe.Input.Complete(); } catch { }
                try { pipe.Output.CancelPendingFlush(); } catch { }
                try { pipe.Output.Complete(); } catch { }
                if (pipe is IDisposable d) try { d.Dispose(); } catch { }
            }
        }

        private int _activeSlot = ServerSelectionStrategy.NoSlot;
        internal void ResetAfterRequest() => _activeSlot = ServerSelectionStrategy.NoSlot;
        public virtual void AssertKey(in RedisKey key)
        {
            if (Node is { } node)
            {
                if (node.CheckCrossSlot)
                {
                    var slot = RespServer.GetHashSlot(key);
                    if (_activeSlot is ServerSelectionStrategy.NoSlot)
                    {
                        _activeSlot = slot;
                    }
                    else if (_activeSlot != slot)
                    {
                        CrossSlotException.Throw();
                    }
                }
                node.AssertKey(key);
            }
        }
    }

    internal sealed class CrossSlotException : Exception
    {
        public static void Throw() => throw new CrossSlotException();
    }
}
