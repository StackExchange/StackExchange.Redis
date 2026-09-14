using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. How a client-side cache behaves: how long an entry may be served, and what to do
    /// as it ages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deployment-level configuration, held once by the cache rather than passed per call. The one thing
    /// that genuinely varies per caller is how stale an answer they will accept, and that rides on the
    /// context instead - see <see cref="RespContext.WithMaxCacheAge"/>. Splitting them that way matches
    /// where each decision actually lives: the tracking mode is a fact about the connection, the default
    /// lifetime is a fact about the deployment, and freshness tolerance is a fact about the call.
    /// </para>
    /// <para>
    /// It also keeps <see cref="RespContext"/> at its 48 bytes: a context carries a reference to shared
    /// policy plus at most one override, rather than a field per knob.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public sealed class CachePolicy
    {
        /// <summary>The default policy, used when none is given.</summary>
        public static CachePolicy Default { get; } = new();

        /// <summary>
        /// The longest an entry may be served after it was fetched, regardless of invalidation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A safety bound, not a tuning knob, and it must be finite.</b> There is no such thing as "no
        /// TTL policy" - the absence of a lifetime is a policy, and it is infinity. An entry that is never
        /// invalidated and never expires is <i>permanently</i> stale, which is strictly worse than being
        /// briefly over-stale. The Redis documentation makes the same point: <i>"Putting a max TTL on every
        /// key is a good idea, even if it has no TTL. This protects against bugs or connection issues that
        /// would make the client have old data in the local copy."</i>
        /// </para>
        /// <para>
        /// What it protects against is a <i>missed</i> invalidation - a connection blip nobody noticed, or a
        /// bug. Flushing on disconnect plus keepalive detects a real disconnect within seconds to tens of
        /// seconds, so the default of one minute is comfortably longer than detection while still bounding
        /// the damage when detection itself fails.
        /// </para>
        /// </remarks>
        public TimeSpan TimeToLive { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>Whether this policy permits caching at all.</summary>
        public bool Enabled { get; init; } = true;

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
        /// <see cref="TimeToLive"/> alone retires it. That is the same defect as caching a keyless reply,
        /// and it is refused the same way: see <see cref="RespClientCache.RefusedNotTracked"/>.
        /// </para>
        /// <para>
        /// Narrowing the prefix list therefore narrows the cache. That is the trade being made: broadcasting
        /// everything means being told about every key any client touches, and scoping it down buys quiet at
        /// the cost of only caching what is in scope.
        /// </para>
        /// <para>
        /// Empty - the default - means <c>BCAST</c> with no prefix: every key is tracked, so every key is
        /// cacheable. An empty or null entry is not a way to spell that; it is rejected, because
        /// "" matches everything and would silently turn a narrow list into a total one.
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
        internal bool HasPrefixes => _prefixBytes.Length != 0;

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

        /// <summary>
        /// How old an entry may get before a read refreshes it in the background, while still being served.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Stale-while-revalidate. Without it an entry goes from "good" to "gone" in one step, and every
        /// concurrent reader of a hot key misses at the same instant - the stampede this is here to prevent.
        /// With it there are two thresholds: below this, a hit is simply fresh; between this and
        /// <see cref="TimeToLive"/> the old value is still served <i>and</i> a refresh is started; past
        /// <see cref="TimeToLive"/> it is a miss like any other.
        /// </para>
        /// <para>
        /// <b>Off by default.</b> Serving a value already known to be old is a choice about correctness, not
        /// a tuning knob, so it should be made rather than inherited. <see cref="TimeSpan.Zero"/> or greater
        /// than <see cref="TimeToLive"/> both mean "never refresh early" - the latter because a threshold
        /// beyond the lifetime can never be crossed.
        /// </para>
        /// <para>
        /// The refresh costs no configuration of its own: <b>the cache key is the request</b>, so refreshing
        /// an entry means re-sending it. Nothing has to be handed a factory, and nothing of the caller's is
        /// retained to make it possible.
        /// </para>
        /// </remarks>
        public TimeSpan RefreshAfter { get; init; } = TimeSpan.Zero;

        /// <summary>
        /// A grace period after an invalidation, during which the old value may still be served while a
        /// refresh runs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The same stampede protection as <see cref="RefreshAfter"/>, triggered by an invalidation instead
        /// of by age - and the more valuable of the two, because an invalidation lands for <i>every</i>
        /// reader of a popular key at the same instant. That is the thundering herd exactly, and no amount
        /// of time-based smoothing helps, because the trigger was not time.
        /// </para>
        /// <para>
        /// <b>Read it as a grace period, not a licence.</b> It effectively moves the entry's hard expiry to
        /// "now plus this", measured from the <i>invalidation</i>. The case worth protecting is a key under
        /// constant access, where the herd forms instantly; a key that is not being read constantly should
        /// simply expire, and does - nobody arrives inside the window, so nothing is served and the entry
        /// goes on the next sweep.
        /// </para>
        /// <para>
        /// That is why the clock starts at the invalidation and not at the first read that notices. Starting
        /// at first notice would let a key invalidated an hour ago be served stale by whoever happened to
        /// read it next, which is the opposite of the intent: the point is to bridge a burst, not to
        /// resurrect something nobody wanted.
        /// </para>
        /// <para>
        /// It is also the <b>cap</b>. On a hot-written key every refresh is invalidated before it can be
        /// stored, so an unbounded window would serve stale for ever.
        /// </para>
        /// <para>
        /// <b>Serving through an invalidation is a stronger claim than serving something merely old</b>: the
        /// server has said this value is wrong and we are answering with it anyway. The justification is
        /// that no observer can prove the order - a caller arriving now might equally have arrived a moment
        /// before the write - and that holds for <i>somebody else's</i> write. It does not hold for our own,
        /// and this never applies to those.
        /// </para>
        /// <para>
        /// <b>Off by default.</b> Turning it on also turns on a timestamp read in the invalidation path,
        /// which is otherwise a few nanoseconds wide and sees every key the server mentions - so the cost
        /// lands only on those who asked for the feature.
        /// </para>
        /// </remarks>
        public TimeSpan InvalidationGracePeriod { get; init; } = TimeSpan.Zero;

        /// <summary>Whether this policy asks for background refresh at all.</summary>
        internal bool RefreshesEarly => RefreshAfter > TimeSpan.Zero && RefreshAfter < TimeToLive;

        /// <summary>Whether an invalidated entry may be served while it is refreshed.</summary>
        internal bool ServesStale => InvalidationGracePeriod > TimeSpan.Zero;

        /// <summary><see cref="InvalidationGracePeriod"/> as a <see cref="Stopwatch"/> tick count.</summary>
        internal long ServeStaleTicks => ToTicks(InvalidationGracePeriod);

        /// <summary><see cref="RefreshAfter"/> as a <see cref="Stopwatch"/> tick count.</summary>
        internal long RefreshAfterTicks => ToTicks(RefreshAfter);

        /// <summary><see cref="TimeToLive"/> as a <see cref="Stopwatch"/> tick count.</summary>
        /// <remarks>
        /// <see cref="Stopwatch.GetTimestamp"/> rather than <c>Environment.TickCount64</c>, which does not
        /// exist on <c>net461</c>/<c>netstandard2.0</c> - and whose 32-bit form wraps every ~49 days, which
        /// is exactly the kind of thing that bites once a quarter.
        /// </remarks>
        internal long TimeToLiveTicks => ToTicks(TimeToLive);

        internal static long ToTicks(TimeSpan value)
            => value == TimeSpan.MaxValue
                ? long.MaxValue
                : (long)(value.TotalSeconds * Stopwatch.Frequency);

        /// <summary>Whether an entry filled at <paramref name="filledAt"/> has outlived <paramref name="ticks"/>.</summary>
        internal static bool IsOlderThan(long filledAt, long ticks)
            => ticks != long.MaxValue && Stopwatch.GetTimestamp() - filledAt > ticks;
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. A per-context override of how stale an answer the caller will accept.
    /// </summary>
    /// <remarks>
    /// The one piece of cache configuration that cannot be added to the existing surface:
    /// <c>IDatabase.StringGet</c> cannot grow a parameter without a binary break, whereas the context
    /// reaches every command without touching a single signature.
    /// <para>
    /// Applied when the entry is <b>read</b>, never stamped when it is stored - the entry is shared, so one
    /// copy has to serve callers with different tolerances. Stamping at store time would force identical
    /// replies to be cached once per distinct lifetime, which is the opposite of what a shared cache is for.
    /// </para>
    /// </remarks>
    internal sealed class MaxCacheAgeService(TimeSpan maxAge)
    {
        internal TimeSpan MaxAge { get; } = maxAge;

        internal long Ticks { get; } = CachePolicy.ToTicks(maxAge);
    }
}
