using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>Issue a request and return the raw response bytes.</summary>
    /// <typeparam name="TState">Caller state, so the callback need not close over anything.</typeparam>
    /// <param name="state">The caller state.</param>
    /// <param name="request">The rendered request frame.</param>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public delegate byte[] RespExecutor<in TState>(TState state, ReadOnlySpan<byte> request);

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

        /// <summary>Create a cache.</summary>
        /// <param name="keyCapacity">Initial size hint for the tracked-key table.</param>
        public RespClientCache(int keyCapacity = 256) => _keys = new RespKeyTable(keyCapacity);

        /// <summary>The number of cached responses, including any not yet swept after invalidation.</summary>
        public int Count => _entries.Count;

        /// <summary>The number of distinct keys being tracked.</summary>
        public int TrackedKeyCount => _keys.Count;

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
        public bool TryGet(in RespCacheKey frame, int database, [NotNullWhen(true)] out RespPayload? payload)
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
        {
            var keyCount = frame.KeyCount;
            if (keyCount < 0)
            {
                fill = default;
                return false; // keys not enumerable => not invalidatable => must not be cached
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
        public bool TryComplete(in RespFill fill, ReadOnlySpan<byte> response)
            => TryComplete(fill, response, out var retained) ? Release(retained) : false;

        private static bool Release(RespPayload payload)
        {
            payload.Release();
            return true;
        }

        /// <summary>
        /// As <see cref="TryComplete(in RespFill, ReadOnlySpan{byte})"/>, also handing back the cached
        /// payload <b>retained</b> so the caller can read it without a second lookup.
        /// </summary>
        public bool TryComplete(in RespFill fill, ReadOnlySpan<byte> response, out RespPayload retained)
        {
            retained = null!;
            if (fill.Key.IsEmpty) return false;

            if (!Dependency.AllValid(fill.Dependencies))
            {
                fill.Key.Dispose();
                return false;
            }

            if (!fill.Key.TryRetain(out var stored))
            {
                fill.Key.Dispose();
                return false;
            }

            var entry = new Entry(RespPayload.Create(response), fill.Dependencies);
            if (_entries.TryAdd(new EntryKey(stored, fill.Database), entry))
            {
                fill.Key.Dispose(); // the dictionary holds its own reference now
                if (entry.Payload.TryRetain())
                {
                    retained = entry.Payload;
                    return true;
                }

                return false; // evicted already; vanishingly unlikely, but it is a miss, not an error
            }

            // somebody else filled the same frame first; theirs is as good as ours
            stored.Dispose();
            entry.Payload.Dispose();
            fill.Key.Dispose();
            return false;
        }

        /// <summary>
        /// Look up, and on a miss execute and cache - with the send-time ordering handled for you.
        /// </summary>
        /// <returns>
        /// The response, <b>retained</b>. Dispose it (or <see cref="RespPayload.Release"/>) when done; a
        /// <c>using</c> does the right thing.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>Prefer this to calling <see cref="TryGet"/> and a separate add.</b> The obvious hand-written
        /// shape - look up, miss, execute, then add - is exactly the unsafe one: an invalidation arriving
        /// while the command is in flight is lost, because by the time the add happens there is nothing left
        /// to compare against. This method captures the generations before it calls
        /// <paramref name="execute"/>, so <c>TryComplete</c> can see that the world moved.
        /// </para>
        /// <para>
        /// A response that arrives after an invalidation is still <b>returned</b> - it is a legitimate answer
        /// for a read that raced a write, and the caller would have got it anyway without a cache - it is
        /// simply not stored.
        /// </para>
        /// <para>
        /// <paramref name="state"/> exists so the callback can be a <c>static</c> lambda and allocate no
        /// closure. Returning <c>byte[]</c> is a spike convenience: the real thing would hand back the
        /// response frame's own lease, as <c>RespResult</c> already does, rather than copying.
        /// </para>
        /// </remarks>
        /// <typeparam name="TState">Caller state, passed to <paramref name="execute"/>.</typeparam>
        /// <param name="frame">The rendered request; its buffer is taken over when the response is cached.</param>
        /// <param name="database">The database the request runs against.</param>
        /// <param name="state">Caller state, so the callback need not close over anything.</param>
        /// <param name="execute">Issues the request when the cache misses.</param>
        public RespPayload GetOrExecute<TState>(
            ref RespFrame frame,
            int database,
            TState state,
            RespExecutor<TState> execute)
        {
            if (execute is null) throw new ArgumentNullException(nameof(execute));

            if (TryGet(frame.AsLookupKey(), database, out var hit)) return hit;

            if (!TryBeginFill(ref frame, database, out var fill))
            {
                // keys not nameable, so not cacheable - but the caller still wants an answer
                return RespPayload.Create(execute(state, frame.Span));
            }

            // the frame's buffer now belongs to the fill, so read the request from there
            var response = execute(state, fill.Key.Span);
            return TryComplete(fill, response, out var stored) ? stored : RespPayload.Create(response);
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
        private readonly struct EntryKey(RespCacheKey frame, int database) : IEquatable<EntryKey>
        {
            internal RespCacheKey Frame { get; } = frame;

            private int Database { get; } = database;

            public bool Equals(EntryKey other) => Database == other.Database && Frame.Equals(other.Frame);

            public override bool Equals(object? obj) => obj is EntryKey other && Equals(other);

            public override int GetHashCode() => (Frame.GetHashCode() * 397) ^ Database;
        }

        /// <summary>An in-flight fill: the frame that will become the cache key, and what it depends on.</summary>
        [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
        public readonly struct RespFill
        {
            internal RespFill(RespCacheKey key, int database, Dependency[] dependencies)
            {
                Key = key;
                Database = database;
                Dependencies = dependencies;
            }

            internal RespCacheKey Key { get; }

            internal int Database { get; }

            internal Dependency[] Dependencies { get; }

            /// <summary>Abandon the fill without caching anything.</summary>
            public void Abandon() => Key.Dispose();
        }
    }
}
