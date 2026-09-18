using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. An <see cref="IAsyncEnumerable{T}"/> over a cursor scan, driven by the raw page API.
/// </summary>
/// <typeparam name="T">The element type of the scan.</typeparam>
/// <remarks>
/// <para>
/// <b>Built on the raw API, not beside it.</b> It calls back into the page fetcher whenever it runs out,
/// so there is one implementation of the cursor loop and the convenient shape cannot disagree with the
/// controllable one - the same arrangement the eager <c>ToArray</c> shapes have with the deferred replies.
/// </para>
/// <para>
/// <b>It is a resumable cursor</b> - <see cref="IScanningCursor"/>, as the existing <c>CursorEnumerable</c>
/// is - so the progress of an in-flight scan can be read off it and a later scan told where to resume. The
/// enumerator carries the live position; the enumerable reports the active enumerator's, falling back to
/// where it was told to start.
/// </para>
/// <para>
/// <b>Hand-written rather than an <c>async</c> iterator</b>, because the enumerator has to <i>be</i> an
/// <see cref="IScanningCursor"/> and a compiler-generated state machine cannot implement an interface of
/// ours. That is also what lets the enumerator own the linked token source and dispose it.
/// </para>
/// </remarks>
internal sealed class RespScanEnumerable<T> : IAsyncEnumerable<T>, IEnumerable<T>, IScanningCursor
{
    /// <summary>Fetch one page, starting at <paramref name="cursor"/>.</summary>
    internal delegate ValueTask<RespScanPage<T>> PageFetcher(long cursor, CancellationToken cancellationToken);

    /// <summary>Fetch one page synchronously.</summary>
    /// <remarks>
    /// <b>A real synchronous send, not a blocking wait on the asynchronous one.</b> The context surface
    /// has both, so the sync face can use <c>Send</c> rather than parking a thread on a <c>ValueTask</c> -
    /// which is what makes offering both faces honest rather than a sync-over-async trap.
    /// </remarks>
    internal delegate RespScanPage<T> SyncPageFetcher(long cursor);

    private readonly PageFetcher _fetch;
    private readonly long _initialCursor;
    private readonly int _pageSize, _initialOffset;
    private readonly CancellationToken _cancellationToken;
    private readonly SyncPageFetcher _fetchSync;
    private volatile IScanningCursor? _active;

    /// <summary>Start a scan that fetches pages through <paramref name="fetch"/>.</summary>
    /// <param name="fetch">Fetches one page from a cursor.</param>
    /// <param name="cursor">Where to start.</param>
    /// <param name="pageSize">Reported by <see cref="IScanningCursor.PageSize"/>.</param>
    /// <param name="pageOffset">How far into the first page to begin.</param>
    /// <param name="cancellationToken">The scan-level token.</param>
    /// <param name="fetchSync">Fetches one page synchronously.</param>
    /// <remarks>
    /// <b>Both faces, always.</b> The shipped surface lets a caller cast either way - <c>HashTests.ScanAsync</c>
    /// enumerates <c>HashScan</c> as an <see cref="IAsyncEnumerable{T}"/> <i>and</i> <c>HashScanAsync</c> as
    /// an <see cref="IEnumerable{T}"/> - so which method produced the sequence cannot decide which
    /// interfaces work. Taking both fetchers is what keeps that true without one face blocking on the
    /// other.
    /// </remarks>
    internal RespScanEnumerable(
        PageFetcher fetch,
        SyncPageFetcher fetchSync,
        long cursor,
        int pageSize,
        int pageOffset,
        CancellationToken cancellationToken)
    {
        if (pageOffset < 0) throw new ArgumentOutOfRangeException(nameof(pageOffset));
        _fetch = fetch;
        _fetchSync = fetchSync;
        _initialCursor = cursor;
        _pageSize = pageSize;
        _initialOffset = pageOffset;
        _cancellationToken = cancellationToken;
    }

    long IScanningCursor.Cursor => _active?.Cursor ?? _initialCursor;

    int IScanningCursor.PageSize => _pageSize;

    int IScanningCursor.PageOffset => _active?.PageOffset ?? _initialOffset;

    /// <summary>
    /// Start enumerating, honouring both the token given here and the one given when the scan was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two tokens, and usually the same one.</b> <c>await foreach</c> supplies the enumerator token -
    /// normally via <c>WithCancellation</c> - and the scan method takes one too, because passing it where
    /// you describe the work is what most callers reach for first. When both are real, they have to be
    /// combined.
    /// </para>
    /// <para>
    /// <b>No <see cref="EnumeratorCancellationAttribute"/> here, and it would be a no-op if there were</b>
    /// (CS8424). That attribute exists to thread the enumerator token into a <i>compiler-generated</i>
    /// async iterator; this enumerable hands out a hand-written enumerator, because the enumerator has to
    /// implement <see cref="IScanningCursor"/> and a generated state machine cannot. So the token arrives
    /// through the interface method directly, which is the same token <c>WithCancellation</c> passes - the
    /// attribute only ever automated getting it to a place this code already is.
    /// </para>
    /// <para>
    /// <b>Except when they are the same token</b>, which happens the moment anyone writes
    /// <c>ScanAsync(key, cancellationToken: ct).WithCancellation(ct)</c>. Linking a token to itself
    /// allocates a source and registers a callback on <i>each</i> side, so one cancellation walks the
    /// chain twice and the whole apparatus buys nothing. <see cref="CancellationToken"/> compares by its
    /// underlying source, so the check is exact rather than a heuristic - and the same comparison catches
    /// the other free case, both being <see cref="CancellationToken.None"/>.
    /// </para>
    /// </remarks>
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

    /// <summary>The synchronous face, for the shipped <see cref="IEnumerable{T}"/> signatures.</summary>
    public IEnumerator<T> GetEnumerator() => new Enumerator(this, _cancellationToken, null);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator(RespScanEnumerable<T> parent, CancellationToken cancellationToken, CancellationTokenSource? linked)
        : IAsyncEnumerator<T>, IEnumerator<T>, IScanningCursor
    {
        private RespScanPage<T> _page;
        private bool _hasPage, _finished;
        private int _index = -1;

        /// <summary>The cursor that produced the page being read, not the one that comes after it.</summary>
        /// <remarks>
        /// <see cref="IScanningCursor.Cursor"/> is explicit about this: it is the <i>active</i> page, so
        /// that resuming from it re-reads the page in progress rather than skipping it.
        /// </remarks>
        private long _activeCursor = parent._initialCursor, _nextCursor = parent._initialCursor;

        public T Current => _page.Items.Span[_index];

        long IScanningCursor.Cursor => _activeCursor;

        int IScanningCursor.PageSize => parent._pageSize;

        int IScanningCursor.PageOffset => _index < 0 ? 0 : _index;

        public async ValueTask<bool> MoveNextAsync()
        {
            parent._active = this;
            while (true)
            {
                if (TryAdvance(out var more)) return more;

                cancellationToken.ThrowIfCancellationRequested();
                var cursor = _nextCursor;
                Accept(cursor, await parent._fetch(cursor, cancellationToken).ForAwait());
            }
        }

        /// <summary>The same walk, blocking on each page.</summary>
        /// <remarks>
        /// One loop body, two ways of getting a page: the bookkeeping that decides when the scan is over
        /// lives in <c>TryAdvance</c>/<c>Accept</c> and is shared, so the sync and async faces cannot
        /// disagree about it - which is the same reason the sequence is built on the raw page API rather
        /// than beside it.
        /// </remarks>
        public bool MoveNext()
        {
            parent._active = this;
            while (true)
            {
                if (TryAdvance(out var more)) return more;

                cancellationToken.ThrowIfCancellationRequested();
                var cursor = _nextCursor;
                Accept(cursor, parent._fetchSync(cursor));
            }
        }

        /// <summary>Move within the current page; false when another page is needed.</summary>
        private bool TryAdvance(out bool more)
        {
            if (_hasPage && ++_index < _page.Count)
            {
                more = true;
                return true;
            }

            // an empty page does NOT mean the end: only a zero cursor does, and a scan can hand back any
            // number of empty pages on the way through a sparse keyspace
            more = false;
            return _finished;
        }

        private void Accept(long cursor, RespScanPage<T> next)
        {
            if (_hasPage) _page.Dispose();

            _page = next;
            _hasPage = true;
            _activeCursor = cursor;
            _nextCursor = next.Cursor;
            _finished = next.IsComplete;

            // the first page starts at the requested offset, so a resumed scan does not repeat what the
            // caller already saw; every page after it starts at the beginning
            _index = (cursor == parent._initialCursor ? parent._initialOffset : 0) - 1;
        }

        object? IEnumerator.Current => Current;

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

        public ValueTask DisposeAsync()
        {
            if (_hasPage)
            {
                _page.Dispose();
                _hasPage = false;
            }

            linked?.Dispose();
            if (ReferenceEquals(parent._active, this)) parent._active = null;
            return default;
        }
    }
}
