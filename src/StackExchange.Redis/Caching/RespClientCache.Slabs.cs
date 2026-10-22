using System;
using System.Collections.Generic;
using System.Threading;
using RESPite.Buffers;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis.Caching
{
    /// <summary>
    /// Where cached replies live: carved, in fill order, out of large arrays ("slabs") that the GC owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not one array per reply.</b> That made a hit free of shared writes - the GC is the reference count, so
    /// no reader can ever find recycled bytes - but every entry became exactly the object a generational GC handles
    /// worst: alive long enough to be promoted, then dead on eviction. Measured on the all-miss path (RespFest's
    /// <c>cache-miss-conc64</c>): ~100 gen1 and 5-10 gen2 collections where the pooled version had none.
    /// </para>
    /// <para>
    /// <b>A slab keeps the safety and drops the churn.</b> It is one large object, born in the old generation, so
    /// nothing is promoted and there is next to nothing to mark; it is never reused, only dropped, so a reader still
    /// parsing simply keeps it alive - the GC remains the count, and hits still write nothing shared.
    /// </para>
    /// <para>
    /// <b>Entries die in roughly the order they were born.</b> <see cref="CachePolicy.TimeToLive"/> runs from the
    /// fill, so a slab is wholly dead within one lifetime of its last fill, and a hot key's refresh lands in the
    /// current slab rather than holding an old one open. Eviction goes by slab, oldest first, which frees the
    /// budget in whole slabs rather than leaving holes it cannot use.
    /// </para>
    /// <para>
    /// <b>The budget counts slabs, not replies.</b> A slab holding one live reply still costs all of its bytes, and
    /// <see cref="CacheOptions.MaxBytes"/> is a claim about memory, so that is what it is charged.
    /// </para>
    /// </remarks>
    internal sealed partial class RespClientCache
    {
        private const int LargestSlab = 1 << 20;

        // the smallest slab worth having: above the large-object threshold (85,000 bytes), so it is born in the old
        // generation - which is the whole point - rather than being one more mid-life object itself
        private const int SmallestSlab = 128 * 1024;

        // a budget is spread over at least this many slabs, so that freeing the oldest is a fraction of it
        private const int SlabsPerBudget = 32;

        /// <summary>The size of this cache's slabs, or zero for none: every reply gets an array of its own.</summary>
        private readonly int _slabSize;

        /// <summary>
        /// A slab size to suit the budget: one slab must be a small fraction of it, or the current slab alone - which
        /// is never evicted - could exceed it. Below <see cref="SmallestSlab"/> x <see cref="SlabsPerBudget"/> (4 MiB),
        /// none at all: a cache that small is not where GC churn matters, and it keeps exact per-entry accounting.
        /// </summary>
        private static int ChooseSlabSize(long? maxBytes)
        {
            if (maxBytes is not long budget) return LargestSlab;
            if (budget < (long)SmallestSlab * SlabsPerBudget) return 0;
            return (int)Math.Min(LargestSlab, budget / SlabsPerBudget);
        }

        /// <summary>Replies larger than this get an array of their own rather than most of a slab.</summary>
        private int MaxSlabbedReply => _slabSize / 8;

        private readonly object _slabSync = new();
        private readonly Queue<Slab> _sealedSlabs = new(); // oldest first; guarded by _slabSync
        private Slab? _currentSlab; // guarded by _slabSync
        private int _slabOffset; // guarded by _slabSync
        private long _slabBytes;

        /// <summary>One slab: an array, the wrapper payloads share, and the keys stored in it, for eviction.</summary>
        private sealed class Slab
        {
            internal Slab(int size)
            {
                // sized for ~1 KiB replies, so the list is not regrown and recopied ~10 times per slab; smaller replies
                // still grow it, but from a sensible start
                Keys = new List<EntryKey>(Math.Max(16, size / 1024));
#if NET
                Buffer = GC.AllocateUninitializedArray<byte>(size);
#else
                Buffer = new byte[size];
#endif
                Lease = RefCountedBuffer.CreateFixed(Buffer);
            }

            internal byte[] Buffer { get; }

            internal RefCountedBuffer Lease { get; }

            /// <summary>Every key stored here, in order; appended under the allocation lock, read once sealed.</summary>
            internal List<EntryKey> Keys { get; }

            // one per entry stored here, plus one for the allocator while this is the current slab
            private int _holds = 1;

            internal void Hold() => Interlocked.Increment(ref _holds);

            /// <summary>Drop one hold; true for the one call that dropped the last.</summary>
            internal bool Release() => Interlocked.Decrement(ref _holds) == 0;

            internal bool IsDead => Volatile.Read(ref _holds) == 0;
        }

        /// <summary>Copy a request and its reply into the current slab, side by side - or arrays of their own if large.</summary>
        /// <param name="request">The request, which becomes the cache key.</param>
        /// <param name="database">The database it ran against.</param>
        /// <param name="reply">The reply to keep; the caller holds it for the duration.</param>
        /// <param name="key">The key to store the entry under, over the copied request.</param>
        /// <param name="slab">The slab they went into, or <see langword="null"/> for arrays of their own.</param>
        /// <remarks>
        /// The key goes in the slab too, and not only to save an object: for a small reply - a count, a flag - the key is
        /// the bigger of the two, and this puts them in one place, where a lookup that finds the key is already beside
        /// the reply it wants.
        /// </remarks>
        private RespPayload StoreReply(in RespRequest request, int database, ReadOnlySpan<byte> reply, out EntryKey key, out Slab? slab)
        {
            var keyLength = request.Span.Length;
            var total = keyLength + reply.Length;
            if (_slabSize == 0 || total > MaxSlabbedReply)
            {
                slab = null;
                key = new EntryKey(request.CopyForCacheKey(), database);
                return RespPayload.CreateOwned(reply);
            }

            Slab? sealedNow = null;
            int offset;
            lock (_slabSync)
            {
                if (_currentSlab is null || _slabSize - _slabOffset < total)
                {
                    if (_currentSlab is { } full)
                    {
                        _sealedSlabs.Enqueue(full);
                        sealedNow = full;
                    }

                    _currentSlab = new Slab(_slabSize);
                    _slabOffset = 0;
                    Interlocked.Add(ref _slabBytes, _slabSize);
                }

                slab = _currentSlab;
                offset = _slabOffset;
                _slabOffset += total;
                slab.Hold();

                // the key is copied under the lock - a few dozen bytes - because the slab's list of keys, which
                // evicting it walks, is appended to here and needs the finished key
                key = new EntryKey(request.CopyForCacheKey(slab.Buffer, offset), database);
                slab.Keys.Add(key);
            }

            // outside the lock: the range is ours alone, and nobody can see it until the entry is published
            reply.CopyTo(slab.Buffer.AsSpan(offset + keyLength));
            if (sealedNow is not null) ReleaseSlab(sealedNow); // the allocator's hold on the slab it just sealed
            return new RespPayload(slab.Lease, offset + keyLength, reply.Length);
        }

        /// <summary>Drop one hold on a slab, crediting the budget when it was the last.</summary>
        private void ReleaseSlab(Slab slab)
        {
            if (slab.Release()) Interlocked.Add(ref _slabBytes, -slab.Buffer.Length);
        }

        /// <summary>Drop the allocator's hold on the current slab, so that it goes once its entries do; for disposal.</summary>
        private void ReleaseCurrentSlab()
        {
            Slab? current;
            lock (_slabSync)
            {
                current = _currentSlab;
                _currentSlab = null;
            }

            if (current is not null) ReleaseSlab(current);
        }

        private int _evictingSlabs;

        /// <summary>
        /// The most slabs one call to <see cref="EvictSlabsToBudget"/> will take before handing the rest to the next
        /// store. Bounds the work of a store that finds a run of hot slabs: each one is mostly copied forward rather
        /// than freed, and without a bound the worst case - everything hot - copies the whole cache in one call.
        /// </summary>
        private const int MaxSlabsPerEviction = 4;

        /// <summary>Evict the oldest slabs until the budget is met, one evictor at a time.</summary>
        /// <remarks>
        /// <para>
        /// One at a time because every store that finds the cache over budget comes here, and under concurrency they
        /// all found it over budget by the same one slab: each evicting "the oldest" took several slabs where one was
        /// due, and a 32 MiB cache held ~11k 1 KiB entries instead of ~31k. A store that finds eviction already
        /// underway leaves it to the evictor - briefly over budget, as the remarks on <see cref="EvictToBudget"/> allow.
        /// </para>
        /// <para>
        /// Bounded by <see cref="MaxSlabsPerEviction"/> for the same reason: a slab whose entries were all read is
        /// copied forward, not freed, so a run of them frees little, and the budget is a target the next store
        /// continues towards rather than something one store must reach however much it costs.
        /// </para>
        /// </remarks>
        /// <returns>
        /// Whether the bytes are now this caller's to account for: <see langword="true"/> if it evicted to the
        /// budget or ran out of slabs; <see langword="false"/> if another thread was already evicting, or this one
        /// stopped at the bound with work still to do.
        /// </returns>
        private bool EvictSlabsToBudget(long maxBytes, ref int evicted)
        {
            if (Interlocked.CompareExchange(ref _evictingSlabs, 1, 0) != 0) return false;
            try
            {
                var slabs = 0;
                while (Bytes > maxBytes)
                {
                    if (slabs++ == MaxSlabsPerEviction) return false; // enough for one store; the next one resumes
                    if (!TryEvictOldestSlab(ref evicted)) break;
                }

                return true;
            }
            finally
            {
                Volatile.Write(ref _evictingSlabs, 0);
            }
        }

        /// <summary>
        /// Take the oldest sealed slab: every entry still living in it is evicted, except those read since they
        /// were stored there, which are copied forward into the current slab.
        /// </summary>
        /// <param name="evicted">Incremented once per entry removed.</param>
        /// <returns>Whether there was a slab to evict.</returns>
        /// <remarks>
        /// <para>
        /// <b>The slab path's Clock.</b> A slab is a cohort by fill time, so evicting it whole made a hot entry's
        /// survival depend on the age of its neighbours. Carrying forward the entries whose flag is set
        /// (<see cref="Entry.TryClearReferenced"/>) makes it depend on whether the entry itself was used, which is
        /// the LRU-shaped answer, at the cost of copying the hot part of each slab once per cycle around the
        /// budget - on the eviction path, not the hit path. The flag is cleared by the move, so an entry has to be
        /// read again before its new slab reaches the head to be carried forward again.
        /// </para>
        /// <para>
        /// An entry past its lifetime is not carried forward however often it was read: it can never be served
        /// again, so copying it would only defer its release to the next cycle.
        /// </para>
        /// </remarks>
        private bool TryEvictOldestSlab(ref int evicted)
        {
            Slab oldest;
            lock (_slabSync)
            {
                while (_sealedSlabs.Count != 0 && _sealedSlabs.Peek().IsDead) _sealedSlabs.Dequeue();
                if (_sealedSlabs.Count == 0) return false;
                oldest = _sealedSlabs.Dequeue();
            }

            var lifetime = Policy.TimeToLiveTicks;
            var entries = (ICollection<KeyValuePair<EntryKey, Entry>>)_entries;
            foreach (var key in oldest.Keys)
            {
                // the key may since have been refilled into a newer slab, or removed altogether: only an entry that
                // still lives HERE is evicted, and only if it is still the one we looked at
                if (!_entries.TryGetValue(key, out var entry) || !ReferenceEquals(entry.Slab, oldest)) continue;

                if (entry.IsValid && !CachePolicy.IsOlderThan(entry.FilledAt, lifetime) && entry.TryClearReferenced())
                {
                    Promote(key, entry, oldest, ref evicted);
                }
                else if (entries.Remove(new KeyValuePair<EntryKey, Entry>(key, entry)))
                {
                    Release(entry);
                    evicted++;
                }
            }

            return true;
        }

        /// <summary>Move an entry from the slab being evicted into the current slab.</summary>
        /// <param name="key">The entry's key, as stored in <paramref name="from"/>.</param>
        /// <param name="entry">The entry as it was found; the move only happens if it is still there.</param>
        /// <param name="from">The slab being evicted, which the entry lives in.</param>
        /// <param name="evicted">Incremented if the entry ended up removed rather than moved.</param>
        /// <remarks>
        /// <para>
        /// <b>Copied first, then removed and re-added</b> - not updated in place, which is what a refresh does. The
        /// dictionary keeps the key object it already has, and this key's bytes live in the slab being evicted:
        /// updating in place would keep that slab's array reachable for as long as the entry stayed hot, which for
        /// a hot key is for ever, and the budget would be counting memory it had not actually freed. Removing and
        /// re-adding puts the new copy's key in the dictionary. The gap between the two is a miss for a concurrent
        /// reader, and if a fill lands in it theirs is as good as ours.
        /// </para>
        /// <para>
        /// The copy always lands in a slab: it fitted one before and the slab size does not change. The dependencies
        /// and the fill time travel with it, so it is exactly as valid and exactly as old as it was.
        /// </para>
        /// </remarks>
        private void Promote(in EntryKey key, Entry entry, Slab from, ref int evicted)
        {
            var payload = StoreReply(entry.Key, key.Database, entry.Payload.Span, out var newKey, out var slab);
            var moved = entry.CopyForward(payload, newKey.Frame, slab);

            var entries = (ICollection<KeyValuePair<EntryKey, Entry>>)_entries;
            if (!entries.Remove(new KeyValuePair<EntryKey, Entry>(key, entry)))
            {
                // gone, or replaced, since we looked: nothing to move, and our copy is surplus. The space it took
                // stays used until its slab goes, as for a redundant fill
                if (slab is not null) ReleaseSlab(slab);
                return;
            }

            if (_entries.TryAdd(newKey, moved))
            {
                Interlocked.Add(ref _bytes, moved.Bytes - entry.Bytes);
                entry.Payload.Dispose();
                ReleaseSlab(from);
                entry.Key.Dispose();
                Interlocked.Increment(ref _promoted);
                return;
            }

            // a fill re-added the key between the remove and the add; theirs is as good as ours, and the old entry
            // is simply gone
            Release(entry);
            if (slab is not null) ReleaseSlab(slab);
            evicted++;
        }
    }
}
