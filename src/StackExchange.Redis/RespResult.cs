using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using RESPite;
using RESPite.Buffers;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// Represents a single RESP reply as a leased, undecoded byte sequence, together with its top-level
/// <see cref="RespPrefix"/>. This is a low-allocation alternative to <see cref="RedisResult"/> for callers
/// who want direct access to a reply - including arbitrary trees, not just a single scalar payload -
/// without the cost of materializing the entire reply into <see cref="RedisResult"/>/<see cref="RedisValue"/>
/// instances up-front. Dispose to return the underlying buffer to the pool.
/// </summary>
/// <remarks>
/// A RESP null is never represented as a <c>null</c> <see cref="RespResult"/> reference; check
/// <see cref="IsNull"/> instead. This preserves which of the three null encodings (a RESP2 null bulk
/// string, a RESP2 null array, or the unified RESP3 null) was actually on the wire, and leaves room for
/// future attribute metadata on a null reply.
/// </remarks>
public sealed class RespResult : IDisposable
{
    private static readonly RespResult NullBulkStringReply = CreateNullSingleton(RespPrefix.BulkString, "$-1\r\n"u8);
    private static readonly RespResult NullArrayReply = CreateNullSingleton(RespPrefix.Array, "*-1\r\n"u8);

    /// <summary>
    /// The shared singleton representing a unified RESP3 null (<c>_\r\n</c>); also used as the default
    /// result for a fire-and-forget request, where no reply is ever observed.
    /// </summary>
    internal static readonly RespResult NullReply = CreateNullSingleton(RespPrefix.Null, "_\r\n"u8);

    private static RespResult CreateNullSingleton(RespPrefix prefix, ReadOnlySpan<byte> raw) =>
        new(prefix, isNull: true, RefCountedBuffer.CreateFixed(raw.ToArray()));

    // reference-counted, so that a Lease taken from this reply (see RespReaderExtensions.ReadLease) can
    // point back into this buffer rather than copying out of it; the buffer returns to its pool when this
    // result and every lease taken from it have been disposed.
    private RefCountedBuffer? _buffer;

    // where this reply sits inside _buffer. Zero/whole-buffer when we rented it for ourselves; a genuine
    // window when we are SHARING somebody else's buffer - a cache entry, say - rather than copying out of
    // it. See Share below.
    private readonly int _offset;
    private readonly int _length;

    private RespResult(RespPrefix prefix, bool isNull, RefCountedBuffer buffer)
        : this(prefix, isNull, buffer, 0, buffer.GetSpan().Length)
    {
    }

    private RespResult(RespPrefix prefix, bool isNull, RefCountedBuffer buffer, int offset, int length)
    {
        Prefix = prefix;
        IsNull = isNull;
        _buffer = buffer;
        _offset = offset;
        _length = length;
    }

    /// <summary>
    /// Wrap an existing buffer <b>without copying</b>, taking a reference to it.
    /// </summary>
    /// <param name="buffer">The buffer holding the raw frame.</param>
    /// <param name="offset">Where the frame starts within it.</param>
    /// <param name="length">How long the frame is.</param>
    /// <returns>The reply, or <c>null</c> if the buffer had already gone - treat that as a miss.</returns>
    /// <remarks>
    /// <para>
    /// The zero-copy counterpart of <see cref="Capture(ReadOnlySpan{byte}, MemoryPool{byte}?)"/>, and safe
    /// for a buffer that is still owned elsewhere for a reason no mutable type can offer: everything this
    /// type exposes is a <c>RespReader</c>, so nothing can write through it. Sharing a <i>cache entry</i>
    /// also pins nothing extra - the cache holds that buffer for the entry's lifetime regardless. See
    /// design notes 6.16.
    /// </para>
    /// <para>
    /// The reference is taken here and given back by <see cref="Dispose"/>, so the reply outlives whatever
    /// the pipeline does with its own reference the moment parsing returns.
    /// </para>
    /// </remarks>
    internal static RespResult? Share(RefCountedBuffer buffer, int offset, int length)
    {
        var probe = new RespReader(buffer.GetSpan().Slice(offset, length));
        probe.MovePastBof();

        // increment-if-nonzero: losing this race means the buffer is already going back to its pool, which
        // the caller must treat as a miss rather than resurrecting it
        if (!buffer.TryAddRef()) return null;

        return new RespResult(probe.Prefix, probe.IsNull, buffer, offset, length);
    }

    /// <summary>
    /// Capture a complete, already-framed reply from a span.
    /// </summary>
    /// <param name="frame">The raw reply, header bytes included.</param>
    /// <param name="pool">The pool to rent the copy from.</param>
    /// <remarks>
    /// The entry point for the interpolated surface, whose replies arrive as a finished frame rather than
    /// through a connection's reader. It <b>copies</b>, exactly as the connection path does - and for the
    /// interpolated path that copy is not yet avoidable: sharing needs the reader to know which buffer the
    /// bytes live in, and a <see cref="ReadOnlySpan{T}"/> does not carry that. See design notes 6.16.
    /// </remarks>
    internal static RespResult Capture(ReadOnlySpan<byte> frame, MemoryPool<byte>? pool = null)
    {
        var probe = new RespReader(frame);
        probe.MovePastBof();

        var buffer = RefCountedBuffer.Rent(frame.Length, pool);
        var result = new RespResult(probe.Prefix, probe.IsNull, buffer);
        frame.CopyTo(result.RawSpan);
        return result;
    }

    internal static RespResult Capture(RespPrefix prefix, bool isNull, ref RespReader reader, int length, MemoryPool<byte>? pool)
    {
        if (isNull)
        {
            return prefix switch
            {
                RespPrefix.BulkString => NullBulkStringReply,
                RespPrefix.Array => NullArrayReply,
                _ => NullReply,
            };
        }

        Debug.Assert(length >= 0, "length must be non-negative");
        var buffer = RefCountedBuffer.Rent(length, pool);
        var result = new RespResult(prefix, isNull: false, buffer);
        var copied = reader.CopyRawTo(result.RawSpan);
        Debug.Assert(copied == length, "raw frame capture length mismatch");
        return result;
    }

    /// <summary>
    /// The RESP prefix of the top-level element of this reply.
    /// </summary>
    public RespPrefix Prefix { get; }

    /// <summary>
    /// Whether this reply is a RESP null (of any of the three encodings).
    /// </summary>
    public bool IsNull { get; }

    private Span<byte> RawSpan => (_buffer ?? ThrowDisposed()).GetSpan().Slice(_offset, _length);

    [DoesNotReturn]
    private static RefCountedBuffer ThrowDisposed() => throw new ObjectDisposedException(nameof(RespResult));

    /// <summary>
    /// Obtains a reader over the contents of this reply, positioned at the top-level element; this
    /// supports full read access, including nested trees.
    /// </summary>
    public RespReader Read()
    {
        var buffer = _buffer ?? ThrowDisposed();
        var reader = new RespReader(buffer.GetSpan().Slice(_offset, _length), buffer);
        reader.MoveNext();
        return reader;
    }

    /// <summary>
    /// Obtains a reader over the contents of a reply that is required to be scalar (i.e. a single value),
    /// positioned ready to read the value.
    /// </summary>
    public RespReader ReadScalar()
    {
        var buffer = _buffer ?? ThrowDisposed();
        var reader = new RespReader(buffer.GetSpan().Slice(_offset, _length), buffer);
        reader.MoveNextScalar();
        return reader;
    }

    /// <summary>
    /// Release all resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        // one of the shared null singletons: never counted down, and the field must stay put - these
        // instances are handed out again and again for the lifetime of the process
        if (_buffer is { IsFixed: true }) return;

        // exchange-to-null makes this once-only, however many times a caller disposes us; any leases
        // still holding a reservation keep the buffer alive until they are disposed in turn
        Interlocked.Exchange(ref _buffer, null)?.Release();
    }

    /// <summary>
    /// The number of live references to the underlying buffer; for tests.
    /// </summary>
    internal int RefCount => _buffer?.RefCount ?? 0;
}
