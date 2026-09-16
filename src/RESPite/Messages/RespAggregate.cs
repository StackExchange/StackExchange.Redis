using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RESPite.Messages;

/// <summary>
/// One aggregate RESP value, as a window over somebody else's buffer, whose children are projected on
/// demand rather than materialised.
/// </summary>
/// <typeparam name="T">What each child is projected to.</typeparam>
/// <remarks>
/// <para>
/// <b>The aggregate counterpart to <see cref="RespValue"/></b>, and it holds the frame for the same reason:
/// the frame is self-describing, so a window over it can be re-read from scratch at any time without
/// carrying any parse state. <see cref="RespValue"/> stays scalar-by-construction; this is a separate type
/// rather than a relaxation of it, so neither has to test what it is.
/// </para>
/// <para>
/// <b>Uncounted, like <see cref="RespValue"/>.</b> A struct copy cannot increment a reference count, so the
/// owner holds the single reference and these are views valid for exactly as long as it is. One disposal on
/// the owner, none on the values inside it.
/// </para>
/// <para>
/// <b>Enumeration only - there is deliberately no indexer.</b> RESP is forward-only with variable-length
/// frames and no offset table, so reaching child <c>i</c> means walking the <c>i</c> before it; an indexer
/// would be O(i), and a <c>for</c> loop over one would be O(n^2) on something that looks exactly like a
/// list. The shape of an API is a promise, so the shape has to be one that can be kept. A caller who needs
/// random access materialises, explicitly.
/// </para>
/// <para>
/// <b>The capture needs nothing new from the reader.</b> <see cref="RespReader.BytesConsumed"/> sampled
/// either side of <c>MoveNext</c> + <see cref="RespReader.SkipChildren"/> is exactly the sub-tree's window,
/// which is the same guarantee <see cref="RespValue"/>'s capture rests on - and <c>SkipChildren</c> is
/// already non-recursive, so depth costs nothing here.
/// </para>
/// </remarks>
[Experimental(Experiments.BorrowedValues, UrlFormat = Experiments.UrlFormat)]
public readonly struct RespAggregate<T>
{
    private readonly object? _owner;
    private readonly int _start;
    private readonly int _length;
    private readonly int _count;
    private readonly RespReader.Projection<object?, T>? _projection;

    private RespAggregate(object? owner, int start, int length, int count, RespReader.Projection<object?, T> projection)
    {
        _owner = owner;
        _start = start;
        _length = length;
        _count = count;
        _projection = projection;
    }

    /// <summary>An aggregate with no children; what a nil aggregate reads as.</summary>
    public static RespAggregate<T> Empty => default;

    /// <summary>
    /// How many children this aggregate has.
    /// </summary>
    /// <remarks>
    /// Read from the header when the window was captured, so this is free. Only a <i>streamed</i> aggregate
    /// has no count in its header, and no server sends one in practice.
    /// </remarks>
    public int Count => _count;

    /// <summary>How the server spelled this aggregate.</summary>
    /// <remarks>
    /// <para>
    /// Read from the frame rather than stored, so it costs one header parse.
    /// </para>
    /// <para>
    /// <b>It is what tells a map from an array</b>, which matters because <see cref="Count"/> counts
    /// <i>children</i>: a map of two pairs is <c>%2</c> on the wire and reports <b>4</b>, since that is how
    /// many elements the walk yields. Without the prefix a caller cannot tell that from an array of four.
    /// Sets (<c>~</c>) and pushes (<c>&gt;</c>) are likewise only distinguishable here.
    /// </para>
    /// <para>
    /// <see cref="RespPrefix.None"/> for an aggregate with no bytes - the default, and a captured nil,
    /// which collapses to <see cref="Empty"/>.
    /// </para>
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
    /// Move to the next element and capture it, which must be an aggregate.
    /// </summary>
    /// <param name="owner">Who the bytes belong to; the captured window is valid for as long as it is.</param>
    /// <param name="reader">The reader, positioned before the element to capture.</param>
    /// <param name="projection">How to read one child.</param>
    /// <param name="value">The captured aggregate, when this returns <see langword="true"/>.</param>
    /// <remarks>
    /// As <see cref="RespValue.TryCaptureNext"/>: the reader knows <i>where</i> but not <i>whose</i>, so the
    /// owner comes from the caller and the extent from the reader. The extra step over the scalar case is
    /// <see cref="RespReader.SkipChildren"/>, which is what makes the window span the whole sub-tree rather
    /// than just its header.
    /// </remarks>
    public static bool TryCaptureNext(
        object? owner,
        scoped ref RespReader reader,
        RespReader.Projection<object?, T> projection,
        out RespAggregate<T> value)
    {
        if (projection is null) throw new ArgumentNullException(nameof(projection));

        var start = checked((int)reader.BytesConsumed);
        if (!reader.TryMoveNext())
        {
            value = default;
            return false;
        }

        // a nil aggregate reads as empty: every caller of an aggregate wants to iterate it
        if (reader.IsNull)
        {
            reader.SkipChildren();
            value = Empty;
            return true;
        }

        reader.DemandAggregate();
        var count = reader.AggregateLength();
        reader.SkipChildren();
        var end = checked((int)reader.BytesConsumed);

        value = new RespAggregate<T>(owner, start, end - start, count, projection);
        return true;
    }

    /// <summary>Walk the children, projecting each.</summary>
    public Enumerator GetEnumerator() => new(Frame, _projection, _owner);

    /// <summary>Materialise the children into an array.</summary>
    /// <remarks>
    /// <b><c>To</c>, not <c>As</c>:</b> this allocates and copies, where <c>As*</c> on these types means
    /// cheap-and-yours. The name is the price. What it buys is independence - the array outlives the owner,
    /// where the children do not.
    /// </remarks>
    public T[] ToArray()
    {
        var count = Count;
        if (count == 0) return [];

        // no guard for "fewer children than the header promised": RESP framing makes that unreachable,
        // because the count IS how many the reader reads - a truncated frame throws out of the walk long
        // before it gets here. A check would be dead code that reads as load-bearing.
        var result = new T[count];
        var index = 0;
        foreach (var child in this)
        {
            result[index++] = child;
        }

        Debug.Assert(index == count, "the walk disagreed with the header");
        return result;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Cannot throw, and needs no guard to say so: <see cref="Count"/> was read from the header when the
    /// window was captured, so this touches no bytes and does not care whether the owner still holds them.
    /// <see cref="RespValue.ToString"/> does read bytes, and catches for that reason.
    /// </remarks>
    public override string ToString()
        => _owner is null ? "(nil)" : $"({Count} item{(Count == 1 ? "" : "s")})";

    /// <summary>Walks an aggregate's children, projecting each as it goes.</summary>
    /// <remarks>
    /// A <c>ref struct</c>, so it cannot be stored in a field or cross an <c>await</c> - which is fine for
    /// <c>foreach</c> and is the same reason <see cref="RespValue"/> exists for the cases that need to be
    /// stored.
    /// </remarks>
    public ref struct Enumerator
    {
        private RespReader.AggregateEnumerator _children;
        private readonly RespReader.Projection<object?, T>? _projection;
        private object? _owner;
        private readonly bool _empty;

        internal Enumerator(ReadOnlySpan<byte> frame, RespReader.Projection<object?, T>? projection, object? owner)
        {
            _projection = projection;
            _owner = owner;
            Current = default!;

            if (frame.IsEmpty || projection is null)
            {
                _empty = true;
                _children = default;
                return;
            }

            _empty = false;
            var reader = new RespReader(frame);
            reader.MoveNext(); // onto the aggregate header itself
            _children = reader.AggregateChildren();
        }

        /// <summary>The child most recently walked to.</summary>
        public T Current { get; private set; }

        /// <summary>Walk to the next child.</summary>
        public bool MoveNext()
        {
            if (_empty || !_children.MoveNext()) return false;

            Current = _projection!(ref _owner, ref _children.Value);
            return true;
        }
    }
}
