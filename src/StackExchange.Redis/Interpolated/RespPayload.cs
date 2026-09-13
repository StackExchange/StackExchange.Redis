using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Buffers;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. A cached response body, held as a pooled blob rather than as parsed objects, so
    /// that a cache hit costs a reference-count bump and a parse - and no allocation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The protocol.</b> A cache entry can be evicted or invalidated at any moment, including between the
    /// dictionary lookup and the read. So finding the entry is not enough - the read side must be:
    /// </para>
    /// <code>
    /// if (cache.TryGetValue(key, out var payload) &amp;&amp; payload.TryRetain())
    /// {
    ///     try { /* parse payload.Span here; the buffer cannot be recycled */ }
    ///     finally { payload.Release(); }
    /// }
    /// </code>
    /// <para>
    /// Success means BOTH that the entry was found AND that the reference count was incremented from a
    /// non-zero value. A zero count means eviction already won the race and the buffer is back in the pool;
    /// that is a miss, not an error. This is the shape used in <c>HybridCache</c> for the same reason.
    /// </para>
    /// <para>
    /// <b>Why the increment must be checked.</b> A bare <c>Interlocked.Increment</c> would resurrect a count
    /// from zero - exactly the window in which the array has already been handed back and may already be
    /// serving another rent. <see cref="RefCountedBuffer.TryAddRef"/> is increment-if-non-zero for that
    /// reason, and the failure mode it prevents is wrong data served from cache, not a crash.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public sealed class RespPayload : IDisposable
    {
        private readonly RefCountedBuffer _lease;
        private readonly int _offset;
        private readonly int _length;

        internal RespPayload(RefCountedBuffer lease, int offset, int length)
        {
            _lease = lease;
            _offset = offset;
            _length = length;
        }

        /// <summary>Copy a response body into a pooled blob, with one reference held by the caller.</summary>
        /// <param name="value">The bytes to cache.</param>
        /// <remarks>
        /// A copy, because the bytes being cached arrive in a connection buffer that is about to be reused.
        /// In the real thing this is where the response frame's own lease would be shared instead - see
        /// <c>RespResult</c>, which already reserves against the reader's buffer rather than copying.
        /// </remarks>
        public static RespPayload Create(ReadOnlySpan<byte> value)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, value.Length));
            value.CopyTo(buffer);
            return new RespPayload(RefCountedBuffer.Adopt(buffer, buffer.Length), 0, value.Length);
        }

        /// <summary>The number of live references; zero once the blob is back in the pool.</summary>
        internal int RefCount => _lease.RefCount;

        /// <summary>
        /// Take a reference, so the blob cannot be recycled while it is being read. Returns <c>false</c> if
        /// it has already gone - treat that as a cache miss.
        /// </summary>
        /// <remarks>Every successful call must be paired with exactly one <see cref="Release"/>.</remarks>
        public bool TryRetain() => _lease.TryAddRef();

        /// <summary>Drop a reference taken by <see cref="TryRetain"/>.</summary>
        public void Release() => _lease.Release();

        /// <summary>
        /// The cached bytes. Only valid while a reference is held; throws once the last one has gone.
        /// </summary>
        /// <remarks>
        /// The throw catches a caller reading after releasing. It is NOT a substitute for holding a
        /// reference: without one, another thread can recycle the buffer between the check and the read,
        /// and then the bytes are simply somebody else's. Correctness comes from <see cref="TryRetain"/>.
        /// </remarks>
        public ReadOnlySpan<byte> Span => _lease.GetSpan().Slice(_offset, _length);

        /// <summary>
        /// A reader over the cached bytes. A <c>ref struct</c>, so it cannot outlive the retained window.
        /// </summary>
        public RespReader GetReader() => new(Span);

        /// <summary>Drop the reference held by whoever created or retained this payload.</summary>
        public void Dispose() => Release();
    }
}
