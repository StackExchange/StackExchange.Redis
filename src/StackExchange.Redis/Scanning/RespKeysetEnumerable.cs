using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. An <see cref="IAsyncEnumerable{T}"/> over <b>keyset</b> pagination: take a page,
/// then ask again from the last item with the start excluded.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sibling of <see cref="RespScanEnumerable{T}"/>, and deliberately not the same type.</b> A cursor
/// scan resumes from an opaque <see cref="long"/> the server hands back and is resumable by construction -
/// which is why that one is an <see cref="IScanningCursor"/>. This resumes from the last <i>value</i> it
/// saw, so its position is the data rather than a token, and there is nothing server-side to report.
/// <c>VRANGE</c> is the only command shaped this way today; the shipped code says why it is not called a
/// scan - <i>"intentionally not using scan naming in case a VSCAN command is added later"</i>.
/// </para>
/// <para>
/// <b>Built on the raw page API rather than beside it</b>, exactly as the scans are: it calls back into
/// the fetcher when it runs out, so the paging loop exists once and the convenient shape cannot disagree
/// with the controllable one.
/// </para>
/// <para>
/// <b>Both faces</b>, for the reason the scans have them: the shipped surface lets a caller cast either
/// way, and which method produced the sequence must not decide which interfaces work. Hand-written rather
/// than an <c>async</c> iterator so the enumerator can own the linked token source and dispose it.
/// </para>
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class RespKeysetEnumerable<T> : IAsyncEnumerable<T>, IEnumerable<T>
{
    /// <summary>Fetch one page, starting after <paramref name="from"/> when <paramref name="excludeStart"/>.</summary>
    internal delegate ValueTask<ReadOnlyLease<T>> PageFetcher(RedisValue from, bool excludeStart, CancellationToken cancellationToken);

    /// <summary>Fetch one page synchronously.</summary>
    /// <remarks><inheritdoc cref="RespScanEnumerable{T}.SyncPageFetcher" path="/remarks"/></remarks>
    internal delegate ReadOnlyLease<T> SyncPageFetcher(RedisValue from, bool excludeStart);

    /// <summary>How the marker for the next page is read off an item.</summary>
    /// <remarks>
    /// The whole of what makes this keyset rather than cursor paging, and it is the caller's because only
    /// the caller knows which part of an element the server orders by.
    /// </remarks>
    internal delegate RedisValue MarkerSelector(in T item);

    private readonly PageFetcher _fetch;
    private readonly SyncPageFetcher _fetchSync;
    private readonly MarkerSelector _marker;
    private readonly RedisValue _start, _end;
    private readonly bool _excludeStart;
    private readonly long _pageSize;
    private readonly CancellationToken _cancellationToken;

    /// <summary>Page through a range.</summary>
    /// <param name="fetch">Fetches one page.</param>
    /// <param name="fetchSync">Fetches one page synchronously.</param>
    /// <param name="marker">Reads the next page's starting marker off an item.</param>
    /// <param name="start">The first marker; <c>default</c> for "from the beginning".</param>
    /// <param name="end">The last marker; <c>default</c> for "to the end".</param>
    /// <param name="excludeStart">Whether <paramref name="start"/> is itself excluded.</param>
    /// <param name="pageSize">How many the fetcher was told to return, which is how a short page is recognised.</param>
    /// <param name="cancellationToken">The range-level token.</param>
    internal RespKeysetEnumerable(
        PageFetcher fetch,
        SyncPageFetcher fetchSync,
        MarkerSelector marker,
        RedisValue start,
        RedisValue end,
        bool excludeStart,
        long pageSize,
        CancellationToken cancellationToken)
    {
        _fetch = fetch;
        _fetchSync = fetchSync;
        _marker = marker;
        _start = start;
        _end = end;
        _excludeStart = excludeStart;
        _pageSize = pageSize;
        _cancellationToken = cancellationToken;
    }

    /// <inheritdoc cref="RespScanEnumerable{T}.GetAsyncEnumerator"/>
    /// <param name="cancellationToken">The enumerator's token, supplied by <c>await foreach</c>.</param>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var outer = _cancellationToken;
        CancellationTokenSource? linked = null;
        CancellationToken effective;

        if (cancellationToken == outer || !cancellationToken.CanBeCanceled)
        {
            effective = outer; // same token, or nothing new to honour
        }
        else if (!outer.CanBeCanceled)
        {
            effective = cancellationToken;
        }
        else
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(outer, cancellationToken);
            effective = linked.Token;
        }

        return new Enumerator(this, effective, linked);
    }

    /// <summary>The synchronous face, for the shipped <see cref="IEnumerable{T}"/> signature.</summary>
    public IEnumerator<T> GetEnumerator() => new Enumerator(this, _cancellationToken, null);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator(RespKeysetEnumerable<T> parent, CancellationToken cancellationToken, CancellationTokenSource? linked)
        : IAsyncEnumerator<T>, IEnumerator<T>
    {
        private ReadOnlyLease<T> _page = ReadOnlyLease<T>.Empty;
        private bool _finished;
        private int _index = -1;
        private int _pageLength;

        /// <summary>Where the next page starts; the last item yielded, once anything has been.</summary>
        private RedisValue _from = parent._start;

        /// <summary>
        /// Excluded from the second page onwards, always.
        /// </summary>
        /// <remarks>
        /// The page ends ON the last item yielded, so asking again inclusively would yield it twice. The
        /// caller's own <c>Exclude</c> only decides the FIRST page, which is why this is a field rather
        /// than a constant: it starts as whatever was asked for and is true for ever after.
        /// </remarks>
        private bool _excludeStart = parent._excludeStart;

        public T Current => _page.Span[_index];

        object? IEnumerator.Current => Current;

        public async ValueTask<bool> MoveNextAsync()
        {
            while (true)
            {
                if (TryAdvance(out var more)) return more;

                cancellationToken.ThrowIfCancellationRequested();

                // the token is checked here and NOT handed to the send, as the scans do: the executor
                // refuses a live token outright, and a single page is bounded work
                Reset(await parent._fetch(_from, _excludeStart, cancellationToken).ForAwait());
            }
        }

        public bool MoveNext()
        {
            while (true)
            {
                if (TryAdvance(out var more)) return more;

                cancellationToken.ThrowIfCancellationRequested();
                Reset(parent._fetchSync(_from, _excludeStart));
            }
        }

        /// <summary>Take the next item from the page in hand, or say that another page is needed.</summary>
        /// <param name="more">Whether there is an item, when this returns <c>true</c>.</param>
        /// <returns><c>false</c> when the caller must fetch.</returns>
        private bool TryAdvance(out bool more)
        {
            if (_finished)
            {
                more = false;
                return true;
            }

            if (_index + 1 < _pageLength)
            {
                _index++;

                // the side effect the paging turns on: the last item yielded is the next page's start
                _from = parent._marker(_page.Span[_index]);
                _excludeStart = true;
                more = true;
                return true;
            }

            // the page is exhausted. A short page means the server had no more; reaching the requested end
            // means there is nothing left worth asking for. Both are the shipped rules, and both avoid a
            // final round trip that could only come back empty.
            if (_pageLength > 0
                && (_pageLength < parent._pageSize || (!parent._end.IsNull && parent._end == _from)))
            {
                Finish();
                more = false;
                return true;
            }

            if (_pageLength == 0 && _index >= 0)
            {
                // an empty page after a full one: nothing more
                Finish();
                more = false;
                return true;
            }

            more = false;
            return false;
        }

        private void Reset(ReadOnlyLease<T> page)
        {
            _page.Dispose();
            _page = page;
            _pageLength = page.Length;
            _index = -1;

            if (_pageLength == 0) Finish();
        }

        private void Finish()
        {
            _finished = true;
            _page.Dispose();
            _page = ReadOnlyLease<T>.Empty;
            _pageLength = 0;
            _index = -1;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public void Dispose()
        {
            _page.Dispose();
            _page = ReadOnlyLease<T>.Empty;
            _pageLength = 0;
            linked?.Dispose();
        }

        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}
