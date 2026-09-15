using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. "Table 2": Redis key bytes to a generation ticket, the only structure a server
    /// invalidation touches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tickets are global and monotonic, not per-key counters.</b> That is what makes removal and reuse
    /// safe. A per-key counter restarting at zero could collide with a ticket a cached entry recorded before
    /// the key was invalidated, and the entry would validate against a key that had in fact changed. A
    /// <c>long</c> incremented globally cannot repeat: at 10^9 a second it lasts about 292 years.
    /// </para>
    /// <para>
    /// <b>Invalidation stamps the node, it does not merely remove it.</b> Cache entries hold the
    /// <see cref="Node"/> directly, so that validating a cache hit is a pointer dereference and a
    /// <c>long</c> compare - no hashing, no second lookup. The cost of that is an invariant: a node that
    /// leaves this table must be stamped invalid FIRST, or entries still referencing it would never learn
    /// and would serve stale data forever. Everything that removes here goes through
    /// <c>Node.Invalidate</c> on the way out.
    /// </para>
    /// <para>
    /// <b>Lookups are lock-free and allocation-free</b>, because in broadcasting mode this is fed every key
    /// touched on the server, and almost none of them will be cached here. A probe is: hash the span, read
    /// the bucket array, read one bucket, compare. Buckets are copy-on-write arrays rather than linked
    /// nodes, so a reader never walks a chain that a writer is re-linking, and growth can rebuild the bucket
    /// array without disturbing nodes that cache entries point at.
    /// </para>
    /// </remarks>
    internal sealed class RespKeyTable
    {
        /// <summary>A generation that can never be handed out, meaning "this key has been invalidated".</summary>
        internal const long Invalid = 0;

        private static long _ticket;

        /// <summary>The next global generation ticket; never zero, never repeated.</summary>
        internal static long NextTicket() => Interlocked.Increment(ref _ticket);

        private readonly object[] _locks;
        private Node[]?[] _buckets;
        private int _count;

        internal RespKeyTable(int capacity = 256)
        {
            var size = 1;
            while (size < capacity) size <<= 1;
            _buckets = new Node[]?[size];
            _locks = new object[Math.Min(size, 32)];
            for (var i = 0; i < _locks.Length; i++) _locks[i] = new object();
        }

        /// <summary>The number of keys currently tracked.</summary>
        internal int Count => Volatile.Read(ref _count);

        /// <summary>One tracked key and its current generation.</summary>
        internal sealed class Node
        {
            private long _generation;
            private long _localWriteAt;
            private long _invalidatedAt;

            internal Node(byte[] key, int hash, long generation)
            {
                Key = key;
                Hash = hash;
                _generation = generation;
            }

            internal byte[] Key { get; }

            internal int Hash { get; }

            /// <summary>The current generation, or <see cref="Invalid"/> once the key has been invalidated.</summary>
            internal long Generation => Volatile.Read(ref _generation);

            /// <summary>Mark the key invalidated; every entry that recorded a generation now fails to validate.</summary>
            internal void Invalidate() => Invalidate(stampTime: false);

            /// <summary>Mark the key invalidated, optionally recording when.</summary>
            /// <param name="stampTime">
            /// Whether to record <i>when</i> this happened, for a grace period that runs from the
            /// invalidation.
            /// </param>
            /// <remarks>
            /// Optional because this is the hot path: under <c>BCAST</c> the server names every key anybody
            /// modifies, and the overwhelming majority are keys we do not hold. Reading a timestamp there
            /// would be paid on all of them to benefit the few. So the cost lands only on a cache that has
            /// actually asked for a grace period - see <see cref="CachePolicy.InvalidationGracePeriod"/>.
            /// <para>
            /// Stamped <b>before</b> the generation is cleared, so a reader that sees the node invalid can
            /// rely on the timestamp already being there.
            /// </para>
            /// </remarks>
            internal void Invalidate(bool stampTime)
            {
                if (stampTime) Volatile.Write(ref _invalidatedAt, Stopwatch.GetTimestamp());
                Volatile.Write(ref _generation, Invalid);
            }

            /// <summary>When this key was last invalidated, if anybody asked for that to be recorded.</summary>
            internal long InvalidatedAt => Volatile.Read(ref _invalidatedAt);

            /// <summary>
            /// The ticket current when <b>this process</b> last wrote this key, or <see cref="Invalid"/>.
            /// </summary>
            /// <remarks>
            /// Tickets are globally monotonic, so comparing this against the generation an entry recorded
            /// answers "did we write this key after that entry was filled?" without storing a timestamp or
            /// walking anything. It exists solely to keep <i>our own</i> writes out of any
            /// serve-stale-anyway behaviour: that is read-your-own-writes, and it is reported as corruption
            /// rather than as staleness. See design notes 6.15.
            /// </remarks>
            internal long LocalWriteAt => Volatile.Read(ref _localWriteAt);

            /// <summary>
            /// Mark the key invalidated <b>by us</b>, which is a stronger statement than an invalidation
            /// arriving from the server.
            /// </summary>
            /// <remarks>
            /// Stamped before the invalidation, so a reader that sees the node invalid can trust that this
            /// has already been set if it was going to be. Monotonic, so a later server invalidation cannot
            /// erase the fact that we wrote it.
            /// </remarks>
            internal void InvalidateLocal(bool stampTime)
            {
                var ticket = NextTicket();
                while (true)
                {
                    var current = Volatile.Read(ref _localWriteAt);
                    if (current >= ticket) break;
                    if (Interlocked.CompareExchange(ref _localWriteAt, ticket, current) == current) break;
                }

                Invalidate(stampTime);
            }

            /// <summary>
            /// The generation to record for a fill starting now, reviving the node with a fresh ticket if it
            /// had been invalidated.
            /// </summary>
            /// <remarks>
            /// A fresh ticket cannot revive entries that recorded the old one, because tickets never repeat.
            /// Two fills racing here may burn a ticket; the loser simply fails to validate later, which
            /// costs a miss and nothing else.
            /// </remarks>
            internal long EnsureLive()
            {
                while (true)
                {
                    var current = Volatile.Read(ref _generation);
                    if (current != Invalid) return current;

                    var ticket = NextTicket();
                    if (Interlocked.CompareExchange(ref _generation, ticket, Invalid) == Invalid) return ticket;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashOf(ReadOnlySpan<byte> key) => RedisValue.GetHashCode(key);

        /// <summary>
        /// Find the node for a key, without allocating. This is the hot path for invalidation traffic.
        /// </summary>
        internal Node? Find(ReadOnlySpan<byte> key)
        {
            var hash = HashOf(key);
            var buckets = Volatile.Read(ref _buckets);
            var bucket = Volatile.Read(ref buckets[(hash & int.MaxValue) & (buckets.Length - 1)]);
            if (bucket is null) return null;

            foreach (var node in bucket)
            {
                if (node.Hash == hash && node.Key.AsSpan().SequenceEqual(key)) return node;
            }

            return null;
        }

        /// <summary>
        /// Invalidate a key. Allocation-free, and cheap when the key is not tracked - which, under
        /// broadcasting, is almost every call.
        /// </summary>
        /// <returns><c>true</c> if the key was tracked, so callers can count how much of the flood mattered.</returns>
        internal bool Invalidate(ReadOnlySpan<byte> key) => Invalidate(key, local: false, stampTime: false);

        /// <inheritdoc cref="Invalidate(ReadOnlySpan{byte})"/>
        /// <param name="key">The key that changed.</param>
        /// <param name="local">
        /// <c>true</c> if <b>this process</b> made the change. A local write is a fact, not a race, so an
        /// entry it invalidates must never be served afterwards.
        /// </param>
        /// <param name="stampTime">Whether to record when this happened, for a grace period.</param>
        internal bool Invalidate(ReadOnlySpan<byte> key, bool local, bool stampTime)
        {
            var node = Find(key);
            if (node is null) return false;

            if (local)
            {
                node.InvalidateLocal(stampTime);
            }
            else
            {
                node.Invalidate(stampTime);
            }

            return true;
        }

        /// <summary>Invalidate everything: a null invalidation (FLUSHALL/FLUSHDB), or a lost connection.</summary>
        internal void InvalidateAll()
        {
            AcquireAll();
            try
            {
                foreach (var bucket in _buckets)
                {
                    if (bucket is null) continue;
                    foreach (var node in bucket) node.Invalidate(); // stamp before dropping; see the type remarks
                }

                _buckets = new Node[]?[_buckets.Length];
                Volatile.Write(ref _count, 0);
            }
            finally
            {
                ReleaseAll();
            }
        }

        /// <summary>
        /// Get - creating if needed - the node for a key, and the generation a fill starting now should
        /// record against it.
        /// </summary>
        internal Node GetOrAdd(ReadOnlySpan<byte> key, out long generation)
        {
            var existing = Find(key);
            if (existing is not null)
            {
                generation = existing.EnsureLive();
                return existing;
            }

            var hash = HashOf(key);
            var lockObj = _locks[(hash & int.MaxValue) % _locks.Length];
            Node node;
            Node[]?[] witnessed;
            bool crowded;
            lock (lockObj)
            {
                // re-check: another thread may have added it while we were outside the lock
                var raced = Find(key);
                if (raced is not null)
                {
                    generation = raced.EnsureLive();
                    return raced;
                }

                generation = NextTicket();
                node = new Node(key.ToArray(), hash, generation);
                witnessed = _buckets;
                crowded = Insert(node);
            }

            // NOT inside the stripe lock. Growing takes every lock, and a thread holding one stripe while
            // waiting for the rest deadlocks against another doing the same from a different stripe.
            if (crowded) Grow(witnessed);
            return node;
        }

        // caller holds the stripe lock for this node's hash; returns whether the table wants growing
        private bool Insert(Node node)
        {
            var buckets = _buckets;
            var index = (node.Hash & int.MaxValue) & (buckets.Length - 1);
            var bucket = buckets[index];

            // copy-on-write: readers keep walking the old array, which never changes under them
            Node[] updated;
            if (bucket is null)
            {
                updated = [node];
            }
            else
            {
                updated = new Node[bucket.Length + 1];
                Array.Copy(bucket, updated, bucket.Length);
                updated[bucket.Length] = node;
            }

            Volatile.Write(ref buckets[index], updated);
            return Interlocked.Increment(ref _count) > buckets.Length * 2;
        }

        private void Grow(Node[]?[] witnessed)
        {
            AcquireAll();
            try
            {
                if (!ReferenceEquals(_buckets, witnessed)) return;

                var grown = new Node[]?[witnessed.Length << 1];
                foreach (var bucket in witnessed)
                {
                    if (bucket is null) continue;
                    foreach (var node in bucket)
                    {
                        var index = (node.Hash & int.MaxValue) & (grown.Length - 1);
                        var target = grown[index];
                        if (target is null)
                        {
                            grown[index] = [node];
                        }
                        else
                        {
                            var updated = new Node[target.Length + 1];
                            Array.Copy(target, updated, target.Length);
                            updated[target.Length] = node;
                            grown[index] = updated;
                        }
                    }
                }

                // nodes are carried over BY REFERENCE: cache entries point at them, so they must survive
                Volatile.Write(ref _buckets, grown);
            }
            finally
            {
                ReleaseAll();
            }
        }

        private void AcquireAll()
        {
            foreach (var lockObj in _locks) Monitor.Enter(lockObj);
        }

        private void ReleaseAll()
        {
            for (var i = _locks.Length - 1; i >= 0; i--) Monitor.Exit(_locks[i]);
        }
    }
}
