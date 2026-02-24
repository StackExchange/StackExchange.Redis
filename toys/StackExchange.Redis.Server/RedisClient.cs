using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using RESPite.Buffers;
using RESPite.Messages;

namespace StackExchange.Redis.Server
{
    public sealed class RedisClient : IDisposable
    {
        private RespScanState _readState;

        public bool TryReadRequest(ReadOnlySequence<byte> data, out long consumed)
        {
            // skip past data we've already read
            data = data.Slice(_readState.TotalBytes);
            var status = RespFrameScanner.Default.TryRead(ref _readState, data);
            consumed = _readState.TotalBytes;
            switch (status)
            {
                case OperationStatus.Done:
                    _readState = default; // reset ready for the next frame
                    return true;
                case OperationStatus.NeedMoreData:
                    consumed = 0;
                    return false;
                default:
                    throw new InvalidOperationException($"Unexpected status: {status}");
            }
        }

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

            _readState = default;
        }
    }
}
