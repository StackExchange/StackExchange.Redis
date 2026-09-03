using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using RESPite;
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
        new(prefix, isNull: true, raw.ToArray(), raw.Length, noReturn: true);

    // the high bit of _length flags a buffer that must never be returned to a pool (the shared null
    // singletons above, sitting on a fixed byte[]); Length masks it back off. Real captures never set
    // it themselves - the length always comes from a checked cast of a non-negative byte count.
    private const int NoReturnFlag = 1 << 31;

    // either a byte[] (from ArrayPool<byte>.Shared, or one of the fixed null singletons) or an
    // IMemoryOwner<byte> (from a custom pool); sitting directly on this - rather than wrapping a
    // Lease<byte> - avoids an extra allocation.
    private object? _buffer;
    private readonly int _length;

    private RespResult(RespPrefix prefix, bool isNull, object buffer, int length, bool noReturn = false)
    {
        // length must not already occupy the high bit reserved for NoReturnFlag, or Length/NoReturn below
        // would misread the buffer's real size and disposal-eligibility, respectively.
        Debug.Assert(length >= 0, "length must be non-negative");
        Prefix = prefix;
        IsNull = isNull;
        _buffer = buffer;
        _length = noReturn ? length | NoReturnFlag : length;
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

        object buffer = pool is null ? ArrayPool<byte>.Shared.Rent(length) : pool.Rent(length);
        var result = new RespResult(prefix, isNull: false, buffer, length);
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

    private int BufferLength => _length & ~NoReturnFlag;

    private bool NoReturn => (_length & NoReturnFlag) != 0;

    private Span<byte> RawSpan
    {
        get
        {
            var buffer = _buffer;
            if (buffer is byte[] arr) return new Span<byte>(arr, 0, BufferLength);
            if (buffer is IMemoryOwner<byte> owner) return owner.Memory.Span.Slice(0, BufferLength);
            return ThrowDisposed();
        }
    }

    [DoesNotReturn]
    private static Span<byte> ThrowDisposed() => throw new ObjectDisposedException(nameof(RespResult));

    /// <summary>
    /// Obtains a reader over the contents of this reply, positioned at the top-level element; this
    /// supports full read access, including nested trees.
    /// </summary>
    public RespReader Read()
    {
        var reader = new RespReader(RawSpan);
        reader.MoveNext();
        return reader;
    }

    /// <summary>
    /// Obtains a reader over the contents of a reply that is required to be scalar (i.e. a single value),
    /// positioned ready to read the value.
    /// </summary>
    public RespReader ReadScalar()
    {
        var reader = new RespReader(RawSpan);
        reader.MoveNextScalar();
        return reader;
    }

    /// <summary>
    /// Release all resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (NoReturn) return; // one of the shared null singletons; never disposed
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is byte[] arr) ArrayPool<byte>.Shared.Return(arr);
        else if (buffer is IMemoryOwner<byte> owner) owner.Dispose();
    }
}
