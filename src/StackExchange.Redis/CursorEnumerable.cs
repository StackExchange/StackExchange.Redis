using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Provides the ability to iterate over a cursor-based sequence of redis data, synchronously or asynchronously
    /// </summary>
    internal abstract class CursorEnumerable<T> : IEnumerable<T>, IScanningCursor, IAsyncEnumerable<T>
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
        public Enumerator GetEnumerator() => new Enumerator(this, default);
        /// <summary>
        /// Gets an enumerator for the sequence
        /// </summary>
        public Enumerator GetAsyncEnumerator(CancellationToken cancellationToken) => new Enumerator(this, cancellationToken);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(CancellationToken cancellationToken) => GetAsyncEnumerator(cancellationToken);

        internal readonly struct ScanResult
        {
            public readonly long Cursor;
            public readonly T[] ValuesOversized;
            public readonly int Count;
            public readonly bool IsPooled;
            public ScanResult(long cursor, T[] valuesOversized, int count, bool isPooled)
            {
                Cursor = cursor;
                ValuesOversized = valuesOversized;
                Count = count;
                IsPooled = isPooled;
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
        public class Enumerator : IEnumerator<T>, IScanningCursor, IAsyncEnumerator<T>
        {
            private CursorEnumerable<T> parent;
            private readonly CancellationToken cancellationToken;
            internal Enumerator(CursorEnumerable<T> parent, CancellationToken cancellationToken)
            {
                this.parent = parent ?? throw new ArgumentNullException(nameof(parent));
                this.cancellationToken = cancellationToken;
                Reset();
            }

            /// <summary>
            /// Gets the current value of the enumerator
            /// </summary>
            public T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    Debug.Assert(_pageIndex >= 0 & _pageIndex < _pageCount & _pageOversized.Length >= _pageCount);
                    return _pageOversized[_pageIndex];
                }
            }

            /// <summary>
            /// Release all resources associated with this enumerator
            /// </summary>
            public void Dispose()
            {
                _state = State.Disposed;
                SetComplete();
                parent = null;
            }

            private void SetComplete()
            {
                _pageIndex = _pageCount = 0;
                Recycle(ref _pageOversized, ref _isPooled);
                switch (_state)
                {
                    case State.Initial:
                    case State.Running:
                        _state = State.Complete;
                        break;
                }
            }

            /// <summary>
            /// Release all resources associated with this enumerator
            /// </summary>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
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
            private bool _isPooled;
            private Task<ScanResult> _pending;
            private Message _pendingMessage;
            private int _pageIndex;
            private long _currentCursor, _nextCursor;

            private volatile State _state;
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
                _pageIndex = _state == State.Initial ? parent.initialOffset - 1 :  -1;
                Recycle(ref _pageOversized, ref _isPooled); // recycle any existing data
                _pageOversized = result.ValuesOversized ?? Array.Empty<T>();
                _isPooled = result.IsPooled;
                _pageCount = result.Count;
                if (_nextCursor == RedisBase.CursorUtils.Origin)
                {   // eof
                    _pending = null;
                    _pendingMessage = null;
                }
                else
                {
                    // start the next page right away
                    _pending = parent.GetNextPageAsync(this, _nextCursor, out _pendingMessage);
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

            private ValueTask<bool> SlowNextAsync()
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (_state)
                {
                    case State.Initial:
                        _pending = parent.GetNextPageAsync(this, _nextCursor, out _pendingMessage);
                        _state = State.Running;
                        goto case State.Running;
                    case State.Running:
                        Task<ScanResult> pending;
                        while ((pending = _pending) != null & _state == State.Running)
                        {
                            if (!pending.IsCompleted) return AwaitedNextAsync();
                            ProcessReply(pending.Result);
                            if (SimpleNext()) return new ValueTask<bool>(true);
                        }
                        SetComplete();
                        return default;
                    case State.Complete:
                    case State.Disposed:
                    default:
                        return default;
                }
            }

            private async ValueTask<bool> AwaitedNextAsync()
            {
                Task<ScanResult> pending;
                while ((pending = _pending) != null & _state == State.Running)
                {
                    ProcessReply(await pending.ForAwait());
                    if (SimpleNext()) return true;
                }
                SetComplete();
                return false;
            }

            static void Recycle(ref T[] array, ref bool isPooled)
            {
                var tmp = array;
                array = null;
                if (tmp != null && tmp.Length != 0 && isPooled)
                    ArrayPool<T>.Shared.Return(tmp);
                isPooled = false;
            }

            /// <summary>
            /// Reset the enumerator
            /// </summary>
            public void Reset()
            {
                if (_state == State.Disposed) throw new ObjectDisposedException(GetType().Name);
                _nextCursor = _currentCursor = parent.initialCursor;
                _pageIndex = parent.initialOffset; // don't -1 here; this makes it look "right" before incremented
                _state = State.Initial;
                Recycle(ref _pageOversized, ref _isPooled);
                _pageOversized = Array.Empty<T>();
                _isPooled = false;
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

        internal static CursorEnumerable<T> From(RedisBase redis, ServerEndPoint server, Task<T[]> pending, int pageOffset)
            => new SingleBlockEnumerable(redis, server, pending, pageOffset);

        class SingleBlockEnumerable : CursorEnumerable<T>
        {
            private readonly Task<T[]> _pending;
            public SingleBlockEnumerable(RedisBase redis, ServerEndPoint server, 
                Task<T[]> pending, int pageOffset) : base(redis, server, 0, int.MaxValue, 0, pageOffset, default)
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
                var arr = (await _pending.ForAwait()) ?? Array.Empty<T>();
                return new ScanResult(RedisBase.CursorUtils.Origin, arr, arr.Length, false);
            }
            private protected override ResultProcessor<ScanResult> Processor => null;
            private protected override Message CreateMessage(long cursor) => null;
        }
    }
}
