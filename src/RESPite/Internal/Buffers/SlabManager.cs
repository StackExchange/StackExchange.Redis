using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;

namespace RESPite.Internal.Buffers;

internal sealed class SlabManager<T> : IDisposable
{
    private readonly int _chunkSize, _slabSize;
    private readonly bool _dedicated;
    private Slab? _slab;

    // reads will use a dedicated slab manager; writes are much more transient, so: retain a small
    // number of slab managers, and hand them out based on the thread-id
    // dedicated slab managers can be more aggressive - probably "read", so be generous with buffers;
    // for default (thread-ambient) managers, assume "write", which can be quite small
    private static readonly SlabManager<T>?[] s_Ambient = new SlabManager<T>[Environment.ProcessorCount];
    public static SlabManager<T> Ambient => s_Ambient[(uint)Environment.CurrentManagedThreadId % s_Ambient.Length] ??= new(false, 16 * 1024, 128);

    public SlabManager(int slabSize = 64 * 1024, int chunkSize = 4096) : this(true, slabSize, chunkSize) { }
    private SlabManager(bool dedicated, int slabSize, int chunkSize)
    {
        _slabSize = Math.Max(slabSize, 1024);
        _chunkSize = Math.Max(chunkSize, 16);
        if (chunkSize > slabSize || slabSize % chunkSize != 0) Throw();
        _dedicated = dedicated;
        _slab = new(SlabSize);

        static void Throw() => throw new ArgumentOutOfRangeException(nameof(chunkSize));
    }

    private int SlabSize => _slabSize;
    internal int ChunkSize => _chunkSize;

    public void Dispose()
    {
        // put things back (this only makes sense for scoped instances being correctly unrolled)
        if (_dedicated)
        {
            Interlocked.Exchange(ref _slab, null)?.Dispose();
        }
    }

    public IDisposable GetChunk(out Memory<T> chunk)
    {
        // optimize for happy path
        if (Volatile.Read(ref _slab) is { } slab && slab.TryTake(ChunkSize, out chunk))
        {
            return slab;
        }
        return GetChunkSlow(out chunk);
    }

    public bool TryExpandChunk(IDisposable owner, ref Memory<T> chunk)
        => owner is Slab slab && slab.TryExpand(ChunkSize, ref chunk);

    private IDisposable GetChunkSlow(out Memory<T> chunk)
    {
        Slab? newSlab = null;
        do
        {
            var oldSlab = Volatile.Read(ref _slab);
            if (oldSlab is null) ThrowDisposed();

            if (oldSlab.TryTake(ChunkSize, out chunk))
            {
                newSlab?.Dispose(); // allocated but never swapped in due to CEX fail
                return oldSlab;
            }

            // otherwise, we need a new slab
            newSlab ??= new(SlabSize);

            if (ReferenceEquals(Interlocked.CompareExchange(ref _slab, newSlab, oldSlab), oldSlab))
            {
                // we successfully swapped in the new slab; wipe our copy so we don't dispose on exit
                newSlab = null;
                oldSlab.Dispose();
            }
            else
            {
                // someone else swapped; we'll keep our newSlab for now, in case we looop again and need another
            }
        }
        while (true);

        [DoesNotReturn]
        static void ThrowDisposed() => throw new ObjectDisposedException(nameof(SlabManager<int>));
    }

    internal sealed class Slab : IDisposable
    {
        private readonly T[] _array;
        private long _offsetAndRefCount = 1; // overlapped so we can do atomic updates
        public Slab(int size) => _array = ArrayPool<T>.Shared.Rent(size);

        private const long LO_MASK = 0xFFFFFFFF, HI_MASK = ~LO_MASK; // count is in LO, index is in HI

        internal bool IsAlive => (Volatile.Read(ref _offsetAndRefCount) & LO_MASK) != 0;

        public void Dispose()
        {
            long oldOffsetAndRefCount, oldCount;
            do
            {
                oldOffsetAndRefCount = Volatile.Read(ref _offsetAndRefCount);
                oldCount = oldOffsetAndRefCount & LO_MASK;
                if (oldCount == 0) return; // nothing to do
            }
            while (Interlocked.CompareExchange(
                ref _offsetAndRefCount,
                oldOffsetAndRefCount & HI_MASK | oldCount - 1, // decrement the count, leaving the index unchanged
                oldOffsetAndRefCount) != oldOffsetAndRefCount);

            if (oldCount == 1)
            {
                ArrayPool<T>.Shared.Return(_array);
            }
        }

        public override string ToString()
        {
            var offsetAndRefCount = Volatile.Read(ref _offsetAndRefCount);
            int count = (int)(offsetAndRefCount & LO_MASK), offset = (int)(offsetAndRefCount >> 32 & LO_MASK);
            return $"ref-count: {count}; {offset} of {_array.Length} consumed";
        }

        public bool TryTake(int size, out Memory<T> value)
        {
            if (size <= 0) ThrowInvalidSize();
            while (true)
            {
                var oldOffsetAndRefCount = Volatile.Read(ref _offsetAndRefCount);

                int oldCount = (int)(oldOffsetAndRefCount & LO_MASK), oldOffset = (int)(oldOffsetAndRefCount >> 32 & LO_MASK);
                if (oldCount == 0 || oldOffset + size > _array.Length)
                {
                    break; // already disposed, or does not fit
                }

                if (Interlocked.CompareExchange(
                    ref _offsetAndRefCount,
                    (long)(oldOffset + size) << 32 | (long)(oldCount + 1), // update the offset and increment the count
                    oldOffsetAndRefCount) == oldOffsetAndRefCount)
                {
                    value = new Memory<T>(_array, oldOffset, size);
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static void ThrowInvalidSize() => throw new ArgumentOutOfRangeException("size");

        internal bool TryExpand(int size, ref Memory<T> value)
        {
            if (size <= 0) ThrowInvalidSize();
            if (MemoryMarshal.TryGetArray<T>(value, out var segment) && ReferenceEquals(segment.Array, _array))
            {
                // we're talking about the correct array; has anything else been reserved from the end?
                long oldOffsetAndRefCount;
                while (true)
                {
                    oldOffsetAndRefCount = Volatile.Read(ref _offsetAndRefCount);
                    int oldCount = (int)(oldOffsetAndRefCount & LO_MASK), oldOffset = (int)(oldOffsetAndRefCount >> 32 & LO_MASK);

                    if (oldCount == 0 || oldOffset != segment.Offset + segment.Count || oldOffset + size > _array.Length)
                    {
                        break; // already disposed, or someone else has taken data, or does not fit
                    }
                    if (Interlocked.CompareExchange(
                        ref _offsetAndRefCount,
                        (long)(oldOffset + size) << 32 | oldOffsetAndRefCount & LO_MASK, // increment the offset, leaving the count unchanged
                        oldOffsetAndRefCount) == oldOffsetAndRefCount)
                    {
                        value = new Memory<T>(_array, segment.Offset, segment.Count + size);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
