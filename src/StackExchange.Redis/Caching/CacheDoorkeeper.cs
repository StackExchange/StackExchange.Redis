using System;
using System.Threading;

namespace StackExchange.Redis.Caching
{
    /// <summary>
    /// Remembers which requests have missed recently, so that only a repeated miss is stored: the "doorkeeper" in front
    /// of TinyLFU. See <see cref="CacheAdmission.OnRepeatedMiss"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A Bloom filter with two probes</b>, over the hash the request already carries: no allocation, no key copy,
    /// a few nanoseconds against a miss that already costs a round trip. It answers "seen before?" with false
    /// positives only - an occasional first miss admitted - never false negatives, so a hot request is never kept out.
    /// </para>
    /// <para>
    /// <b>Cleared, not decayed</b>, once it has been asked about an eighth as many misses as it has bits - so at most
    /// that many requests are recorded, where two probes leave the false-positive rate near 5%. That makes "recently"
    /// a window of misses, and keeps the cost of remembering bounded however large the keyspace is. A request recorded
    /// just before a clear needs one more miss; that is the whole price.
    /// </para>
    /// <para>
    /// <b>Every miss ages it, not only first sightings</b> - as TinyLFU ages by samples. Counting only what it
    /// recorded, a keyspace smaller than the window was never forgotten: once each key had missed once, nothing new
    /// arrived to trigger a clear, every later miss read as a repeat, and admission became
    /// <see cref="CacheAdmission.OnFirstMiss"/> for good (RespFest's Zipf cells, replaying ~120,000 distinct keys
    /// against a 131,072 window: 72.5% hits either way; modelled, aging by misses gives 76.1% there and on independent
    /// draws, where first-sighting aging gave 75.3%).
    /// </para>
    /// <para>
    /// <b>Racy on purpose.</b> Two threads recording at once may both see "new", or a clear may land between one's
    /// test and set; every such race ends in a request being admitted one miss early or late, never in anything
    /// unsound - this decides only whether to cache, never what a reply says.
    /// </para>
    /// </remarks>
    internal sealed class CacheDoorkeeper
    {
        private readonly long[] _words;
        private readonly int _mask; // bit index mask
        private readonly int _clearAfter;
        private int _recorded;

        /// <summary>A filter sized for a cache of about <paramref name="expectedEntries"/> entries.</summary>
        /// <param name="expectedEntries">Roughly how many entries the cache holds; sizes the filter.</param>
        internal CacheDoorkeeper(long expectedEntries)
        {
            // ~8 bits per entry the cache can hold, as a power of two between 4 Ki and 16 Mi bits (512 B to 2 MiB)
            var bits = 1 << 12;
            while (bits < (1 << 24) && bits < expectedEntries * 8) bits <<= 1;
            _words = new long[bits / 64];
            _mask = bits - 1;
            _clearAfter = bits / 8;
        }

        /// <summary>Misses asked about since the last clear; for tests.</summary>
        internal int Recorded => Volatile.Read(ref _recorded);

        /// <summary>Whether this request has missed recently; if not, remember that it has now.</summary>
        /// <param name="hash">The request's hash.</param>
        internal bool SeenBefore(int hash)
        {
            // two probes from one 32-bit hash: the low bits of a full avalanche (MurmurHash3's finalizer) for the first,
            // its rotation for the second. Not a bare multiply: its low bits depend only on the hash's low bits, and a
            // hash weak there put 200,000 requests on ~4,500 bits, so the filter saturated and saw every miss as a repeat
            var mixed = unchecked((uint)hash);
            mixed ^= mixed >> 16;
            mixed = unchecked(mixed * 0x85EBCA6Bu);
            mixed ^= mixed >> 13;
            mixed = unchecked(mixed * 0xC2B2AE35u);
            mixed ^= mixed >> 16;
            var first = (int)(mixed & (uint)_mask);
            var second = (int)(((mixed >> 16) | (mixed << 16)) & (uint)_mask);

            var seen = IsSet(first) && IsSet(second);
            if (!seen)
            {
                Set(first);
                Set(second);
            }

            if (Interlocked.Increment(ref _recorded) >= _clearAfter) Clear();
            return seen;
        }

        private bool IsSet(int bit) => (Volatile.Read(ref _words[bit >> 6]) & (1L << (bit & 63))) != 0;

        private void Set(int bit)
        {
            ref var word = ref _words[bit >> 6];
            var mask = 1L << (bit & 63);
            if ((Volatile.Read(ref word) & mask) != 0) return; // already set: no write, so no shared line bounced
#if NET
            Interlocked.Or(ref word, mask);
#else
            long current;
            do
            {
                current = Volatile.Read(ref word);
            }
            while ((current & mask) == 0 && Interlocked.CompareExchange(ref word, current | mask, current) != current);
#endif
        }

        private void Clear()
        {
            // whoever resets the count does the clearing; anyone recording meanwhile lands in either generation
            if (Interlocked.Exchange(ref _recorded, 0) < _clearAfter) return;
            Array.Clear(_words, 0, _words.Length);
        }
    }
}
