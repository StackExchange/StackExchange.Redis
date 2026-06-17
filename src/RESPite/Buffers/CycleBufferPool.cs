using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace RESPite.Buffers;

[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public abstract class CycleBufferPool
{
    /// <summary>
    /// Create an initial buffer.
    /// </summary>
    public virtual IMemoryOwner<byte> Rent() => Rent(default);

    /// <summary>
    /// Create a buffer with knowledge of the existing leased data.
    /// </summary>
    public abstract IMemoryOwner<byte> Rent(in ReadOnlySequence<byte> existing);

    // new MemoryPool(...) would be a non-growing buffer pool.
    public static CycleBufferPool Default { get; } = new GrowingMemoryPool(minBytes: 8 * 1024);

    private class MemoryPool : CycleBufferPool
    {
#if TRACK_MEMORY
        private static MemoryPool<byte> DefaultPool => MemoryTrackedPool<byte>.Shared;
#else
        private static MemoryPool<byte> DefaultPool => MemoryPool<byte>.Shared;
#endif
        private readonly MemoryPool<byte> _pool;
        private readonly int _minBytes, _maxBytes;

        public MemoryPool(int minBytes, MemoryPool<byte>? pool = null, int maxBytes = int.MaxValue)
        {
            _pool = pool ?? DefaultPool;
            // capture the max bytes, without exceeding the pool's max size
            _maxBytes = Math.Min(maxBytes, _pool.MaxBufferSize);
            // capture the min bytes, applying a rigid lower bound, and not overlapping the max bytes
            _minBytes = Math.Min(Math.Max(minBytes, 16), _maxBytes);
        }

        /// <summary>
        /// Rent a chunk using the specified size as a hint.
        /// </summary>
        protected IMemoryOwner<byte> Rent(int bytes)
        {
#if NET
            bytes = Math.Clamp(bytes, _minBytes, _maxBytes);
#else
            bytes = Math.Min(Math.Max(bytes, _minBytes), _maxBytes);
#endif
            return _pool.Rent(bytes);
        }

        // by default, use fixed size without reference to the existing data; subclasses can tweak

        /// <inheritdoc/>
        public override IMemoryOwner<byte> Rent() => Rent(_minBytes);

        /// <inheritdoc/>
        public override IMemoryOwner<byte> Rent(in ReadOnlySequence<byte> existing) => Rent(_minBytes);
    }

    private sealed class GrowingMemoryPool(int minBytes, MemoryPool<byte>? pool = null, int maxBytes = int.MaxValue)
        : MemoryPool(minBytes, pool, maxBytes)
    {
        public override IMemoryOwner<byte> Rent(in ReadOnlySequence<byte> existing)
        {
            if (existing.IsEmpty) return base.Rent(existing);
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

            // "max" here is to account for overflow - i.e. stop growing if that happens (unlikely)
            return Rent(Math.Max(lastChunk, lastChunk << 1));
        }
    }
}
