using System;
using System.Buffers;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// A sized region of contiguous read-only memory; disposing the lease releases it.
    /// </summary>
    /// <typeparam name="T">The type of data being leased.</typeparam>
    /// <remarks>
    /// <para>
    /// The read-only sibling of <see cref="Lease{T}"/>, and the difference is not cosmetic - it is what
    /// allows the data to be <b>shared</b> rather than copied. A mutable lease over memory anyone else can
    /// reach lets one holder rewrite what another is about to read; a read-only one cannot, so it is safe
    /// to hand out a view of a buffer that is still owned elsewhere - a client-side cache entry, for
    /// instance.
    /// </para>
    /// <para>
    /// Hence the rule the two types express between them: <b>share what cannot be written, copy what can</b>.
    /// A <see cref="Lease{T}"/> is safe because the caller owns bytes nobody else reaches;
    /// a <see cref="ReadOnlyLease{T}"/> is safe because nobody can write through it.
    /// </para>
    /// <para>
    /// Deliberately a class, not a <c>struct</c>. It owns a reference that must be released exactly once,
    /// and the underlying release is a bare decrement with no idempotence guard - so a struct copied and
    /// disposed twice would drive the count negative and hand a live buffer back to its pool while other
    /// holders still read from it.
    /// </para>
    /// <para>
    /// There is deliberately no <c>ArraySegment</c> accessor. <see cref="Lease{T}"/> has one, and it hands
    /// out the underlying array - which for a shared buffer is a way to reach outside the lease entirely.
    /// </para>
    /// </remarks>
    public sealed class ReadOnlyLease<T> : IDisposable
    {
        /// <summary>
        /// A lease of length zero.
        /// </summary>
        public static ReadOnlyLease<T> Empty { get; } = new(System.Array.Empty<T>(), 0, 0);

        // either a T[] rented from the shared pool (the copied case), or an IMemoryOwner<T> whose memory
        // this lease holds a reference to (the shared case). One type covers both because a payload that
        // is not one contiguous run - a streamed scalar arriving in chunks - has to be assembled, and
        // therefore copied, however much we would rather share it.
        private object? _buffer;

        private readonly int _offset;

        /// <summary>Gets whether this lease is empty.</summary>
        public bool IsEmpty => Length == 0;

        /// <summary>The length of the lease.</summary>
        public int Length { get; }

        private ReadOnlyLease(object? buffer, int offset, int length)
        {
            _buffer = buffer;
            _offset = offset;
            Length = length;
        }

        /// <summary>Create a lease over a rented array, for data that had to be copied.</summary>
        /// <param name="length">The size required.</param>
        /// <param name="pool">The pool to rent from; the shared array pool when null.</param>
        /// <param name="target">The memory to write the data into.</param>
        internal static ReadOnlyLease<T> Rent(int length, MemoryPool<T>? pool, out Span<T> target)
        {
            if (length == 0)
            {
                target = default;
                return Empty;
            }

            if (pool is not null)
            {
                var owner = pool.Rent(length);
                target = owner.Memory.Span.Slice(0, length);
                return new ReadOnlyLease<T>(owner, 0, length);
            }

            var array = ArrayPool<T>.Shared.Rent(length);
            target = new Span<T>(array, 0, length);
            return new ReadOnlyLease<T>(array, 0, length);
        }

        /// <summary>
        /// Take ownership of an array that has <b>already been rented</b> from
        /// <see cref="ArrayPool{T}"/>, of which only the first <paramref name="length"/> elements are live.
        /// </summary>
        /// <param name="pooled">The rented array; this lease returns it on disposal.</param>
        /// <param name="length">How many elements are actually populated.</param>
        /// <remarks>
        /// For a parser that already rents - <c>ParseArray(allowOversized: true)</c> is the case this exists
        /// for - so its result becomes a lease without a copy. The caller must not keep using the array
        /// afterwards: this lease is now the owner, and will hand it back.
        /// </remarks>
        internal static ReadOnlyLease<T> Adopt(T[] pooled, int length)
            => length == 0 ? Empty : new ReadOnlyLease<T>(pooled, 0, length);

        /// <summary>
        /// Create a lease that <b>shares</b> an existing buffer rather than copying out of it.
        /// </summary>
        /// <param name="owner">The buffer to point into; a reference must already have been taken.</param>
        /// <param name="offset">Where the data starts within the buffer.</param>
        /// <param name="length">How much of it belongs to this lease.</param>
        /// <remarks>
        /// The caller takes the reference; disposing this lease gives it back. Sharing is why this type
        /// exists - see the remarks on the type.
        /// </remarks>
        internal static ReadOnlyLease<T> Share(IMemoryOwner<T> owner, int offset, int length)
            => length == 0 ? Empty : new ReadOnlyLease<T>(owner, offset, length);

        /// <summary>The data as a <see cref="ReadOnlyMemory{T}"/>.</summary>
        public ReadOnlyMemory<T> Memory => _buffer is IMemoryOwner<T> owner
            ? owner.Memory.Slice(_offset, Length)
            : new ReadOnlyMemory<T>((T[]?)_buffer ?? ThrowDisposed(), _offset, Length);

        /// <summary>The data as a <see cref="ReadOnlySpan{T}"/>.</summary>
        public ReadOnlySpan<T> Span => _buffer is IMemoryOwner<T> owner
            ? owner.Memory.Span.Slice(_offset, Length)
            : new ReadOnlySpan<T>((T[]?)_buffer ?? ThrowDisposed(), _offset, Length);

        /// <summary>Copy the contents into a new array.</summary>
        /// <remarks>For a caller who needs to own the data outright, or to outlive this lease.</remarks>
        public T[] ToArray() => Span.ToArray();

        private static T[] ThrowDisposed() => throw new ObjectDisposedException(nameof(ReadOnlyLease<T>));

        /// <summary>
        /// Whether a returned array must be wiped before it goes back to the pool.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only when <typeparamref name="T"/> can hold a reference. For <c>byte</c> - the only element type
        /// this began with - clearing is pure cost and buys nothing. For <see cref="RedisValue"/>,
        /// <see cref="HashEntry"/> and friends it is not optional: a pooled array that is handed back still
        /// points at whatever was in it, so every one of those objects stays reachable until the buffer
        /// happens to be rented and overwritten. That is not a leak that ever throws; it is a heap that
        /// quietly does not shrink, which is materially harder to find.
        /// </para>
        /// <para>
        /// <c>RuntimeHelpers.IsReferenceOrContainsReferences</c> answers this exactly, but does not exist on
        /// <c>net461</c>/<c>netstandard2.0</c>. Down-level the fallback errs towards clearing: a needless
        /// wipe costs a memset, a missed one costs retention, and those are not the same size of mistake.
        /// </para>
        /// </remarks>
        private static readonly bool ClearOnReturn =
#if NET
            System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#else
            !(typeof(T).IsPrimitive || typeof(T).IsEnum);
#endif

        /// <summary>Release the memory owned or referenced by this lease.</summary>
        /// <remarks>
        /// Exchange-to-null makes this once-only however many times it is called, which matters because the
        /// release underneath is not idempotent: a second one would decrement somebody else's reference.
        /// </remarks>
        public void Dispose()
        {
            if (Length == 0) return;

            var buffer = Interlocked.Exchange(ref _buffer, null);
            switch (buffer)
            {
                case T[] array:
                    ArrayPool<T>.Shared.Return(array, ClearOnReturn);
                    break;
                case IMemoryOwner<T> owner:
                    owner.Dispose();
                    break;
            }
        }
    }
}
