using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Everything about a client-side cache that is settled once, when the connection
    /// is made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Options versus policy.</b> The test for which side a setting belongs on is whether it changes what
    /// we <i>send</i> or what we <i>record</i>, or only how we <i>interpret</i> what we already hold. The
    /// first kind is a fact about the connection and lives here. The second kind is
    /// <see cref="CachePolicy"/>, and can vary per call, because it is applied when an entry is read and
    /// never stamped when it is stored - which it has to be, since entries are shared and one copy must
    /// serve callers with different tolerances.
    /// </para>
    /// <para>
    /// <see cref="Prefixes"/> is the clearest case: it <i>is</i> the argument list sent in
    /// <c>CLIENT TRACKING ... BCAST</c>, so it cannot vary per call - the server was only told once.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public sealed class CacheOptions
    {
        /// <summary>The default options, used when none are given.</summary>
        public static CacheOptions Default { get; } = new();

        /// <summary>Whether a cache is created at all.</summary>
        /// <remarks>
        /// The global switch. Suppressing the cache for one <i>command</i> is
        /// <see cref="CommandFlags.NoClientCache"/>, which suppresses the probe as well as the store; this
        /// is the deployment-level decision that there is no cache to probe.
        /// </remarks>
        public bool Enabled { get; init; } = true;

        /// <summary>How the server decides which keys to tell us about.</summary>
        /// <remarks>
        /// <see cref="CacheTrackingMode.Broadcast"/> by default: it costs the server nothing to remember,
        /// and what it costs us - hearing about keys we never asked for - is the part we can bound, with
        /// <see cref="Prefixes"/>.
        /// </remarks>
        public CacheTrackingMode TrackingMode { get; init; } = CacheTrackingMode.Broadcast;

        /// <summary>How entries behave, unless a caller says otherwise.</summary>
        public CachePolicy DefaultPolicy { get; init; } = CachePolicy.Default;

        /// <summary>
        /// The largest reply that may be cached; <see langword="null"/> for no limit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The cheapest bound available, and the one that binds first.</b> It needs no bookkeeping at
        /// all - the reply's size is known before anything is stored - and large replies are both the ones
        /// that consume a memory budget fastest and, typically, the ones least likely to be read again.
        /// </para>
        /// <para>
        /// Measured against the reply as the server sent it. What an entry actually <i>costs</i> is a little
        /// more, because each reply is copied into its own array from <see cref="System.Buffers.ArrayPool{T}"/>
        /// and the shared pool rounds up to power-of-two buckets - a 33-byte reply pins 64. That rounding is
        /// the quota's business; this is a limit a human sets, so it reads in the units a human has.
        /// </para>
        /// <para>
        /// Refusals are counted as <see cref="RespClientCache.RefusedTooLarge"/>, because a reply silently
        /// not being cached is exactly the kind of thing that should be answerable without a debugger.
        /// </para>
        /// </remarks>
        public int? MaxPayloadBytes
        {
            get => _maxPayloadBytes;
            init => _maxPayloadBytes = value is null or > 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "The maximum payload size must be positive, or null for no limit.");
        }

        private readonly int? _maxPayloadBytes = 1024 * 1024;

        /// <summary>
        /// The most memory cached replies may hold; <see langword="null"/> for no limit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Counted as memory held, not bytes carried.</b> Each reply is copied into its own rent from
        /// <see cref="System.Buffers.ArrayPool{T}"/>, and the shared pool serves from power-of-two buckets,
        /// so a 33-byte reply holds 64. Budgeting on payload lengths would under-report by up to a factor
        /// of two - which is the error that lets a quota fail to bind under exactly the workload that
        /// needed it to.
        /// </para>
        /// <para>
        /// Enforced after a store rather than before: whether an entry fits is not knowable until the reply
        /// has arrived, and refusing it at that point would throw away something already paid for in full.
        /// So it is stored, and the cache then evicts down to the budget - which also means the budget is a
        /// <i>target</i> the cache returns to, briefly overshot, rather than a wall.
        /// </para>
        /// <para>
        /// <see langword="null"/> is the default and means unbounded, which is the honest description of
        /// what a client-side cache is without one. Pair it with <see cref="MaxEntries"/>: bytes do not
        /// bound the tracked-key table, which grows with the number of distinct requests rather than their
        /// size.
        /// </para>
        /// </remarks>
        public long? MaxBytes
        {
            get => _maxBytes;
            init => _maxBytes = value is null or > 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "The memory budget must be positive, or null for no limit.");
        }

        private readonly long? _maxBytes;

        /// <summary>
        /// The most entries that may be cached; <see langword="null"/> for no limit.
        /// </summary>
        /// <remarks>
        /// The companion to <see cref="MaxBytes"/>, and not redundant with it: a workload of many tiny
        /// replies spends almost no memory on payloads while still growing the entry table and the
        /// tracked-key table, whose costs a byte budget cannot see.
        /// </remarks>
        public int? MaxEntries
        {
            get => _maxEntries;
            init => _maxEntries = value is null or > 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "The entry limit must be positive, or null for no limit.");
        }

        private readonly int? _maxEntries;

        /// <summary>Whether either budget is set.</summary>
        internal bool HasBudget => _maxBytes is not null || _maxEntries is not null;

        /// <summary>
        /// How many entries are examined when choosing what to evict.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Sampled, not exact, and deliberately so.</b> True LRU needs the moment of last use, which
        /// means a write on every <i>read</i> - and a read is the one path in this cache that is currently
        /// free: a dictionary lookup and a reference-count bump, nothing else. Paying for eviction on every
        /// hit to make eviction slightly better is the wrong trade, and Redis reached the same conclusion
        /// about its own keyspace, approximating LRU by sampling rather than maintaining it.
        /// </para>
        /// <para>
        /// So eviction samples this many entries and takes the oldest of them, by fill time. A larger
        /// sample is a closer approximation at proportionally more work, and the work happens on eviction -
        /// which is already the expensive path - rather than on every hit.
        /// </para>
        /// </remarks>
        public int EvictionSampleSize
        {
            get => _evictionSampleSize;
            init => _evictionSampleSize = value > 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "The eviction sample size must be positive.");
        }

        private readonly int _evictionSampleSize = 8;

        /// <summary>
        /// How often dead entries are reclaimed; <see cref="TimeSpan.Zero"/> or less to never sweep.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Invalidation deliberately does no work beyond stamping a generation, and expiry is decided when
        /// an entry is read - so an entry that was invalidated, or that simply aged out, holds its memory
        /// until something comes back for it. For a key that is never read again, that is forever. This is
        /// what comes back for it.
        /// </para>
        /// <para>
        /// A cadence rather than a deadline: nothing about correctness depends on it, since a dead entry is
        /// already refused on read. It is purely when the memory returns, which is why the default is
        /// unhurried.
        /// </para>
        /// </remarks>
        public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>Whether <see cref="SweepInterval"/> asks for sweeping at all.</summary>
        internal bool Sweeps => SweepInterval > TimeSpan.Zero;

        /// <summary><see cref="SweepInterval"/> as a <see cref="System.Diagnostics.Stopwatch"/> tick count.</summary>
        internal long SweepIntervalTicks => CachePolicy.ToTicks(SweepInterval);

        /// <summary>
        /// The key prefixes this connection asks the server to track; empty means all keys.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These are the <c>PREFIX</c> arguments of <c>CLIENT TRACKING ... BCAST</c>, and they are declared
        /// here rather than derived from anything because the server's rules are not the library's: prefixes
        /// are <b>connection-global</b>, must not overlap one another, and cannot be removed individually.
        /// Context key-prefixes routinely nest, so they are the wrong source. See design notes 6.13.
        /// </para>
        /// <para>
        /// <b>A prefix list is also a statement about what may be cached.</b> Under <c>BCAST</c> the server
        /// announces only keys matching a prefix, so an entry whose key matches none of them has no
        /// invalidation path - nothing will ever say it is wrong, and it is served until
        /// <see cref="CachePolicy.TimeToLive"/> alone retires it. That is the same defect as caching a
        /// keyless reply, and it is refused the same way: see
        /// <see cref="RespClientCache.RefusedNotTracked"/>.
        /// </para>
        /// <para>
        /// Narrowing the prefix list therefore narrows the cache. That is the trade being made: broadcasting
        /// everything means being told about every key any client touches, and scoping it down buys quiet at
        /// the cost of only caching what is in scope.
        /// </para>
        /// <para>
        /// Empty - the default - means <c>BCAST</c> with no prefix: every key is tracked, so every key is
        /// cacheable. An empty or null entry is not a way to spell that; it is rejected, because
        /// <c>""</c> matches everything and would silently turn a narrow list into a total one.
        /// </para>
        /// <para>
        /// <b>Broadcast only.</b> <c>PREFIX</c> is meaningless under
        /// <see cref="CacheTrackingMode.PerKey"/> - the server announces what we read, so there is nothing
        /// to filter - and <c>CLIENT TRACKING</c> rejects the combination outright. Setting both is an
        /// error, raised when the cache is built rather than at the handshake.
        /// </para>
        /// </remarks>
        public IReadOnlyList<string> Prefixes
        {
            get => _prefixes;
            init
            {
                _prefixes = value ?? throw new ArgumentNullException(nameof(value));
                _prefixBytes = Encode(_prefixes);
            }
        }

        private readonly IReadOnlyList<string> _prefixes = Array.Empty<string>();
        private readonly byte[][] _prefixBytes = [];

        /// <summary>Whether <see cref="Prefixes"/> restricts what may be cached.</summary>
        /// <remarks>
        /// Only ever true in <see cref="CacheTrackingMode.Broadcast"/>: under
        /// <see cref="CacheTrackingMode.PerKey"/> the server announces exactly what we read, so there is no
        /// such thing as an untracked key and nothing for the gate to refuse.
        /// </remarks>
        internal bool HasPrefixes => _prefixBytes.Length != 0;

        /// <summary>
        /// Check settings that constrain one another; called when a cache is built from these options.
        /// </summary>
        /// <remarks>
        /// Not in the <c>init</c> accessors, and not because it would be inconvenient there: an object
        /// initializer assigns in whatever order the <i>caller</i> wrote, so a rule spanning two properties
        /// would pass or fail depending on which line came first. Checking once, when the options are
        /// actually used for something, is the only place the whole object exists.
        /// </remarks>
        internal void Validate()
        {
            if (HasPrefixes && TrackingMode != CacheTrackingMode.Broadcast)
            {
                throw new ArgumentException(
                    $"Cache prefixes require {nameof(CacheTrackingMode)}.{nameof(CacheTrackingMode.Broadcast)};"
                    + $" {TrackingMode} announces the keys that were read, so there is nothing to filter.",
                    nameof(Prefixes));
            }
        }

        /// <summary>
        /// Whether a key is inside the tracked set, and so has something that can invalidate it.
        /// </summary>
        /// <remarks>
        /// Compared as <b>bytes</b>, against the key as it was written to the wire. That is the only
        /// comparison that means anything: the server matches the bytes it received and names those bytes
        /// back, so anything done to the key on the way out - a context key-prefix, keyspace isolation - is
        /// already baked in by the time it gets here.
        /// </remarks>
        internal bool IsTracked(scoped ReadOnlySpan<byte> key)
        {
            var prefixes = _prefixBytes;
            for (var i = 0; i < prefixes.Length; i++)
            {
                if (key.StartsWith(prefixes[i])) return true;
            }

            return false;
        }

        /// <remarks>
        /// Overlap is rejected rather than tolerated because the server rejects it: <c>CLIENT TRACKING</c>
        /// refuses a prefix list where one entry is a prefix of another. Catching it here means the failure
        /// arrives where the mistake was made, rather than as a handshake error much later.
        /// </remarks>
        private static byte[][] Encode(IReadOnlyList<string> prefixes)
        {
            if (prefixes.Count == 0) return [];

            var result = new byte[prefixes.Count][];
            for (var i = 0; i < prefixes.Count; i++)
            {
                var prefix = prefixes[i];
                if (string.IsNullOrEmpty(prefix))
                {
                    throw new ArgumentException(
                        "An empty cache prefix matches every key; use an empty prefix list to track everything.",
                        nameof(Prefixes));
                }

                result[i] = Encoding.UTF8.GetBytes(prefix);
            }

            for (var i = 0; i < result.Length; i++)
            {
                for (var j = 0; j < result.Length; j++)
                {
                    if (i != j && result[i].AsSpan().StartsWith(result[j]))
                    {
                        throw new ArgumentException(
                            $"Cache prefixes must not overlap, but '{prefixes[i]}' starts with '{prefixes[j]}'.",
                            nameof(Prefixes));
                    }
                }
            }

            return result;
        }
    }
}
