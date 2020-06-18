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
        private protected readonly RedisValue initialCursor;
        private volatile IScanningCursor activeCursor;

        private protected CursorEnumerable(RedisBase redis, ServerEndPoint server, int db, int pageSize, in RedisValue cursor, int pageOffset, CommandFlags flags)
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
            public readonly RedisValue Cursor;
            public readonly T[] ValuesOversized;
            public readonly int Count;
            public readonly bool IsPooled;
            public ScanResult(RedisValue cursor, T[] valuesOversized, int count, bool isPooled)
            {
                Cursor = cursor;
                ValuesOversized = valuesOversized;
                Count = count;
                IsPooled = isPooled;
            }
        }

        private protected abstract Message CreateMessage(in RedisValue cursor);

        private protected abstract ResultProcessor<ScanResult> Processor { get; }

        private protected virtual Task<ScanResult> GetNextPageAsync(IScanningCursor obj, RedisValue cursor, out Message message)
        {
            activeCursor = obj;
            message = CreateMessage(cursor);
            return redis.ExecuteAsync(message, Processor, server);
        }

        /// <summary>
        /// Provides the ability to iterate over a cursor-based sequence of redis data, synchronously or asynchronously
        /// </summary>
        public class Enumerator : IEnumerator<T>, IScanningCursor, IAsyncEnumerator<T>
        {
            private readonly CursorEnumerable<T> parent;
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
                    Debug.Assert(_pageOffset >= 0 & _pageOffset < _pageCount & _pageOversized.Length >= _pageCount);
                    return _pageOversized[_pageOffset];
                }
            }

            /// <summary>
            /// Release all resources associated with this enumerator
            /// </summary>
            public void Dispose()
            {
                _state = State.Disposed;
                SetComplete();
            }

            private void SetComplete()
            {
                _pageOffset = _pageCount = 0;
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
            

            object IEnumerator.Current => _pageOversized[_pageOffset];

            private bool SimpleNext()
            {
                if (_pageOffset + 1 < _pageCount)
                {
                    _pageOffset++;
                    return true;
                }
                return false;
            }

            private T[] _pageOversized;
            private int _pageCount, _pageOffset, _pageIndex = -1;
            private bool _isPooled;
            private Task<ScanResult> _pending;
            private Message _pendingMessage;
            private RedisValue _currentCursor, _nextCursor;

            private volatile State _state;
            private enum State : byte
            {
                Initial,
                Running,
                Complete,
                Disposed,
            }

            private void ProcessReply(in ScanResult result, bool isInitial)
            {
                _currentCursor = _nextCursor;
                _nextCursor = result.Cursor;
                _pageOffset = isInitial ? parent.initialOffset - 1 :  -1;
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
                return Wait(pending.AsTask(), _pendingMessage);
            }

            private protected TResult Wait<TResult>(Task<TResult> pending, Message message)
            {
                if (!parent.redis.TryWait(pending)) ThrowTimeout(message);
                return pending.Result;
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
                bool isInitial = false;
                switch (_state)
                {
                    case State.Initial:
                        _pending = parent.GetNextPageAsync(this, _nextCursor, out _pendingMessage);
                        isInitial = true;
                        _state = State.Running;
                        goto case State.Running;
                    case State.Running:
                        Task<ScanResult> pending;
                        while ((pending = _pending) != null & _state == State.Running)
                        {
                            if (!pending.IsCompleted) return AwaitedNextAsync(isInitial);
                            ProcessReply(pending.Result, isInitial);
                            isInitial = false;
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

            [MethodImpl(MethodImplOptions.NoInlining)]
            private void ThrowTimeout(Message message)
            {
                try
                {
                    throw ExceptionFactory.Timeout(parent.redis.multiplexer, null, message, parent.server);
                }
                catch (Exception ex)
                {
                    TryAppendExceptionState(ex);
                    throw;
                }
            }

            private void TryAppendExceptionState(Exception ex)
            {
                try
                {
                    var data = ex.Data;
                    data["Redis-page-size"] = parent.pageSize;
                    data["Redis-page-index"] = _pageIndex;
                }
                catch { }
            }

            private async ValueTask<bool> AwaitedNextAsync(bool isInitial)
            {
                Task<ScanResult> pending;
                while ((pending = _pending) != null & _state == State.Running)
                {
                    ScanResult scanResult;
                    try
                    {
                        scanResult = await pending.ForAwait();
                    }
                    catch(Exception ex)
                    {
                        TryAppendExceptionState(ex);
                        throw;
                    }
                    ProcessReply(scanResult, isInitial);
                    isInitial = false;
                    _pageIndex++;
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
                _pageOffset = parent.initialOffset; // don't -1 here; this makes it look "right" before incremented
                _state = State.Initial;
                Recycle(ref _pageOversized, ref _isPooled);
                _pageOversized = Array.Empty<T>();
                _isPooled = false;
                _pageCount = 0;
                _pending = null;
                _pendingMessage = null;
            }

            long IScanningCursor.Cursor => (long)_currentCursor; // this may fail on cluster-proxy; I'm OK with this for now

            int IScanningCursor.PageSize => parent.pageSize;

            int IScanningCursor.PageOffset => _pageOffset;
        }

        long IScanningCursor.Cursor // this may fail on cluster-proxy; I'm OK with this for now
        {
            get { var tmp = activeCursor; return tmp?.Cursor ?? (long)initialCursor; }
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

            private protected override Task<ScanResult> GetNextPageAsync(IScanningCursor obj, RedisValue cursor, out Message message)
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
            private protected override Message CreateMessage(in RedisValue cursor) => null;
        }
    }
}
