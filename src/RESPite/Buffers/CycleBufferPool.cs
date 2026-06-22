using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RESPite.Buffers;

internal static class MemoryPoolExtensions
{
    internal static IMemoryOwner<T> Rent<T>(this MemoryPool<T> pool, in ReadOnlySequence<T> existing)
    {
        return pool is CycleBufferPool<T> typed
            ? typed.Rent(existing)
            : pool.Rent(Math.Min(pool.MaxBufferSize, NextSize(existing)));
    }

    private const int DefaultPageSizeBytes = 8 * 1024;

    internal static int NextSize<T>(in ReadOnlySequence<T> existing)
    {
        if (existing.IsEmpty) return DefaultPageSizeBytes / Unsafe.SizeOf<T>();

        // use a growth strategy looking at the size of the last segment
        int lastChunk;
        if (existing.IsSingleSegment)
        {
            lastChunk = existing.First.Length;
        }
        else if (existing.End.GetObject() is ReadOnlySequenceSegment<byte> segment)
        {
            lastChunk = segment.Memory.Length; // note we ignore GetInteger() intentionally
        }
        else
        {
            // do it the hard way; note we'll only observe the reserved size, rather
            // than the actual buffer size, but that's the best we can do
            lastChunk = 0;
            foreach (var chunk in existing)
            {
                if (!chunk.IsEmpty) lastChunk = chunk.Length;
            }

            if (lastChunk is 0) lastChunk = existing.First.Length; // paranoia
        }

        // apply a fixed lower bound - don't start with trivial growth
        lastChunk = Math.Max(lastChunk, 16);

        // apply doubling; "max" here is to account for overflow - i.e. stop growing if that happens (unlikely)
        return Math.Max(lastChunk, lastChunk << 1);
    }
}

[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public abstract class CycleBufferPool<T> : MemoryPool<T>
{
    /// <summary>
    /// Create a buffer with knowledge of the existing leased data.
    /// </summary>
    public abstract IMemoryOwner<T> Rent(in ReadOnlySequence<T> existing);

    // new MemoryPool(...) would be a non-growing buffer pool.
    public static CycleBufferPool<T> Default { get; } = new DefaultPool();

    private class DefaultPool : CycleBufferPool<T>
    {
        private static readonly MemoryPool<T> Tail =
#if TRACK_MEMORY
            MemoryTrackedPool<T>.Shared;
#else
            MemoryPool<T>.Shared;
#endif

        // ReSharper disable once StaticMemberInGenericType - intentional to memoize, but can vary per T
        private static readonly int MaxSize = Tail.MaxBufferSize;

        protected override void Dispose(bool disposing) { }

        /// <inheritdoc/>
        public override IMemoryOwner<T> Rent(int minBufferSize = -1) => Tail.Rent(minBufferSize);

        public override int MaxBufferSize => Tail.MaxBufferSize;

        /// <inheritdoc/>
        public override IMemoryOwner<T> Rent(in ReadOnlySequence<T> existing)
            => Tail.Rent(Math.Min(MemoryPoolExtensions.NextSize(existing), MaxSize));
    }
}
