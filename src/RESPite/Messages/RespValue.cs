using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using RESPite.Buffers;

namespace RESPite.Messages;

/// <summary>
/// Something that can still hand back the bytes it lent out, or say that it cannot.
/// </summary>
/// <remarks>
/// The seam a <see cref="RespValue"/> holds rather than a raw <see cref="ReadOnlyMemory{T}"/>: an owner
/// that has returned its buffer to the pool throws from here, where a memory would quietly hand back
/// whatever landed there next. Implemented by <c>RefCountedBuffer</c> and by anything else that pools.
/// </remarks>
[Experimental(Experiments.BorrowedValues, UrlFormat = Experiments.UrlFormat)]
public interface IRespBufferOwner
{
    /// <summary>The bytes this owner holds.</summary>
    /// <exception cref="ObjectDisposedException">If the buffer has already gone back.</exception>
    ReadOnlySpan<byte> GetReadOnlySpan();
}

/// <summary>
/// One scalar RESP value, as a window over somebody else's buffer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The storable counterpart to <see cref="RespReader"/>.</b> A reader walks; this is a bookmark - a
/// frozen position that can live in a field, an array or an async state machine, which a
/// <c>ref struct</c> reader cannot.
/// </para>
/// <para>
/// <b>It holds the frame, not the payload.</b> That is what lets a streamed value be represented at all:
/// a chunked scalar's payload exists nowhere as one run, so there is nothing for a payload window to
/// point at. The frame always exists, and it is self-describing.
/// </para>
/// <para>
/// <b>Uncounted, deliberately.</b> A struct copy cannot increment a reference count, so counting per
/// value would be a leak or a double-free waiting to happen. The owner holds the single reference and
/// these are views valid for as long as it is - one disposal on the owner, none on the values inside it.
/// </para>
/// <para>
/// <b>Scalar by construction</b>, enforced at capture by <see cref="RespReader.DemandScalar"/>, which is
/// what makes the accessors total rather than partial. Scalar means the family the reader means by
/// <see cref="RespReader.IsScalar"/> - bulk, simple, integer, double, boolean, big number, verbatim - not
/// one prefix.
/// </para>
/// <para>
/// <b>Two kinds of member, and the prefix says which.</b> <c>As*</c> hands back something you own: free
/// or nearly so, and safe to keep after the buffer has gone. <see cref="Frame"/> and
/// <see cref="TryGetSpan"/> lend you the bytes instead, and those die with the owner.
/// </para>
/// <para>
/// <b>Everything is delegated.</b> Every accessor is a reader pointed at this window, so there is no
/// second copy of the parse rules, the coercions, or - the part most likely to be got wrong - the
/// streamed-scalar chunk walk.
/// </para>
/// </remarks>
[Experimental(Experiments.BorrowedValues, UrlFormat = Experiments.UrlFormat)]
public readonly struct RespValue : IEquatable<RespValue>
{
    /// <summary>
    /// Whoever the bytes belong to: an <see cref="IRespBufferOwner"/>, an <see cref="IMemoryOwner{T}"/>,
    /// a bare array, or <see langword="null"/> for a value with no bytes at all.
    /// </summary>
    private readonly object? _owner;

    private readonly int _start;
    private readonly int _length;

    // "has a value" rather than "is null", so that default(RespValue) is nil rather than a present value
    // with no bytes - a struct's default has to be the absent one, and Null hands back exactly that
    private readonly bool _hasValue;

    private RespValue(object? owner, int start, int length, bool isNull)
    {
        _owner = owner;
        _start = start;
        _length = length;
        _hasValue = !isNull;
    }

    /// <summary>A value that is not there: the server's nil, however it spelled it.</summary>
    /// <remarks>
    /// RESP spells absence three ways - <c>_</c>, <c>$-1</c> and <c>*-1</c> - and they mean the same thing
    /// to a caller, so all three report alike.
    /// </remarks>
    public static RespValue Null => default;

    /// <summary>Whether this is the server's nil rather than a value.</summary>
    public bool IsNull => !_hasValue;

    /// <summary>Whether this is a value at all, as opposed to nil.</summary>
    public bool HasValue => _hasValue;

    /// <summary>How many bytes this value's payload holds, however it arrived.</summary>
    public int Length => IsNull ? 0 : Reader().ScalarLength();

    /// <summary>The bytes of this value's frame, as the owner still holds them.</summary>
    /// <exception cref="ObjectDisposedException">If the owner has already given its buffer back.</exception>
    public ReadOnlySpan<byte> Frame => _owner switch
    {
        null => default,
        IRespBufferOwner owner => owner.GetReadOnlySpan().Slice(_start, _length),
        IMemoryOwner<byte> owner => owner.Memory.Span.Slice(_start, _length),
        _ => new ReadOnlySpan<byte>((byte[])_owner, _start, _length),
    };

    /// <summary>
    /// Move to the next element and capture it, which must be a scalar.
    /// </summary>
    /// <param name="owner">Who the bytes belong to; the captured value is valid for as long as it is.</param>
    /// <param name="reader">The reader, positioned before the element to capture.</param>
    /// <param name="value">The captured value, when this returns <see langword="true"/>.</param>
    /// <exception cref="InvalidOperationException">If the element is not a scalar.</exception>
    /// <remarks>
    /// <para>
    /// The reader knows <i>where</i> but not <i>whose</i>: it works from spans, and a span carries no
    /// origin. So capture takes the owner from the caller and the extent from the reader.
    /// </para>
    /// <para>
    /// <b>The extent needs nothing new.</b> <see cref="RespReader.BytesConsumed"/> sampled either side of
    /// the move is exactly the element's window, because the reader counts to the <i>end</i> of what it
    /// just read. <c>RespValueTests.TheReaderBracketsEachElement</c> pins that, because everything here
    /// rests on it and nothing else would notice if it changed.
    /// </para>
    /// </remarks>
    public static bool TryCaptureNext(object? owner, scoped ref RespReader reader, out RespValue value)
    {
        var start = checked((int)reader.BytesConsumed);
        if (!reader.TryMoveNext())
        {
            value = default;
            return false;
        }

        reader.DemandScalar();
        var end = checked((int)reader.BytesConsumed);
        value = new RespValue(owner, start, end - start, reader.IsNull);
        return true;
    }

    /// <summary>How the server spelled this value.</summary>
    /// <remarks>
    /// <para>
    /// Read from the frame rather than stored, so it costs one header parse and the struct stays the size
    /// it is - the same arrangement as <see cref="Length"/>.
    /// </para>
    /// <para>
    /// What it is <i>for</i>: the accessors coerce, deliberately - <c>:1</c>, <c>#t</c> and <c>+OK</c> all
    /// read as <see langword="true"/> - so the spelling is lost by the time you have a value. Inspection,
    /// diagnostics and anything round-tripping a reply need to know which one arrived.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">If the owner has already given its buffer back.</exception>
    public RespPrefix Prefix => Reader().Prefix;

    /// <summary>Read this value as a 64-bit integer.</summary>
    public long AsInt64() => Reader().ReadInt64();

    /// <summary>Read this value as a 32-bit integer.</summary>
    public int AsInt32() => Reader().ReadInt32();

    /// <summary>Read this value as a double.</summary>
    public double AsDouble() => Reader().ReadDouble();

    /// <summary>Read this value as a boolean.</summary>
    /// <remarks>
    /// The reader's rule: <c>:0</c>/<c>:1</c>, <c>#f</c>/<c>#t</c> and <c>+OK</c> are booleans; a
    /// <i>bulk</i> <c>"1"</c> is not, because no server answers a boolean that way.
    /// </remarks>
    public bool AsBoolean() => Reader().ReadBoolean();

    /// <summary>
    /// The value's payload as one contiguous run, when it is one.
    /// </summary>
    /// <param name="payload">The bytes, when this returns <see langword="true"/>.</param>
    /// <remarks>
    /// <para>
    /// <see langword="false"/> for a streamed value, whose payload arrives in chunks and so exists nowhere
    /// as a single run - use <see cref="CopyTo(Span{byte})"/> for those. That is what the <c>Try</c> is
    /// for, and the only thing it is for.
    /// </para>
    /// <para>
    /// Also <see langword="false"/> for nil. The reader itself says <see langword="true"/> with an empty
    /// span there, which would read a missing value as a present empty one.
    /// </para>
    /// </remarks>
    public bool TryGetSpan(out ReadOnlySpan<byte> payload)
    {
        var reader = Reader();
        if (reader.IsNull || reader.IsStreaming)
        {
            payload = default;
            return false;
        }

        return reader.TryGetSpan(out payload);
    }

    /// <summary>Copy this value's payload out, joining the chunks of a streamed value.</summary>
    /// <param name="target">Where to write; <see cref="Length"/> says how much room to leave.</param>
    /// <returns>How many bytes were written.</returns>
    public int CopyTo(scoped Span<byte> target) => Reader().CopyTo(target);

    /// <summary>This value as text, or <see langword="null"/> if it is nil.</summary>
    /// <param name="value">The value to convert.</param>
    /// <remarks>
    /// A conversion rather than an <c>AsString()</c> method: it is the one member here that allocates
    /// every single time, so it is not an <c>As*</c>, and <see cref="ToString"/> cannot be it because that
    /// may not return null.
    /// </remarks>
    public static explicit operator string?(RespValue value)
        => value.IsNull ? null : value.Reader().ReadString();

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>By payload bytes</b>, which normalises the spelling for free: the payload of <c>:1</c> and of
    /// <c>$1\r\n1</c> is the same single digit, so the two compare equal without a rule of their own.
    /// </para>
    /// <para>
    /// It does <i>not</i> reach across prefix families the way <see langword="decimal"/>-style coercion
    /// would: <c>,1.0</c> and <c>:1</c> are different payloads and so different values. That is the
    /// honest answer for a wire-level type - the same bytes mean the same value, and nothing else does.
    /// </para>
    /// </remarks>
    public bool Equals(RespValue other)
    {
        if (IsNull || other.IsNull) return IsNull && other.IsNull;

        if (TryGetSpan(out var mine) && other.TryGetSpan(out var theirs))
        {
            return mine.SequenceEqual(theirs);
        }

        // at least one is streamed, so at least one has to be assembled before they can be compared
        var length = Length;
        if (length != other.Length) return false;

        byte[]? minePooled = null, theirsPooled = null;
        try
        {
            var mineBuffer = Linearize(in this, length, ref minePooled);
            var theirsBuffer = Linearize(in other, length, ref theirsPooled);
            return mineBuffer.SequenceEqual(theirsBuffer);
        }
        finally
        {
            if (minePooled is not null) ArrayPool<byte>.Shared.Return(minePooled);
            if (theirsPooled is not null) ArrayPool<byte>.Shared.Return(theirsPooled);
        }

        static ReadOnlySpan<byte> Linearize(in RespValue value, int length, ref byte[]? pooled)
        {
            if (value.TryGetSpan(out var span)) return span;

            pooled = ArrayPool<byte>.Shared.Rent(length);
            var written = value.CopyTo(pooled);
            return new ReadOnlySpan<byte>(pooled, 0, written);
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RespValue other && Equals(other);

    /// <inheritdoc/>
    /// <remarks>Over the payload, so that it agrees with <see cref="Equals(RespValue)"/>.</remarks>
    public override int GetHashCode()
    {
        if (IsNull) return 0;
        if (!TryGetSpan(out var payload)) return Length; // streamed: cheap and consistent, if coarse

#if NET
        HashCode hash = default;
        hash.AddBytes(payload);
        return hash.ToHashCode();
#else
        var result = 17;
        foreach (var b in payload)
        {
            result = (result * 31) + b;
        }

        return result;
#endif
    }

    /// <inheritdoc/>
    /// <remarks>
    /// For humans and debuggers; nil reads as <c>(nil)</c> rather than null.
    /// <para>
    /// <b>Throws once the owner has handed its buffer back</b>, like every other accessor here - pinned by
    /// <c>RespValueTests.EveryAccessorDiesWithTheLease</c>, which sweeps all thirteen routes to the bytes.
    /// The uniformity is the point: "everything fails after disposal" needs no exceptions remembered.
    /// The counter-argument is real but has not been taken - <c>ToString</c> is what a debugger calls
    /// implicitly, so a released value throws from inside a watch window. Returning a marker instead would
    /// hand back no data and so would not weaken the safety property; it would only weaken the rule.
    /// </para>
    /// </remarks>
    public override string ToString() => (string?)this ?? "(nil)";

    private RespReader Reader()
    {
        // default(RespValue) is nil AND has no bytes, which is not the same as a captured nil: the server
        // said "$-1" there, and that is a frame to read. Here there is nothing, so say so rather than
        // running off the end of an empty span.
        if (_owner is null) ThrowNoContent();

        var reader = new RespReader(Frame);
        reader.MoveNext();
        return reader;

        [DoesNotReturn]
        static void ThrowNoContent()
            => throw new InvalidOperationException("This value has no content; it is the default RespValue.");
    }
}
