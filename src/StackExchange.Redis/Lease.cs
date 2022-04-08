using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// A sized region of contiguous memory backed by a memory pool; disposing the lease returns the memory to the pool.
    /// </summary>
    /// <typeparam name="T">The type of data being leased.</typeparam>
    public sealed class Lease<T> : IMemoryOwner<T>
    {
        /// <summary>
        /// A lease of length zero.
        /// </summary>
        public static Lease<T> Empty { get; } = new Lease<T>(System.Array.Empty<T>(), 0);

        private T[]? _arr;

        /// <summary>
        /// The length of the lease.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Create a new lease.
        /// </summary>
        /// <param name="length">The size required.</param>
        /// <param name="clear">Whether to erase the memory.</param>
        public static Lease<T> Create(int length, bool clear = true)
        {
            if (length == 0) return Empty;
            var arr = ArrayPool<T>.Shared.Rent(length);
            if (clear) System.Array.Clear(arr, 0, length);
            return new Lease<T>(arr, length);
        }

        private Lease(T[] arr, int length)
        {
            _arr = arr;
            Length = length;
        }

        /// <summary>
        /// Release all resources owned by the lease.
        /// </summary>
        public void Dispose()
        {
            if (Length != 0)
            {
                var arr = Interlocked.Exchange(ref _arr, null);
                if (arr != null) ArrayPool<T>.Shared.Return(arr);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T[] ThrowDisposed() => throw new ObjectDisposedException(nameof(Lease<T>));

        private T[] Array
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _arr ?? ThrowDisposed();
        }

        /// <summary>
        /// The data as a <see cref="Memory{T}"/>.
        /// </summary>
        public Memory<T> Memory => new Memory<T>(Array, 0, Length);

        /// <summary>
        /// The data as a <see cref="Span{T}"/>.
        /// </summary>
        public Span<T> Span => new Span<T>(Array, 0, Length);

        /// <summary>
        /// The data as an <see cref="ArraySegment{T}"/>.
        /// </summary>
        public ArraySegment<T> ArraySegment => new ArraySegment<T>(Array, 0, Length);
    }
}
