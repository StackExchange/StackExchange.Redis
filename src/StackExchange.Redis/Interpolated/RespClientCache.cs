using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. A client-side cache built as two independent lookups rather than a cross-indexed
    /// structure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Table 1</b> (here) maps <c>(rendered frame, database)</c> to a payload plus the generations its
    /// keys had when the request was sent. <b>Table 2</b> (<see cref="RespKeyTable"/>) maps Redis key bytes
    /// to a generation. A server invalidation touches <i>only</i> table 2, so it costs one hash and one
    /// stamp per key and never enumerates cache entries - which is the whole point, since under broadcasting
    /// we are told about every key touched on the server, and almost none of them are ours.
    /// </para>
    /// <para>
    /// A hit is valid when every key it depends on still carries the generation recorded at send time.
    /// Entries hold the <see cref="RespKeyTable.Node"/> directly, so validating is a dereference and a
    /// compare - table 2 is not re-hashed on the hot path.
    /// </para>
    /// <para>
    /// <b>Note the deliberate asymmetry:</b> table 1 is keyed by frame AND database; table 2 by key bytes
    /// alone, with no database. That mirrors the protocol - Redis tracking uses "a single keys namespace,
    /// not divided by database numbers", so writing <c>foo</c> in database 3 invalidates a cached
    /// <c>foo</c> in database 2. It looks like an oversight and is not.
    /// </para>
    /// <para>
    /// Failure is always closed. A key that cannot be resolved, a frame whose keys cannot be enumerated, an
    /// entry whose generation no longer matches: all are treated as misses. The only way to serve stale data
    /// would be for an invalidation to leave a live node unstamped, which is why nodes are stamped before
    /// they are ever dropped from table 2.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public sealed class RespClientCache : IDisposable
    {
        private readonly ConcurrentDictionary<EntryKey, Entry> _entries = new();
        private readonly RespKeyTable _keys;
        private long _stored;
        private long _refusedByFlags;
        private long _refusedNoKeys;
        private long _refusedRaced;
        private long _redundantFills;

        /// <summary>Create a cache.</summary>
        /// <param name="keyCapacity">Initial size hint for the tracked-key table.</param>
        public RespClientCache(int keyCapacity = 256) => _keys = new RespKeyTable(keyCapacity);

        /// <summary>The number of cached responses, including any not yet swept after invalidation.</summary>
        public int Count => _entries.Count;

        /// <summary>The number of distinct keys being tracked.</summary>
        public int TrackedKeyCount => _keys.Count;

        /// <summary>Fills that were stored.</summary>
        /// <remarks>
        /// These counters and the ones below are incremented only on the fill path - once per cache miss,
        /// which has already paid for a round trip - so they cost nothing on a hit. They exist because the
        /// failure mode this design can still produce is silent and durable: a command cached that should
        /// not have been serves stale data forever, with no error and no log. "Why is this stale?" and "why
        /// is nothing being cached?" should both be answerable without a debugger.
        /// </remarks>
        public long Stored => Volatile.Read(ref _stored);

        /// <summary>Fills refused because the flags did not permit caching.</summary>
        /// <remarks>
        /// The usual cause is a command that never declared a retry category - which is uncacheable by
        /// design, since undeclared cannot mean "safe". A surprisingly high count here usually means an
        /// external command surface is not declaring categories.
        /// </remarks>
        public long RefusedByFlags => Volatile.Read(ref _refusedByFlags);

        /// <summary>Fills refused because the request named no keys, so nothing could ever invalidate it.</summary>
        public long RefusedNoKeys => Volatile.Read(ref _refusedNoKeys);

        /// <summary>Fills refused because an invalidation landed while the command was in flight.</summary>
        public long RefusedRaced => Volatile.Read(ref _refusedRaced);

        /// <summary>
        /// Fills that completed only to find the same request already cached by someone else - i.e. two or
        /// more callers missed on the same request concurrently and all of them went to the server.
        /// </summary>
        /// <remarks>
        /// This is the stampede signal, and it is measured rather than assumed because request combining is
        /// real complexity and the economics here are not HybridCache's: a miss is a round trip on an
        /// already-multiplexed connection, not an arbitrary factory call. Expect it to be near zero for
        /// ordinary traffic and to spike after a flush, since dropping the cache on disconnect makes every
        /// hot key re-fetch at once. See the design notes, section 6.11.
        /// </remarks>
        public long RedundantFills => Volatile.Read(ref _redundantFills);

        /// <summary>
        /// Invalidate one key, as reported by the server. Allocation-free, and cheap when the key is not
        /// cached here.
        /// </summary>
        /// <param name="key">The key bytes exactly as the server reported them.</param>
        /// <returns><c>true</c> if the key was actually tracked.</returns>
        /// <remarks>
        /// Takes a span rather than anything owned, because in broadcasting mode this is called for every
        /// key touched on the server. The work is: hash the span, one array read, one bucket scan. Nothing
        /// is allocated, and a key we do not track costs only that.
        /// </remarks>
        public bool OnInvalidate(ReadOnlySpan<byte> key) => _keys.Invalidate(key);

        /// <summary>
        /// Invalidate everything - a null invalidation (<c>FLUSHALL</c>/<c>FLUSHDB</c>), a lost connection,
        /// or <c>tracking-redir-broken</c>.
        /// </summary>
        /// <remarks>
        /// Stamps every tracked key rather than walking the cache, so entries fail validation on their next
        /// lookup and the memory is reclaimed by <see cref="Sweep"/>.
        /// </remarks>
        public void OnFlush() => _keys.InvalidateAll();

        /// <summary>
        /// Look for a cached response. On success the payload is returned <b>retained</b> - release it when
        /// the parse is done.
        /// </summary>
        public bool TryGet(in RespRequest frame, int database, [NotNullWhen(true)] out RespPayload? payload)
        {
            if (_entries.TryGetValue(new EntryKey(frame, database), out var entry)
                && entry.IsValid
                && entry.Payload.TryRetain())
            {
                // re-check after retaining: an invalidation between the check and the retain would otherwise
                // let one stale read through the door it had already closed
                if (entry.IsValid)
                {
                    payload = entry.Payload;
                    return true;
                }

                entry.Payload.Release();
            }

            payload = null;
            return false;
        }

        /// <summary>
        /// Begin a fill, capturing the generations of the frame's keys. <b>Call this before sending the
        /// command</b>, not when the reply arrives.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what closes the race the Redis docs describe: an invalidation can arrive between the send
        /// and the reply, and the server will not tell us again, because it dropped the key from its
        /// invalidation table when it fired. Caching that reply would leave permanently stale data. By
        /// recording generations at send time, <c>TryComplete</c> can see that the world moved.
        /// </para>
        /// <para>
        /// Returns <c>false</c> - refusing to cache - when the frame cannot report its keys. That is now only
        /// the case for a key at argument index above 62, which the frame's bitmap has no bit for. Refusing
        /// is the safe answer: an entry whose keys cannot be named could never be invalidated.
        /// </para>
        /// </remarks>
        public bool TryBeginFill(ref RespFrame frame, int database, out RespFill fill)
            => TryBeginFill(ref frame, database, CommandFlags.CommandRetryReadOnly, out fill);

        /// <inheritdoc cref="TryBeginFill(ref RespFrame, int, out RespFill)"/>
        /// <param name="frame">The rendered request.</param>
        /// <param name="database">The database the request runs against.</param>
        /// <param name="flags">
        /// The command's flags. Caching requires a retry category that is <b>set</b> and no more severe than
        /// <see cref="CommandFlags.CommandRetryReadOnly"/>.
        /// </param>
        /// <param name="fill">The fill to complete once the reply arrives.</param>
        /// <remarks>
        /// <para>
        /// <b>Unset is not cacheable.</b> The retry category region is zero when nobody declared one, and
        /// zero compares below <see cref="CommandFlags.CommandRetryReadOnly"/> on the severity ladder - so a
        /// naive <c>&lt;=</c> test would treat "nobody said" as "safe to cache", which is precisely backwards
        /// for commands this library does not know. External surfaces such as NRedisStack reach the server
        /// through <c>Execute</c>, and <c>Message.UserSelectableFlags</c> already lets them declare a
        /// category; declaring nothing must mean no caching.
        /// </para>
        /// <para>
        /// Read-only is <b>necessary but not sufficient</b>, which is why this is a gate rather than the
        /// whole test. Plenty of read-only commands must not be cached - non-deterministic ones
        /// (<c>SRANDMEMBER</c>, <c>HRANDFIELD</c>, <c>ZRANDMEMBER</c>), cursor-based ones (<c>SCAN</c>,
        /// <c>HSCAN</c>), and anything the server does not track for invalidation, which per the Redis docs
        /// includes the whole <c>FT.*</c> family. The keyless rule below catches some of these for free; the
        /// rest need an explicit opt-in that this spike does not yet model.
        /// </para>
        /// </remarks>
        public bool TryBeginFill(ref RespFrame frame, int database, CommandFlags flags, out RespFill fill)
        {
            if (!IsCacheable(flags))
            {
                Interlocked.Increment(ref _refusedByFlags);
                fill = default;
                return false;
            }

            var keyCount = frame.KeyCount;
            if (keyCount <= 0)
            {
                // keyCount < 0: keys not enumerable => not invalidatable.
                // keyCount == 0: NOTHING can ever invalidate this. Server-assisted invalidation only ever
                // reports keys, so an entry with no dependencies is vacuously valid forever - not even a
                // flush clears it, because OnFlush stamps key nodes and there are none. Permanent staleness.
                Interlocked.Increment(ref _refusedNoKeys);
                fill = default;
                return false;
            }

            // the overwhelming majority of commands are well under this; only a huge multi-key command
            // heaps, and that one already paid for a round trip
            Span<KeyRange> ranges = keyCount <= 16 ? stackalloc KeyRange[16] : new KeyRange[keyCount];
            var count = frame.TryGetKeys(ranges);
            if (count < 0)
            {
                fill = default;
                return false;
            }

            var deps = count == 0 ? [] : new Dependency[count];
            for (var i = 0; i < count; i++)
            {
                var node = _keys.GetOrAdd(frame.GetKey(ranges[i]), out var generation);
                deps[i] = new Dependency(node, generation);
            }

            fill = new RespFill(frame.Detach(), database, deps);
            return true;
        }

        /// <summary>
        /// Complete a fill, storing the response only if nothing it depends on was invalidated while the
        /// command was in flight.
        /// </summary>
        /// <returns><c>false</c> if the fill was abandoned; the response must not be cached.</returns>
        /// <summary>
        /// Complete a fill, storing the reply only if nothing it depends on was invalidated while the
        /// command was in flight.
        /// </summary>
        /// <param name="fill">The fill begun before the send.</param>
        /// <param name="response">The reply. The cache takes its OWN reference if it stores it; the caller
        /// still releases theirs.</param>
        /// <returns><c>false</c> if the fill was abandoned; the reply was not cached.</returns>
        /// <remarks>
        /// Takes the payload rather than the bytes, so a stored reply is <b>shared with the caller, not
        /// copied</b>. It is already in a pooled, reference-counted buffer; copying it into a second one to
        /// cache it would be pure waste.
        /// </remarks>
        public bool TryComplete(in RespFill fill, RespPayload response)
        {
            if (response is null) throw new ArgumentNullException(nameof(response));
            if (fill.Key.IsEmpty) return false;

            if (!Dependency.AllValid(fill.Dependencies))
            {
                Interlocked.Increment(ref _refusedRaced);
                fill.Key.Dispose();
                return false;
            }

            if (!fill.Key.TryRetain(out var stored))
            {
                fill.Key.Dispose();
                return false;
            }

            if (!response.TryRetain())
            {
                // the reply is already going back to the pool; nothing to cache
                stored.Dispose();
                fill.Key.Dispose();
                return false;
            }

            if (_entries.TryAdd(new EntryKey(stored, fill.Database), new Entry(response, fill.Dependencies)))
            {
                fill.Key.Dispose(); // the dictionary holds its own references now
                Interlocked.Increment(ref _stored);
                return true;
            }

            // somebody else filled the same request first; theirs is as good as ours
            Interlocked.Increment(ref _redundantFills);
            response.Release();
            stored.Dispose();
            fill.Key.Dispose();
            return false;
        }

        /// <summary>
        /// Drop entries that no longer validate, releasing their payloads and keys.
        /// </summary>
        /// <returns>The number of entries removed.</returns>
        /// <remarks>
        /// Invalidation deliberately does no work beyond stamping a generation, so this is where the memory
        /// actually comes back. It is O(entries) and belongs on a timer, not on the invalidation path.
        /// </remarks>
        public int Sweep()
        {
            var removed = 0;
            foreach (var pair in _entries)
            {
                if (pair.Value.IsValid) continue;
                if (_entries.TryRemove(pair.Key, out var entry))
                {
                    entry.Payload.Dispose();
                    pair.Key.Frame.Dispose();
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>Release every cached payload and key.</summary>
        public void Dispose()
        {
            foreach (var pair in _entries)
            {
                if (!_entries.TryRemove(pair.Key, out var entry)) continue;
                entry.Payload.Dispose();
                pair.Key.Frame.Dispose();
            }

            _keys.InvalidateAll();
        }

        /// <summary>
        /// Whether the command's retry category permits caching at all.
        /// </summary>
        /// <remarks>
        /// The category is a 5-bit severity ladder where zero means "nobody declared one". Both halves of
        /// this test matter: <c>!= 0</c> rejects the undeclared case, and <c>&lt;=</c> uses the ladder the
        /// flags were built to support, so anything at or beyond a write - or server-admin - is out.
        /// </remarks>
        internal static bool IsCacheable(CommandFlags flags)
        {
            if ((flags & CommandFlags.NoClientCache) != 0) return false;

            var category = flags & Message.MaskRetryCategory;
            return category != 0 && category <= CommandFlags.CommandRetryReadOnly;
        }

        /// <summary>One key a cached entry depends on, and the generation it had when the request was sent.</summary>
        internal readonly struct Dependency(RespKeyTable.Node node, long generation)
        {
            private readonly RespKeyTable.Node _node = node;
            private readonly long _generation = generation;

            // a dereference and a compare - no hashing, no lookup in table 2
            internal bool IsValid => _node.Generation == _generation;

            internal static bool AllValid(Dependency[] dependencies)
            {
                foreach (var dependency in dependencies)
                {
                    if (!dependency.IsValid) return false;
                }

                return true;
            }
        }

        private sealed class Entry(RespPayload payload, Dependency[] dependencies)
        {
            internal RespPayload Payload { get; } = payload;

            internal bool IsValid => Dependency.AllValid(dependencies);
        }

        /// <summary>The frame AND the database; see the note on database asymmetry in the type remarks.</summary>
        private readonly struct EntryKey(RespRequest frame, int database) : IEquatable<EntryKey>
        {
            internal RespRequest Frame { get; } = frame;

            private int Database { get; } = database;

            public bool Equals(EntryKey other) => Database == other.Database && Frame.Equals(other.Frame);

            public override bool Equals(object? obj) => obj is EntryKey other && Equals(other);

            public override int GetHashCode() => (Frame.GetHashCode() * 397) ^ Database;
        }

        /// <summary>An in-flight fill: the frame that will become the cache key, and what it depends on.</summary>
        [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
        public readonly struct RespFill
        {
            internal RespFill(RespRequest key, int database, Dependency[] dependencies)
            {
                Key = key;
                Database = database;
                Dependencies = dependencies;
            }

            internal RespRequest Key { get; }

            internal int Database { get; }

            internal Dependency[] Dependencies { get; }

            /// <summary>Abandon the fill without caching anything.</summary>
            public void Abandon() => Key.Dispose();
        }
    }
}
