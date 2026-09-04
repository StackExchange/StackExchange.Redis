using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RESPite.Buffers;

/// <summary>
/// Describes a service that can hand out a counted reservation against a payload it owns, allowing a
/// caller to retain that payload without copying it. See <see cref="RefCountedBuffer"/>.
/// </summary>
internal interface IPayloadReservationProvider
{
    /// <summary>
    /// If <paramref name="payload"/> lies inside a buffer owned by this instance, take a counted
    /// reservation against it; the caller must dispose <see cref="PayloadReservation.Owner"/> exactly once.
    /// </summary>
    bool TryReserve(ReadOnlySpan<byte> payload, out PayloadReservation reservation);
}

/// <summary>
/// Describes a service that knows which pool buffers for this reader should come from, so that callers
/// which need to allocate (rather than share) return memory to the same place it was rented from.
/// </summary>
internal interface IBufferPoolProvider
{
    /// <summary>
    /// The pool to rent from; <c>null</c> for <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    MemoryPool<byte>? BufferPool { get; }
}

/// <summary>
/// A counted claim on a region of a buffer owned by someone else. Disposing <see cref="Owner"/> releases
/// the claim; the payload lives at <see cref="Offset"/>/<see cref="Length"/> within the owner's memory.
/// </summary>
internal readonly struct PayloadReservation(IMemoryOwner<byte> owner, int offset, int length)
{
    public IMemoryOwner<byte> Owner { get; } = owner;
    public int Offset { get; } = offset;
    public int Length { get; } = length;
}

/// <summary>
/// A pooled buffer with a reference count, allowing fragments of it to outlive the original owner without
/// being copied. The count starts at one - held by whoever created it - and the buffer returns to its pool
/// when the count reaches zero.
/// </summary>
/// <remarks>
/// This is a <see cref="MemoryManager{T}"/> rather than a plain <see cref="IMemoryOwner{T}"/> for two
/// reasons. Every <c>Memory</c>/<c>Span</c> access routes back through <see cref="GetSpan"/>, so access
/// after the buffer has gone back to the pool throws rather than silently reading somebody else's data;
/// and <see cref="MemoryManager{T}"/> implements <see cref="IDisposable.Dispose"/> explicitly, so the one
/// publicly reachable <c>Dispose</c> unambiguously means "release one reference" - there is no second
/// disposal concept to confuse it with.
/// </remarks>
internal sealed class RefCountedBuffer : MemoryManager<byte>, IPayloadReservationProvider, IBufferPoolProvider
{
    // a byte[] from ArrayPool<byte>.Shared, or an IMemoryOwner<byte> from a custom pool; null once dead
    private object? _buffer;
    private readonly int _length;
    private readonly bool _noReturn;
    private readonly MemoryPool<byte>? _pool;
    private int _refCount = 1;

    private RefCountedBuffer(object buffer, int length, bool noReturn, MemoryPool<byte>? pool)
    {
        _buffer = buffer;
        _length = length;
        _noReturn = noReturn;
        _pool = pool;
    }

    /// <summary>
    /// The pool this buffer came from, so that a caller who has to copy rather than share still rents
    /// from - and returns to - the same place.
    /// </summary>
    public MemoryPool<byte>? BufferPool => _pool;

    /// <summary>
    /// Rent a buffer of at least <paramref name="length"/> bytes, with a reference count of one.
    /// </summary>
    public static RefCountedBuffer Rent(int length, MemoryPool<byte>? pool) => new(
        pool is null ? ArrayPool<byte>.Shared.Rent(length) : pool.Rent(length),
        length,
        noReturn: false,
        pool);

    /// <summary>
    /// Wrap a fixed buffer that must never be returned to a pool, for shared immutable singletons.
    /// </summary>
    public static RefCountedBuffer CreateFixed(byte[] buffer) => new(buffer, buffer.Length, noReturn: true, pool: null);

    /// <summary>
    /// The number of live references; for assertions and tests.
    /// </summary>
    internal int RefCount => Volatile.Read(ref _refCount);

    /// <summary>
    /// Whether this buffer is fixed - shared, never counted down, and never returned to a pool.
    /// </summary>
    public bool IsFixed => _noReturn;

    /// <summary>
    /// Take an additional reference, unless the buffer is already dead.
    /// </summary>
    /// <remarks>
    /// Increment-if-nonzero, not a bare increment: a reservation racing the final release must fail
    /// rather than resurrect a buffer that has already gone back to the pool.
    /// </remarks>
    public bool TryAddRef()
    {
        int count;
        do
        {
            count = Volatile.Read(ref _refCount);
            if (count == 0) return false;
        }
        while (Interlocked.CompareExchange(ref _refCount, count + 1, count) != count);
        return true;
    }

    /// <summary>
    /// Release one reference, returning the buffer to its pool if that was the last.
    /// </summary>
    public void Release()
    {
        // a fixed buffer is shared for the lifetime of the process and is never counted down; letting it
        // reach zero would strand every future user of the singleton sitting on it
        if (_noReturn) return;
        if (Interlocked.Decrement(ref _refCount) != 0) return;

        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is byte[] arr) ArrayPool<byte>.Shared.Return(arr);
        else if (buffer is IMemoryOwner<byte> owner) owner.Dispose();
    }

    public bool TryReserve(ReadOnlySpan<byte> payload, out PayloadReservation reservation)
    {
        if (!payload.IsEmpty && TryGetOffset(payload, out var offset) && TryAddRef())
        {
            reservation = new PayloadReservation(this, offset, payload.Length);
            return true;
        }

        reservation = default;
        return false;
    }

    // is this span a window onto *our* buffer, and if so, where? this is the pattern the BCL itself uses
    // in MemoryExtensions.Overlaps; the unsigned cast makes a negative delta wrap to a huge value, so the
    // single comparison rejects spans that start before us as well as those that end after us.
    private bool TryGetOffset(ReadOnlySpan<byte> payload, out int offset)
    {
        var mine = RawSpan;
        if (!mine.IsEmpty)
        {
            // note: via long/ulong rather than nuint, which is not available on all target frameworks
            var delta = (long)Unsafe.ByteOffset(
                ref MemoryMarshal.GetReference(mine),
                ref MemoryMarshal.GetReference(payload));
            if (unchecked((ulong)delta) + (uint)payload.Length <= (uint)mine.Length)
            {
                offset = (int)delta;
                return true;
            }
        }

        offset = 0;
        return false;
    }

    private Span<byte> RawSpan
    {
        get
        {
            // read into a local: a concurrent release must give a clean throw, not a null-ref
            var buffer = _buffer;
            if (buffer is byte[] arr) return new Span<byte>(arr, 0, _length);
            if (buffer is IMemoryOwner<byte> owner) return owner.Memory.Span.Slice(0, _length);
            return ThrowDisposed();
        }
    }

    public override Span<byte> GetSpan() => RawSpan;

    // base version is CreateMemory(GetSpan().Length); avoid the round-trip
    public override Memory<byte> Memory => CreateMemory(_length);

    /// <summary>
    /// A <see cref="Memory{T}"/> over part of this buffer; access still routes through this instance.
    /// </summary>
    public Memory<byte> Slice(int offset, int length) => CreateMemory(offset, length);

    // keeping this working is what lets Lease<byte>.ArraySegment - and so DecodeString/AsStream - carry
    // on working for a reservation; MemoryMarshal.TryGetArray composes this with the slice offset
    protected override bool TryGetArray(out ArraySegment<byte> segment)
    {
        if (_buffer is byte[] arr)
        {
            segment = new ArraySegment<byte>(arr, 0, _length);
            return true;
        }

        segment = default;
        return false;
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        // per-call GC pin, as BlockBuffer does; note we do not pass ourselves as the IPinnable, so a
        // handle cannot outlive its own disposal into an Unpin against a released buffer
        if (_buffer is byte[] arr)
        {
            var handle = GCHandle.Alloc(arr, GCHandleType.Pinned);
            unsafe
            {
                return new MemoryHandle((byte*)handle.AddrOfPinnedObject() + elementIndex, handle);
            }
        }

        if (_buffer is IMemoryOwner<byte> owner) return owner.Memory.Slice(elementIndex).Pin();
        return ThrowDisposedHandle();
    }

    // only reachable if we handed out a MemoryHandle naming ourselves as IPinnable, which we never do
    public override void Unpin() => throw new NotSupportedException();

    // this is the *only* publicly reachable Dispose on this type (MemoryManager<T> implements
    // IDisposable explicitly), and it means: release one reference
    protected override void Dispose(bool disposing) => Release();

    [DoesNotReturn]
    private static Span<byte> ThrowDisposed() => throw new ObjectDisposedException(nameof(RefCountedBuffer));

    [DoesNotReturn]
    private static MemoryHandle ThrowDisposedHandle() => throw new ObjectDisposedException(nameof(RefCountedBuffer));
}
