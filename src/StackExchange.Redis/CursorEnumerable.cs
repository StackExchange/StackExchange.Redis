using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Reprents a cursor-based data source that can consumed via foreach, synchronously or asynchronously
    /// </summary>
    public abstract class CursorEnumerable<T> : IScanningCursor, IEnumerable<T>,
        IEnumerator<T>, IEnumerator
    {
        private readonly ConnectionMultiplexer _muxer;
        private T[] _currentPage;
        private Task<PageWithCursor> _pending;
        private int _startIndexNextPage;

        internal CursorEnumerable(ConnectionMultiplexer muxer) => _muxer = muxer;

        internal void Start(long cursor, int index)
        {
            _pending = GetPageAsync(cursor);
            _startIndexNextPage = index;
        }


        internal struct PageWithCursor
        {
            public int Next { get; internal set; }
            public T[] Items { get; internal set; }
            public long Current { get; internal set; }
        }

        internal abstract Task<PageWithCursor> GetPageAsync(long cursor);

        /// <summary>
        /// Get the cursor used to fetch the current page
        /// </summary>
        public long Cursor { get; private set; }

        /// <summary>
        /// Get the page-size used to fetch the current page
        /// </summary>
        public int PageSize { get; private set; }

        /// <summary>
        /// Get the offset into the current page
        /// </summary>
        public int PageOffset { get; private set; }

        /// <summary>
        /// Get the enumerator instance
        /// </summary>
        public CursorEnumerable<T> GetAsyncEnumerator() => this;

        /// <summary>
        /// Get the enumerator instance
        /// </summary>
        public CursorEnumerable<T> GetEnumerator() => this;

        /// <summary>
        /// Move to the next record if possible
        /// </summary>
        public bool MoveNext()
        {
            if (PageOffset + 1 < _currentPage.Length)
            {
                PageOffset++;
                return true;
            }
            return _pending == null ? false : WaitNextNonEmptyPage();
        }

        /// <summary>
        /// Move to the next record if possible
        /// </summary>
        public ValueTask<bool> MoveNextAsync()
        {
            if (PageOffset + 1 < _currentPage.Length)
            {
                PageOffset++;
                return new ValueTask<bool>(true);
            }
            return _pending == null ? new ValueTask<bool>(false) : AwaitNextNonEmptyPage();
        }

        bool WaitNextNonEmptyPage()
        {
            var pending = AwaitNextNonEmptyPage();
            return pending.IsCompleted ? pending.Result : _muxer.Wait(pending.AsTask());
        }

        async ValueTask<bool> AwaitNextNonEmptyPage()
        {
            while (true)
            {
                if (_pending == null) return false;
                // finish fetching the current page, and immediatly fetch the next (if any)
                var page = await _pending;
                _pending = page.Next == 0 ? null : GetPageAsync(page.Next);

                Cursor = page.Current;
                PageOffset = _startIndexNextPage - 1;
                _startIndexNextPage = 0;

                Recycle(ref _currentPage);
                _currentPage = page.Items ?? Array.Empty<T>();
                if (_currentPage.Length != 0) return true;
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => this;

        /// <summary>
        /// Return the current value
        /// </summary>
        public T Current => _currentPage[PageOffset];

        object IEnumerator.Current => Current;

        void IDisposable.Dispose() => Recycle(ref _currentPage);

        private static void Recycle(ref T[] page)
        {
            if (page != null)
                ArrayPool<T>.Shared.Return(page);
            page = null;
        }

        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}
