using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

#nullable enable
namespace StackExchange.Redis.Transports;

/// <summary>
/// A <see cref="MemoryPool{T}"/> implementation that incorporates reference-counted tracking.
/// </summary>
internal abstract class RefCountedMemoryPool<T> : MemoryPool<T>, IAllocator<T>
{
    /// <summary>
    /// Gets a <see cref="RefCountedMemoryPool{T}"/> that uses <see cref="ArrayPool{T}"/>.
    /// </summary>
    public static new RefCountedMemoryPool<T> Shared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => SharedWrapper.Instance;
    }
    private static class SharedWrapper // to allow simple lazy/deferred, without any locking etc
    {
        public static readonly RefCountedMemoryPool<T> Instance = new ArrayRefCountedMemoryPool<T>(ArrayPool<T>.Shared);
    }

    /// <summary>
    /// Create a <see cref="RefCountedMemoryPool{T}"/> instance.
    /// </summary>
    public static RefCountedMemoryPool<T> Create(ArrayPool<T>? pool = default)
    {
        if (pool is null || ReferenceEquals(pool, ArrayPool<T>.Shared))
            return ArrayRefCountedMemoryPool<T>.Shared;
        return new ArrayRefCountedMemoryPool<T>(pool);
    }

    /// <summary>
    /// Create a <see cref="RefCountedMemoryPool{T}"/> instance.
    /// </summary>
    public static RefCountedMemoryPool<T> Create(MemoryPool<T> memoryPool)
    {
        if (memoryPool is RefCountedMemoryPool<T> refCounted) return refCounted;
        if (memoryPool is null || ReferenceEquals(memoryPool, MemoryPool<T>.Shared))
            return ArrayRefCountedMemoryPool<T>.Shared;
        return new WrappedRefCountedMemoryPool<T>(memoryPool);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) { }

    /// <summary>
    /// Returns a memory block capable of holding at least minBufferSize elements of <typeparamref name="T"/>.
    /// </summary>
    public Memory<T> RentMemory(int minBufferSize = -1)
    {
        if (minBufferSize <= 0) minBufferSize = 512; // modest default

        for (int slot = GetCacheSlot(minBufferSize); slot < CACHE_SLOTS; slot++)
        {
            var cache = TryGet(slot);
            if (cache is not null && cache.TryDequeue(out var existing))
            {
                Debug.Assert(existing.IsAlive(), "renting dead memory from cache");
                return existing; // note: no change of counter - just transfer of ownership
            }
        }

        return RentNew(minBufferSize).Memory;
    }

    /// <inheritdoc/>
    [Obsolete(nameof(RentMemory) + " should be used instead")]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member; this is very intentional - want to push people away from this API, since it can't use fragments
    public override IMemoryOwner<T> Rent(int minBufferSize = -1)
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
    {
        if (minBufferSize <= 0) minBufferSize = 512; // modest default
        return RentNew(minBufferSize);
    }

    private IMemoryOwner<T> RentNew(int minBufferSize)
    {
        var manager = RentRefCounted(minBufferSize);
        Debug.Assert(MemoryMarshal.TryGetMemoryManager<T, RefCountedMemoryManager<T>>(manager.Memory, out var viaMemory) && ReferenceEquals(viaMemory, manager),
            "incorrect memory manager detected");
        Debug.Assert(manager.IsAlive, "new memory is dead!");
        return manager;
    }

    /// <summary>
    /// Identical to <see cref="MemoryPool{T}.Rent(int)"/>, but with support for reference-counting.
    /// </summary>
    protected abstract RefCountedMemoryManager<T> RentRefCounted(int minBufferSize);

    const int MIN_USEFUL_LENGTH = 32, CACHE_SLOTS = 13;
    private static int GetCacheSlot(int length)
    {
        if (length <= MIN_USEFUL_LENGTH) return 0;
        return (26 - Utilities.LeadingZeroCount((uint)length)) / 2;
    }

    internal void Return(Memory<T> unused)
    {
        const int MIN_USEFUL_SIZE = 32, MAX_STORED_PER_KEY = 8;
        if (!MemoryMarshal.TryGetMemoryManager<T, RefCountedMemoryManager<T>>(unused, out var manager, out var start, out var length))
            return; // not ref-counted; just ignore

        Debug.Assert(manager.IsAlive, "returning dead memory");
        Debug.Assert(start + unused.Length == manager.Memory.Length, "expect to be the tail end of the buffer");

        if (unused.Length >= MIN_USEFUL_SIZE && (start + unused.Length != manager.Memory.Length))
        {   // only recycle if we have a sensible amount of space left, and we're returning the entire tail of the buffer
            var slot = Math.Min(GetCacheSlot(unused.Length), CACHE_SLOTS - 1);
            while (slot >= 0)
            {
                var store = Get(slot);
                if (store.Count < MAX_STORED_PER_KEY)
                {
                    store.Enqueue(unused);
                    return; // note: we don't decrement the counter in this case - just transfer ownership
                }
                slot--; // try again, but storing an oversized buffer in a smaller pot
            }
        }

        // we can't store everything
        manager.Dispose();
    }

    private ConcurrentQueue<Memory<T>>? TryGet(int scale)
        => _fragments[scale];
    private ConcurrentQueue<Memory<T>> Get(int scale)
        => _fragments[scale] ?? GetSlow(scale);

    private ConcurrentQueue<Memory<T>> GetSlow(int scale)
    {
        var value = Volatile.Read(ref _fragments[scale]);
        if (value is null)
        {
            value = new ConcurrentQueue<Memory<T>>();
            value = Interlocked.CompareExchange(ref _fragments[scale], value, null) ?? value;
        }
        return value;
    }

    Memory<T> IAllocator<T>.Allocate(int count)
    {
        if (count == 0) return default;
        if (count < 0) ThrowOutOfRange();

        var memory = RentMemory(count);
        if (memory.Length > count)
        {
            Return(memory.Slice(count));
            memory = memory.Slice(0, count);
        }
        return memory;

        static void ThrowOutOfRange() => throw new ArgumentOutOfRangeException(nameof(count));
    }

    private ConcurrentQueue<Memory<T>>?[] _fragments = new ConcurrentQueue<Memory<T>>?[CACHE_SLOTS];
}

/// <summary>
/// A <see cref="MemoryManager{T}"/> implementation that incorporates reference-counted tracking.
/// </summary>
internal abstract partial class RefCountedMemoryManager<T> : MemoryManager<T>, IDisposable // re-implement
{
    /// <inheritdoc/>
    public sealed override Memory<T> Memory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => base.Memory; // to prevent implementors from breaking the identity
    }

    private int _refCount, _pinCount;
    private MemoryHandle _pinHandle;
    /// <summary>
    /// Create a new instance.
    /// </summary>
    protected RefCountedMemoryManager()
    {
        _refCount = 1;
    }

    internal bool IsAlive => Volatile.Read(ref _refCount) > 0;

    /// <inheritdoc/>
    protected sealed override void Dispose(bool disposing)
    {   // shouldn't get here since re-implemented, but!
        if (disposing) Dispose();
    }

    /// <summary>
    /// Decrement the reference count associated with this instance; calls <see cref="Release"/> when the count becomes zero.
    /// </summary>
    public void Dispose()
    {
        switch (Interlocked.Decrement(ref _refCount))
        {
            case 0: // all done
                Release();
                GC.SuppressFinalize(this);
                break;
            case -1:
                Throw();
                break;
        }
        static void Throw() => throw new InvalidOperationException("Ref-counted memory was disposed too many times; all bets are off");
    }

    /// <summary>
    /// Recycle the data held by this instance.
    /// </summary>
    protected abstract void Release();

    /// <summary>
    /// Increment the reference count associated with this instance.
    /// </summary>
    public void Preserve()
    {
        if (Interlocked.Increment(ref _refCount) < 0) Throw();
        static void Throw() => throw new InvalidOperationException("Ref-counted memory was preserved too many times; all bets are off");
    }

    /// <summary>
    /// Pin this data so that it does not move during garbage collection.
    /// </summary>
    protected virtual MemoryHandle Pin() => throw new NotSupportedException(nameof(Pin));

    /// <inheritdoc/>
    public sealed override MemoryHandle Pin(int elementIndex = 0)
    {
        if (elementIndex < 0 || elementIndex >= Memory.Length) Throw();
        lock (this) // use lock when pinning to avoid races
        {
            if (_pinCount == 0)
            {
                _pinHandle = Pin();
                Preserve(); // pin acts as a ref
            }
            _pinCount = checked(_pinCount + 1); // note: no incr if Pin() not supported
            unsafe
            {   // we can hand this outside the "unsafe", because it is pinned, but:
                // we always use ourselves as the IPinnable - we need to react, etc
                var ptr = _pinHandle.Pointer;
                if (elementIndex != 0) ptr = Unsafe.Add<T>(ptr, elementIndex);
                return new MemoryHandle(ptr, default, this);
            }
        }
        static void Throw() => throw new ArgumentOutOfRangeException(nameof(elementIndex));
    }

    /// <inheritdoc/>
    public sealed override void Unpin()
    {
        lock (this) // use lock when pinning to avoid races
        {
            if (_pinCount == 0) Throw();
            if (--_pinCount == 0)
            {
                var tmp = _pinHandle;
                _pinHandle = default;
                tmp.Dispose();
                Dispose(false); // we also took a regular ref
            }
        }
        static void Throw() => throw new InvalidOperationException();
    }
}

internal sealed class ArrayRefCountedMemoryPool<T> : RefCountedMemoryPool<T>
{
    private readonly ArrayPool<T> _pool;

    // advertise BCL limits (oddly, ArrayMemoryPool just uses int.MaxValue here, but that's... wrong)
    public override int MaxBufferSize => Unsafe.SizeOf<T>() == 1 ? 0x7FFFFFC7 : 0X7FEFFFFF;
    public ArrayRefCountedMemoryPool(ArrayPool<T> pool)
    {
        if (pool is null) throw new ArgumentNullException(nameof(pool));
        _pool = pool;
    }
    protected override RefCountedMemoryManager<T> RentRefCounted(int minBufferSize)
        => new ArrayRefCountedMemoryManager(_pool, minBufferSize);

    sealed class ArrayRefCountedMemoryManager : RefCountedMemoryManager<T>
    {
        private readonly ArrayPool<T> _pool;
        private T[]? _array;
        private T[] Array
        {
            get
            {
                return _array ?? Throw();
                static T[] Throw() => throw new ObjectDisposedException(nameof(ArrayRefCountedMemoryManager));
            }
        }
        public ArrayRefCountedMemoryManager(ArrayPool<T> pool, int minimumLength)
        {
            _pool = pool;
            _array = pool.Rent(minimumLength);
        }

        public override Span<T> GetSpan() => Array;
        protected override bool TryGetArray(out ArraySegment<T> segment)
        {
            segment = new ArraySegment<T>(Array);
            return true;
        }
        protected override MemoryHandle Pin()
        {
            var gc = GCHandle.Alloc(Array, GCHandleType.Pinned);
            unsafe
            {
                return new MemoryHandle(gc.AddrOfPinnedObject().ToPointer(), gc, null);
            }
        }

        protected override void Release()
        {
            // note: we're fine if operations after this cause NREs
            var arr = Interlocked.Exchange(ref _array, null);
            if (arr is not null) _pool.Return(arr, clearArray: false);
        }
    }
}

internal sealed class WrappedRefCountedMemoryPool<T> : RefCountedMemoryPool<T>
{
    private MemoryPool<T> _pool;

    public override int MaxBufferSize => _pool.MaxBufferSize;
    public WrappedRefCountedMemoryPool(MemoryPool<T> pool)
        => _pool = pool;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pool.Dispose(); // we'll assume we have ownership
    }
    protected override RefCountedMemoryManager<T> RentRefCounted(int minBufferSize)
        => new WrappedRefCountedMemoryManager(_pool.Rent(minBufferSize));

    sealed class WrappedRefCountedMemoryManager : RefCountedMemoryManager<T>
    {
        private IMemoryOwner<T>? _owner;
        private IMemoryOwner<T> Owner
        {
            get
            {
                return _owner ?? Throw();
                static IMemoryOwner<T> Throw() => throw new ObjectDisposedException(nameof(WrappedRefCountedMemoryManager));
            }
        }


        public WrappedRefCountedMemoryManager(IMemoryOwner<T> owner)
            => _owner = owner;

        protected override void Release()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Dispose();
        }

        public override Span<T> GetSpan() => Owner.Memory.Span;
        protected override MemoryHandle Pin()
        {
            if (!MemoryMarshal.TryGetMemoryManager<T, MemoryManager<T>>(Owner.Memory, out var manager))
            {
                return Throw();
            }
            return manager.Pin();
            static MemoryHandle Throw() => throw new NotSupportedException(nameof(Pin));
        }
    }
}
