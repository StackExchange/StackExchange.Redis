using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

        private object? _buffer;

        /// <summary>
        /// Gets whether this lease is empty.
        /// </summary>
        public bool IsEmpty => Length == 0;

        /// <summary>
        /// The length of the lease.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Create a new lease.
        /// </summary>
        /// <param name="length">The size required.</param>
        /// <param name="clear">Whether to erase the memory.</param>
#pragma  warning disable RS0026 // multiple overloads with optional args; not ambiguous in this case.
        public static Lease<T> Create(int length, bool clear = true)
#pragma  warning restore RS0026
        {
            if (length is 0) return Empty;
            var arr = ArrayPool<T>.Shared.Rent(length);
            var lease = new Lease<T>(arr, length);
            if (clear) lease.Span.Clear();
            return lease;
        }

        /// <summary>
        /// Create a new lease.
        /// </summary>
        /// <param name="length">The size required.</param>
        /// <param name="pool">The buffer source to use; if <c>null</c>, <see cref="ArrayPool{T}.Shared"/> is used.</param>
        /// <param name="clear">Whether to erase the memory.</param>
#pragma  warning disable RS0026 // multiple overloads with optional args; not ambiguous in this case.
        public static Lease<T> Create(int length, MemoryPool<T>? pool, bool clear = true)
#pragma  warning restore RS0026
        {
            if (pool is null) return Create(length, clear);
            if (length is 0) return Empty;

            var lease = new Lease<T>(pool.Rent(length), length);
            if (clear) lease.Span.Clear();
            return lease;
        }

        private Lease(T[] arr, int length)
        {
            _buffer = arr;
            Length = length;
        }

        private Lease(IMemoryOwner<T> memoryOwner, int length)
        {
            _buffer = memoryOwner;
            Length = length;
        }

        /// <summary>
        /// Release all resources owned by the lease.
        /// </summary>
        public void Dispose()
        {
            if (Length != 0)
            {
                var buffer = Interlocked.Exchange(ref _buffer, null);
                if (buffer != null)
                {
                    if (buffer is T[] arr)
                        ArrayPool<T>.Shared.Return(arr);
                    else
                        ((IMemoryOwner<T>)buffer).Dispose();
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T[] ThrowDisposed() => throw new ObjectDisposedException(nameof(Lease<T>));

        /// <summary>
        /// The data as a <see cref="Memory{T}"/>.
        /// </summary>
        public Memory<T> Memory => _buffer is IMemoryOwner<T> memoryOwner
            ? memoryOwner.Memory.Slice(0, Length)
            : new Memory<T>((T[]?)_buffer ?? ThrowDisposed(), 0, Length);

        /// <summary>
        /// The data as a <see cref="Span{T}"/>.
        /// </summary>
        public Span<T> Span => _buffer is IMemoryOwner<T> memoryOwner
            ? memoryOwner.Memory.Span.Slice(0, Length)
            : new Span<T>((T[]?)_buffer ?? ThrowDisposed(), 0, Length);

        /// <summary>
        /// The data as an <see cref="ArraySegment{T}"/>.
        /// </summary>
        public ArraySegment<T> ArraySegment
        {
            get
            {
                if (_buffer is IMemoryOwner<T> memoryOwner)
                {
                    if (!MemoryMarshal.TryGetArray((ReadOnlyMemory<T>)memoryOwner.Memory, out var segment))
                        throw new NotSupportedException("Only array-backed buffers are supported");

                    return new ArraySegment<T>(segment.Array!, segment.Offset, Length);
                }
                return new ArraySegment<T>((T[]?)_buffer ?? ThrowDisposed(), 0, Length);
            }
        }
    }
}
