using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Provides the ability to iterate over a cursor-based sequence of redis data, synchronously or asynchronously
    /// </summary>
    public abstract class CursorEnumerable<T> : IEnumerable<T>, IScanningCursor
    {
        private readonly RedisBase redis;
        private readonly ServerEndPoint server;
        private protected readonly int db;
        private protected readonly CommandFlags flags;
        private protected readonly int pageSize, initialOffset;
        private protected readonly long initialCursor;
        private volatile IScanningCursor activeCursor;

        private protected CursorEnumerable(RedisBase redis, ServerEndPoint server, int db, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            if (pageOffset < 0) throw new ArgumentOutOfRangeException(nameof(pageOffset));
            this.redis = redis;
            this.server = server;
            this.db = db;
            this.pageSize = pageSize;
            this.flags = flags;
            initialCursor = cursor;
            initialOffset = pageOffset;
        }

        /// <summary>
        /// Gets an enumerator for the sequence
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(this);
        /// <summary>
        /// Gets an enumerator for the sequence
        /// </summary>
        public Enumerator GetAsyncEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal readonly struct ScanResult
        {
            public readonly long Cursor;
            public readonly T[] ValuesOversized;
            public readonly int Count;
            public ScanResult(long cursor, T[] valuesOversized, int count)
            {
                Cursor = cursor;
                ValuesOversized = valuesOversized;
                Count = count;
            }
        }

        private protected abstract Message CreateMessage(long cursor);

        private protected abstract ResultProcessor<ScanResult> Processor { get; }

        private protected virtual Task<ScanResult> GetNextPageAsync(IScanningCursor obj, long cursor, out Message message)
        {
            activeCursor = obj;
            message = CreateMessage(cursor);
            return redis.ExecuteAsync(message, Processor, server);
        }

        private protected TResult Wait<TResult>(Task<TResult> pending, Message message) {
            if(!redis.TryWait(pending))
                throw ExceptionFactory.Timeout(redis.multiplexer, null, message, server);
            return pending.Result;
        }

        /// <summary>
        /// Provides the ability to iterate over a cursor-based sequence of redis data, synchronously or asynchronously
        /// </summary>
        public class Enumerator : IEnumerator<T>, IScanningCursor
        {
            private CursorEnumerable<T> parent;
            internal Enumerator(CursorEnumerable<T> parent)
            {
                this.parent = parent ?? throw new ArgumentNullException(nameof(parent));
                Reset();
            }

            /// <summary>
            /// Gets the current value of the enumerator
            /// </summary>
            public T Current => _pageOversized[_pageIndex];

            void IDisposable.Dispose()
            {
                state = State.Disposed;
                Recycle(ref _pageOversized);
                parent = null; 
            }

            object IEnumerator.Current => _pageOversized[_pageIndex];

            private bool SimpleNext()
            {
                if (_pageIndex + 1 < _pageCount)
                {
                    _pageIndex++;
                    return true;
                }
                return false;
            }

            private T[] _pageOversized;
            private int _pageCount;
            private Task<ScanResult> _pending;
            private Message _pendingMessage;
            private int _pageIndex;
            private long _currentCursor, _nextCursor;

            private State state;
            private enum State : byte
            {
                Initial,
                Running,
                Complete,
                Disposed,
            }

            private void ProcessReply(in ScanResult result)
            {
                _currentCursor = _nextCursor;
                _nextCursor = result.Cursor;
                _pageIndex = state == State.Initial ? parent.initialOffset - 1 :  -1;
                Recycle(ref _pageOversized); // recycle any existing data
                _pageOversized = result.ValuesOversized;
                _pageCount = result.Count;
                if (_nextCursor == RedisBase.CursorUtils.Origin)
                {   // eof
                    _pending = null;
                    _pendingMessage = null;
                }
                else
                {
                    // start the next page right away
                    parent.GetNextPageAsync(this, _nextCursor, out _pendingMessage);
                }
            }

            /// <summary>
            /// Try to move to the next item in the sequence
            /// </summary>
            public bool MoveNext() => SimpleNext() || SlowNextSync();

            bool SlowNextSync()
            {
                var pending = SlowNextAsync();
                if (pending.IsCompletedSuccessfully) return pending.Result;
                return parent.Wait(pending.AsTask(), _pendingMessage);
            }

            /// <summary>
            /// Try to move to the next item in the sequence
            /// </summary>
            public ValueTask<bool> MoveNextAsync()
            {
                if(SimpleNext()) return new ValueTask<bool>(true);
                return SlowNextAsync();
            }

            private async ValueTask<bool> SlowNextAsync()
            {
                switch (state)
                {
                    case State.Complete: return false;
                    case State.Initial:
                        _pending = parent.GetNextPageAsync(this, _nextCursor, out _pendingMessage);
                        state = State.Running;
                        goto case State.Running;
                    case State.Running:
                        while (_pending != null)
                        {
                            ProcessReply(await _pending);
                            if (SimpleNext()) return true;
                        }
                        // we're exhausted
                        state = State.Complete;
                        return false;
                    case State.Disposed:
                    default:
                        throw new ObjectDisposedException(GetType().Name);
                }
            }

            static void Recycle(ref T[] array)
            {
                var tmp = array;
                array = null;
                if (tmp != null && tmp.Length != 0)
                    ArrayPool<T>.Shared.Return(tmp);
            }

            /// <summary>
            /// Reset the enumerator
            /// </summary>
            public void Reset()
            {
                if (state == State.Disposed) throw new ObjectDisposedException(GetType().Name);
                _nextCursor = _currentCursor = parent.initialCursor;
                _pageIndex = parent.initialOffset; // don't -1 here; this makes it look "right" before incremented
                state = State.Initial;
                Recycle(ref _pageOversized);
                _pageCount = 0;
                _pending = null;
                _pendingMessage = null;
            }

            long IScanningCursor.Cursor => _currentCursor;

            int IScanningCursor.PageSize => parent.pageSize;

            int IScanningCursor.PageOffset => _pageIndex;
        }

        long IScanningCursor.Cursor
        {
            get { var tmp = activeCursor; return tmp?.Cursor ?? initialCursor; }
        }

        int IScanningCursor.PageSize => pageSize;

        int IScanningCursor.PageOffset
        {
            get { var tmp = activeCursor; return tmp?.PageOffset ?? initialOffset; }
        }

        internal static CursorEnumerable<T> From(RedisBase redis, ServerEndPoint server, Task<T[]> pending)
            => new SingleBlockEnumerable(redis, server, pending);

        class SingleBlockEnumerable : CursorEnumerable<T>
        {
            private readonly Task<T[]> _pending;
            public SingleBlockEnumerable(RedisBase redis, ServerEndPoint server, 
                Task<T[]> pending) : base(redis, server, 0, int.MaxValue, 0, 0, default)
            {
                _pending = pending;
            }

            private protected override Task<ScanResult> GetNextPageAsync(IScanningCursor obj, long cursor, out Message message)
            {
                message = null;
                return AwaitedGetNextPageAsync();
            }
            private async Task<ScanResult> AwaitedGetNextPageAsync()
            {
                var arr = (await _pending) ?? Array.Empty<T>();
                return new ScanResult(RedisBase.CursorUtils.Origin, arr, arr.Length);
            }
            private protected override ResultProcessor<ScanResult> Processor => null;
            private protected override Message CreateMessage(long cursor) => null;
        }
    }
}
