using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

        /// <summary>Whether this policy asks for background refresh at all.</summary>
        internal bool RefreshesEarly => RefreshAfter > TimeSpan.Zero && RefreshAfter < TimeToLive;

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
