#if TRACK_MEMORY
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace RESPite.Buffers;

internal sealed class MemoryTrackedPool<T> : MemoryPool<T>
{
    // like MemoryPool<T>, but tracks and reports double disposal via a custom memory manager, which
    // allows all future use of a Memory<T> to be tracked; contrast ArrayMemoryPool<T>, which only tracks
    // the initial fetch of .Memory from the lease
    public override IMemoryOwner<T> Rent(int minBufferSize = -1) => MemoryManager.Rent(minBufferSize);

    protected override void Dispose(bool disposing)
    {
    }

    // ReSharper disable once ArrangeModifiersOrder - you're wrong
    public static new MemoryTrackedPool<T> Shared { get; } = new();

    public override int MaxBufferSize => MemoryPool<T>.Shared.MaxBufferSize;

    private MemoryTrackedPool()
    {
    }

    private sealed class MemoryManager : MemoryManager<T>
    {
        public static IMemoryOwner<T> Rent(int minBufferSize = -1) => new MemoryManager(minBufferSize);

        private T[]? array;
        private MemoryManager(int minBufferSize)
        {
            array = ArrayPool<T>.Shared.Rent(Math.Max(64, minBufferSize));
        }

        private T[] CheckDisposed()
        {
            return array ?? Throw();
            [DoesNotReturn]
            static T[] Throw() => throw new ObjectDisposedException("Use-after-free of Memory-" + typeof(T).Name);
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException(nameof(Pin));

        public override void Unpin() => throw new NotSupportedException(nameof(Unpin));

        public override Span<T> GetSpan() => CheckDisposed();

        protected override bool TryGetArray(out ArraySegment<T> segment)
        {
            segment = new ArraySegment<T>(CheckDisposed());
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            var arr = Interlocked.Exchange(ref array, null);
            if (arr is not null) ArrayPool<T>.Shared.Return(arr);
        }
    }
}
#endif
