using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Caching
{
    /// <summary>
    /// How a client-side cache behaves: how long an entry may be served, and what to do
    /// as it ages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How entries behave, not how the cache is built.</b> Everything here is applied when an entry is
    /// <i>read</i> and never stamped when it is stored, which is what makes it safe to vary per call: the
    /// entry is shared, so one copy has to serve callers with different tolerances. Settings that change
    /// what we <i>send</i> or what we <i>record</i> - the tracked prefixes, the memory budget - are facts
    /// about the connection and live on <see cref="CacheOptions"/> instead.
    /// </para>
    /// <para>
    /// The same read-time nature is what lets a <b>whole policy ride on the context</b> - see
    /// <see cref="RespDatabaseContext.WithCachePolicy"/> - so a caller with a different freshness
    /// tolerance, or a removal observer to arm, states it per call without changing how the shared entry
    /// behaves for anyone else. A per-read policy <b>replaces</b> the cache's
    /// <see cref="CacheOptions.DefaultPolicy"/> for those reads rather than merging with it, so derive it
    /// from the policy you mean: <c>policy with { TimeToLive = ... }</c>.
    /// </para>
    /// <para>
    /// It also keeps <see cref="RespContext"/> at its 48 bytes: a context carries a reference to a shared
    /// policy, rather than a field per knob.
    /// </para>
    /// </remarks>
    public sealed record CachePolicy
    {
        // BEFORE Default: statics initialise in declaration order, and Default's construction reads this
        private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(1);

        /// <summary>The default policy, used when none is given.</summary>
        public static CachePolicy Default { get; } = new();

        private readonly TimeSpan _timeToLive = DefaultTimeToLive;
        private readonly TimeSpan _refreshAfter;
        private readonly TimeSpan _invalidationGracePeriod;
        private readonly long _timeToLiveTicks = ToTicks(DefaultTimeToLive);
        private readonly long _refreshAfterTicks;
        private readonly long _serveStaleTicks;

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
        /// <para>
        /// On a per-read policy (<see cref="RespDatabaseContext.WithCachePolicy"/>) this is the caller's
        /// own freshness requirement, and it <b>narrows, never widens</b>: the
        /// <see cref="CacheOptions.DefaultPolicy"/>'s lifetime remains a ceiling, and reclamation follows
        /// the default policy alone - a read policy changes what its own reads will accept, never how long
        /// the shared entry lives.
        /// </para>
        /// </remarks>
        public TimeSpan TimeToLive
        {
            get
            {
                return _timeToLive;
            }

            init
            {
                _timeToLive = value;
                _timeToLiveTicks = ToTicks(value);
            }
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
        public TimeSpan RefreshAfter
        {
            get
            {
                return _refreshAfter;
            }

            init
            {
                _refreshAfter = value;
                _refreshAfterTicks = ToTicks(value);
            }
        }

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
        /// <para>
        /// That stamping decision belongs to the <see cref="CacheOptions.DefaultPolicy"/>, because the
        /// invalidation path cannot know which context will read next. So on a per-read policy this window
        /// only functions when the default policy enabled one too; given that, the window used is this
        /// policy's own - a caller can opt out of stale serving, or use a different window, still inside
        /// <see cref="TimeToLive"/>.
        /// </para>
        /// </remarks>
        public TimeSpan InvalidationGracePeriod
        {
            get
            {
                return _invalidationGracePeriod;
            }

            init
            {
                _invalidationGracePeriod = value;
                _serveStaleTicks = ToPreciseTicks(value);
            }
        }

        /// <summary>
        /// A removal observer: reads that follow this policy arm it on the keys they serve, and it is told
        /// when such a key stops being servable from the client-side cache.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>You hear about the keys you read, nothing else.</b> Enrollment rides the read: any serve
        /// under this policy - a cache hit, or the fill a miss performs - arms the observer on the keys it
        /// depended on, and the first removal at or after that consumes the enrollment. Reading again
        /// re-arms it. A key may have entered the cache through a context with no observer at all: a later
        /// hit under this policy still enrolls.
        /// </para>
        /// <para>
        /// <b>Per enrollment, exactly one notification, and never a missed removal</b> - including a fill
        /// that raced an invalidation, and a value served from the stale-grace window, both of which fire
        /// immediately: a value that is already not current is announced, not silently handed over. A
        /// notification can arrive after a racing re-read, so treat it as "drop derived state", which is
        /// always safe; it is advice about staleness, not an ordered event stream.
        /// </para>
        /// <para>
        /// The observer is told the key (exactly as stored, prefixes included, valid only for the
        /// callback) and why it went. It is called from the thread pool, never from the connection's
        /// reader. Keep it <b>long-lived and cheap</b>: build the policy once and reuse it (the handler
        /// reference is the identity enrollment dedupes on), and do only bookkeeping in the callback - it
        /// shares the thread pool with everything else. Exceptions are swallowed.
        /// </para>
        /// <para>
        /// On the <see cref="CacheOptions.DefaultPolicy"/> it is <b>connection-wide</b>: every cached read
        /// enrolls it, and so does a background refresh completing - so a watched key stays watched across
        /// its own replacement, for as long as anything keeps reading it.
        /// </para>
        /// </remarks>
        public CacheKeyRemovedHandler? RemovalHandler { get; init; }

        /// <summary>Whether this policy asks for background refresh at all.</summary>
        internal bool RefreshesEarly => RefreshAfter > TimeSpan.Zero && RefreshAfter < TimeToLive;

        /// <summary>Whether an invalidated entry may be served while it is refreshed.</summary>
        internal bool ServesStale => InvalidationGracePeriod > TimeSpan.Zero;

        /// <summary>
        /// <see cref="InvalidationGracePeriod"/> as a <see cref="Stopwatch"/> tick count - the PRECISE clock, unlike
        /// the ages below: a grace window can be tens of milliseconds, and it is only consulted for an entry that
        /// has already been invalidated, so its clock read is off the hit path.
        /// </summary>
        internal long ServeStaleTicks => _serveStaleTicks;

        /// <summary><see cref="RefreshAfter"/> in <see cref="CacheClock"/> ticks; memoised, the hit path reads it per serve.</summary>
        internal long RefreshAfterTicks => _refreshAfterTicks;

        /// <summary><see cref="TimeToLive"/> in <see cref="CacheClock"/> ticks; memoised, the hit path reads it per serve.</summary>
        internal long TimeToLiveTicks => _timeToLiveTicks;

        /// <summary>A span in <see cref="CacheClock"/> ticks: the coarse clock that entry ages are measured on.</summary>
        internal static long ToTicks(TimeSpan value)
            => value == TimeSpan.MaxValue
                ? long.MaxValue
                : (long)(value.TotalSeconds * CacheClock.Frequency);

        /// <summary>A span in <see cref="Stopwatch"/> ticks, for the one window that needs precision.</summary>
        internal static long ToPreciseTicks(TimeSpan value)
            => value == TimeSpan.MaxValue
                ? long.MaxValue
                : (long)(value.TotalSeconds * Stopwatch.Frequency);

        /// <summary>Whether an entry filled at <paramref name="filledAt"/> has outlived <paramref name="ticks"/> (<see cref="CacheClock"/>).</summary>
        internal static bool IsOlderThan(long filledAt, long ticks)
            => ticks != long.MaxValue && CacheClock.Now - filledAt > ticks;

        /// <summary>Whether a <see cref="Stopwatch"/> stamp is older than <paramref name="ticks"/>, also in <see cref="Stopwatch"/> ticks.</summary>
        internal static bool IsOlderThanPrecise(long stamp, long ticks)
            => ticks != long.MaxValue && Stopwatch.GetTimestamp() - stamp > ticks;
    }

    /// <summary>
    /// The clock that cache entry ages are measured on: coarse, and cheap enough to read on every hit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every hit reads it</b> - the <see cref="CachePolicy.TimeToLive"/> test - and <see cref="Stopwatch.GetTimestamp"/>
    /// measured at ~15ns of a ~134ns hit, against ~4ns for <c>Environment.TickCount64</c>. Ages here are seconds to
    /// hours, so a resolution of a few milliseconds (~16ms on Windows) costs nothing anyone can observe - the same
    /// trade as a timer-updated "now-ish" field, without the timer.
    /// </para>
    /// <para>
    /// <b>Down-level keeps <see cref="Stopwatch"/>.</b> <c>Environment.TickCount64</c> does not exist on
    /// <c>net461</c>/<c>netstandard2.0</c>, and the 32-bit <c>TickCount</c> wraps every ~49 days - exactly the kind
    /// of thing that bites once a quarter. Ticks are whatever this clock says they are: compare them only with each
    /// other, and convert spans with <see cref="CachePolicy.ToTicks"/>.
    /// </para>
    /// </remarks>
    internal static class CacheClock
    {
#if NET
        internal const long Frequency = 1000;

        internal static long Now => Environment.TickCount64;
#else
        internal static readonly long Frequency = Stopwatch.Frequency;

        internal static long Now => Stopwatch.GetTimestamp();
#endif
    }
}
