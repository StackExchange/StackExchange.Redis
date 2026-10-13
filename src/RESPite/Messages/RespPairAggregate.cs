using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RESPite.Messages;

/// <summary>
/// One pair-shaped aggregate RESP value - a hash, a stream entry's fields, a config block - as a window
/// over somebody else's buffer, whose pairs are projected on demand rather than materialised.
/// </summary>
/// <typeparam name="T">What each pair is projected to.</typeparam>
/// <remarks>
/// <para>
/// <b>A separate type from <see cref="RespAggregate{T}"/>, because it is a different walk.</b> A pair is
/// two <i>siblings</i>, and the enumerator hands a projection a reader trimmed to a single sub-tree - so a
/// projection over <see cref="RespAggregate{T}"/> structurally cannot reach the second half. Adding a
/// stride to that type would not be enough either, because of the jaggedness below.
/// </para>
/// <para>
/// <b>Two wire shapes, one type.</b> A pair-shaped reply is either <i>interleaved</i>
/// (<c>[k,v,k,v]</c>) or <i>jagged</i> (<c>[[k,v],[k,v]]</c>). Some replies changed shape when RESP3
/// arrived, but jaggedness is expressible in RESP2 as well, so the decision is made from the
/// <b>content</b> - <see cref="RespReader.IsAllJaggedPairs"/> - and not from the protocol version. Whether
/// jagged is permitted at all is policy, and stays with the caller that captures the window.
/// </para>
/// <para>
/// <b>The shape is decided once, at capture, and stored.</b> Detection is O(n) in the children, so
/// re-deciding per pair would make a walk quadratic. This is the one piece of parse state these windows
/// carry that cannot be re-read cheaply.
/// </para>
/// <para>
/// <b><see cref="Count"/> counts pairs</b>, which is the whole point of the type: an interleaved reply of
/// six elements is three pairs. That is not in tension with <see cref="RespAggregate{T}.Count"/> counting
/// <i>children</i> - there, halving a map would have made RESP2 and RESP3 disagree about the same reply;
/// here, both wire shapes agree on the number of pairs, which is why they can share one type at all.
/// </para>
/// </remarks>
[Experimental(Experiments.BorrowedValues, UrlFormat = Experiments.UrlFormat)]
public readonly struct RespPairAggregate<T>
{
    private readonly object? _owner;
    private readonly int _start;
    private readonly int _length;
    private readonly int _count;
    private readonly bool _jagged;
    private readonly RespReader.PairProjection<object?, T>? _projection;

    private RespPairAggregate(object? owner, int start, int length, int count, bool jagged, RespReader.PairProjection<object?, T> projection)
    {
        _owner = owner;
        _start = start;
        _length = length;
        _count = count;
        _jagged = jagged;
        _projection = projection;
    }

    /// <summary>An aggregate with no pairs; what a nil aggregate reads as.</summary>
    public static RespPairAggregate<T> Empty => default;

    /// <summary>How many <b>pairs</b> this aggregate has.</summary>
    /// <remarks>
    /// Read from the header when the window was captured, so this is free. An interleaved aggregate with
    /// an odd number of children reports the number of <i>whole</i> pairs; the trailing element is not a
    /// pair and is not walked.
    /// </remarks>
    public int Count => _count;

    /// <summary>How the server spelled this aggregate.</summary>
    /// <remarks>
    /// Read from the frame rather than stored. It is what tells a map (<c>%</c>) from an array, which for
    /// a pair-shaped reply is the difference between the server saying "these are pairs" and this library
    /// deciding it from the content.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">If the owner has already given its buffer back.</exception>
    public RespPrefix Prefix
    {
        get
        {
            if (_owner is null) return RespPrefix.None;

            var reader = new RespReader(Frame);
            reader.MoveNext();
            return reader.Prefix;
        }
    }

    /// <summary>Whether the pairs are nested aggregates rather than interleaved siblings.</summary>
    /// <remarks>
    /// Exposed because it is the one thing a caller cannot re-derive without paying for the walk again,
    /// and because a caller reconciling this reply with a server's documented shape may need to know.
    /// </remarks>
    public bool IsJagged => _jagged;

    /// <summary>The bytes of this aggregate's frame, as the owner still holds them.</summary>
    /// <exception cref="ObjectDisposedException">If the owner has already given its buffer back.</exception>
    private ReadOnlySpan<byte> Frame => _owner switch
    {
        null => default,
        IRespBufferOwner owner => owner.GetReadOnlySpan().Slice(_start, _length),
        IMemoryOwner<byte> owner => owner.Memory.Span.Slice(_start, _length),
        _ => new ReadOnlySpan<byte>((byte[])_owner, _start, _length),
    };

    /// <summary>
    /// Move to the next element and capture it as a pair-shaped aggregate.
    /// </summary>
    /// <param name="owner">Who the bytes belong to; the captured window is valid for as long as it is.</param>
    /// <param name="reader">The reader, positioned before the element to capture.</param>
    /// <param name="projection">
    /// How to read one pair; handed two readers, each positioned <b>before</b> its element, as this method
    /// is - so a projection can capture windows as well as read values.
    /// </param>
    /// <param name="allowJagged">
    /// Whether a jagged reply may be read as pairs. Policy, and therefore the caller's: this type reports
    /// what the bytes are, but only the caller knows whether the command in question is allowed to answer
    /// that way.
    /// </param>
    /// <param name="value">The captured aggregate, when this returns <see langword="true"/>.</param>
    public static bool TryCaptureNext(
        object? owner,
        scoped ref RespReader reader,
        RespReader.PairProjection<object?, T> projection,
        bool allowJagged,
        out RespPairAggregate<T> value)
    {
        if (projection is null) throw new ArgumentNullException(nameof(projection));

        var start = checked((int)reader.BytesConsumed);
        if (!reader.TryMoveNext())
        {
            value = default;
            return false;
        }

        // a nil aggregate reads as empty: every caller of a pair reply wants to iterate it
        if (reader.IsNull)
        {
            reader.SkipChildren();
            value = Empty;
            return true;
        }

        reader.DemandAggregate();
        var children = reader.AggregateLength();

        // decided here, once, from the content - and before SkipChildren, because the test walks the
        // children of the element the reader is currently on
        var jagged = allowJagged && reader.IsAllJaggedPairs();
        var count = jagged ? children : children >> 1;

        reader.SkipChildren();
        var end = checked((int)reader.BytesConsumed);

        value = new RespPairAggregate<T>(owner, start, end - start, count, jagged, projection);
        return true;
    }

    /// <summary>Walk the pairs, projecting each.</summary>
    /// <remarks>
    /// The window's start goes with the frame so that anything captured during the walk records an offset
    /// into the owner's whole buffer rather than into this slice; see the slice-aware
    /// <see cref="RespReader"/> constructor.
    /// </remarks>
    public Enumerator GetEnumerator() => new(Frame, _projection, _owner, _start, _jagged);

    /// <summary>Materialise the pairs into an array.</summary>
    /// <remarks>
    /// <b><c>To</c>, not <c>As</c>:</b> this allocates and copies. What it buys is independence - the
    /// array outlives the owner, where the pairs do not.
    /// </remarks>
    public T[] ToArray()
    {
        var count = Count;
        if (count == 0) return [];

        // as RespAggregate<T>.ToArray: no short-fill guard, because a frame whose children disagree with
        // its own header throws out of the walk rather than arriving here
        var result = new T[count];
        var index = 0;
        foreach (var pair in this)
        {
            result[index++] = pair;
        }

        Debug.Assert(index == count, "the walk disagreed with the header");
        return result;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Cannot throw: <see cref="Count"/> was read from the header at capture, so this touches no bytes and
    /// does not care whether the owner still holds them.
    /// </remarks>
    public override string ToString()
        => _owner is null ? "(nil)" : $"({Count} pair{(Count == 1 ? "" : "s")})";

    /// <summary>Walks an aggregate's pairs, projecting each as it goes.</summary>
    public ref struct Enumerator
    {
        private RespReader.AggregateEnumerator _children;
        private readonly RespReader.PairProjection<object?, T>? _projection;
        private object? _owner;
        private readonly bool _empty;
        private readonly bool _jagged;

        internal Enumerator(ReadOnlySpan<byte> frame, RespReader.PairProjection<object?, T>? projection, object? owner, int start, bool jagged)
        {
            _projection = projection;
            _owner = owner;
            _jagged = jagged;
            Current = default!;

            if (frame.IsEmpty || projection is null)
            {
                _empty = true;
                _children = default;
                return;
            }

            _empty = false;
            var reader = new RespReader(frame, services: null, positionBase: start);
            reader.MoveNext(); // onto the aggregate header itself
            _children = reader.AggregateChildren();
        }

        /// <summary>The pair most recently walked to.</summary>
        public T Current { get; private set; }

        /// <summary>Walk to the next pair.</summary>
        /// <remarks>
        /// Each half reaches the projection positioned <i>before</i> its element - see
        /// <see cref="RespAggregate{T}.Enumerator.MoveNext"/> for why that is the position a projection
        /// needs. The jagged branch still steps <i>onto</i> the enclosing pair aggregate, because reaching
        /// its children means reading its header first.
        /// </remarks>
        public bool MoveNext()
        {
            if (_empty) return false;

            if (_jagged)
            {
                // onto the pair aggregate itself, so its children can be walked...
                if (!_children.MoveNext()) return false;
                var pair = _children.Value.AggregateChildren();

                // ...but each half is handed over positioned before itself
                if (!pair.MoveNextRaw()) return false;
                var first = pair.Value;
                if (!pair.MoveNextRaw()) return false;
                var second = pair.Value;
                Current = _projection!(ref _owner, ref first, ref second);
            }
            else
            {
                // a copy, because the next move overwrites Value - which is exactly how the eager pair
                // parser reads an interleaved run
                if (!_children.MoveNextRaw()) return false;
                var first = _children.Value;

                // an odd trailing element is not a pair: stop, rather than inventing a half of one
                if (!_children.MoveNextRaw()) return false;
                var second = _children.Value;
                Current = _projection!(ref _owner, ref first, ref second);
            }

            return true;
        }
    }
}
