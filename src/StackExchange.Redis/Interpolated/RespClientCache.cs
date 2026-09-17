using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

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
    internal sealed class RespClientCache : IDisposable
    {
        private readonly ConcurrentDictionary<EntryKey, Entry> _entries = new();

        /// <summary>Requests currently being fetched, so concurrent misses can wait rather than pile on.</summary>
        private readonly ConcurrentDictionary<EntryKey, InFlight> _inFlight = new();
        private readonly RespKeyTable _keys;
        private long _stored;
        private long _refusedByFlags;
        private long _refusedNoKeys;
        private long _refusedNotTracked;
        private long _refusedTooLarge;
        private long _evicted;
        private long _bytes;
        private int _evictCursor;
        private long _lastSweep = Stopwatch.GetTimestamp();
        private long _refusedRaced;
        private long _redundantFills;
        private long _refusedError;
        private long _coalesced;
        private long _expired;
        private long _refreshes;
        private long _servedStale;

        /// <summary>Create a cache.</summary>
        /// <param name="options">How the cache is built; <see cref="CacheOptions.Default"/> when null.</param>
        /// <param name="keyCapacity">Initial size hint for the tracked-key table.</param>
        /// <remarks>
        /// One constructor rather than an overload pair: two constructors both carrying optional parameters
        /// is ambiguous for callers, and the analyzers say so (RS0026). Named arguments cover the cases an
        /// overload would have.
        /// </remarks>
        public RespClientCache(CacheOptions? options = null, int keyCapacity = 256)
        {
            Options = options ?? CacheOptions.Default;
            Options.Validate(); // settings that constrain one another; see CacheOptions.Validate
            _keys = new RespKeyTable(keyCapacity);
        }

        /// <summary>How this cache is built: the settled-once decisions.</summary>
        public CacheOptions Options { get; }

        /// <summary>How entries in this cache behave, unless a caller overrides it.</summary>
        public CachePolicy Policy => Options.DefaultPolicy;

        /// <summary>Background refreshes started, because an entry was ageing but still servable.</summary>
        /// <remarks>
        /// The stampedes that never formed. Compare with <see cref="Expired"/>: refreshes rising while
        /// expiries stay near zero is the shape you want - entries being renewed before anybody had to wait
        /// for one. Expiries rising alongside means the refresh window is too narrow to cover the fetch.
        /// </remarks>
        public long Refreshes => Volatile.Read(ref _refreshes);

        /// <summary>Reads answered from an entry the server had already invalidated.</summary>
        /// <remarks>
        /// Its own counter rather than folded into hits: these are answers that were knowingly out of date,
        /// and a deployment should be able to see how many it served without reading the configuration to
        /// find out whether it could have.
        /// </remarks>
        public long ServedStale => Volatile.Read(ref _servedStale);

        /// <summary>Hits refused because the entry had outlived its lifetime.</summary>
        /// <remarks>
        /// Distinct from an invalidation: nobody told us this was wrong, we simply stopped trusting it. A
        /// high count relative to <see cref="Stored"/> means the lifetime is shorter than the useful life of
        /// the data - or, if invalidation is working, that it is doing nothing for you.
        /// </remarks>
        public long Expired => Volatile.Read(ref _expired);

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

        /// <summary>
        /// Fills refused because a key falls outside <see cref="CacheOptions.Prefixes"/>, so the server will
        /// never announce a change to it.
        /// </summary>
        /// <remarks>
        /// The same defect as <see cref="RefusedNoKeys"/>, arrived at from the other direction: there, the
        /// request declared nothing to depend on; here, it declared something the server was never asked to
        /// watch. Either way the entry would be served until <see cref="CachePolicy.TimeToLive"/> retires
        /// it, with nothing in the system able to say it is wrong sooner.
        /// <para>
        /// A high count is the signal that the prefix list and the workload disagree - either the list is
        /// too narrow to be worth having, or commands are reaching keys nobody meant to cache.
        /// </para>
        /// </remarks>
        public long RefusedNotTracked => Volatile.Read(ref _refusedNotTracked);

        /// <summary>Fills refused because an invalidation landed while the command was in flight.</summary>
        public long RefusedRaced => Volatile.Read(ref _refusedRaced);

        /// <summary>Entries dropped to stay inside <see cref="CacheOptions.MaxBytes"/> or <see cref="CacheOptions.MaxEntries"/>.</summary>
        /// <remarks>
        /// Distinct from <see cref="Expired"/> and from a sweep: nothing was wrong with these entries, there
        /// was simply not room. Rising alongside a healthy hit rate means the budget is the binding
        /// constraint rather than the data's lifetime, which is a different conversation from "why is
        /// nothing being cached".
        /// </remarks>
        public long Evicted => Volatile.Read(ref _evicted);

        /// <summary>
        /// The memory currently held by cached replies.
        /// </summary>
        /// <remarks>
        /// What the entries actually hold, not what they carry: replies live in pooled arrays that round up
        /// to the pool's bucket sizes. See <see cref="CacheOptions.MaxBytes"/>.
        /// </remarks>
        public long Bytes => Volatile.Read(ref _bytes);

        /// <summary>Fills refused because the reply was larger than <see cref="CacheOptions.MaxPayloadBytes"/>.</summary>
        /// <remarks>
        /// Worth watching in both directions. Rising steadily means the limit is doing its job. Rising for
        /// the <i>same</i> request over and over means a round trip is being paid every time for something
        /// that would happily be cached with a slightly larger limit.
        /// </remarks>
        public long RefusedTooLarge => Volatile.Read(ref _refusedTooLarge);

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

        /// <summary>Misses that waited for a request already in flight instead of sending their own.</summary>
        /// <remarks>
        /// The stampedes that did <b>not</b> happen, and the direct counterpart of
        /// <see cref="RedundantFills"/>: before single-flight every one of these was a duplicate round trip.
        /// Watching the two together is the useful thing - coalesced rising while redundant stays flat is
        /// the shape you want, and redundant rising with it means requests are arriving faster than the
        /// leader can register, or their dependencies are changing under them. See design notes 6.15.
        /// </remarks>
        public long Coalesced => Volatile.Read(ref _coalesced);

        /// <summary>Requests currently in flight with at least one waiter attached.</summary>
        public int InFlightCount => _inFlight.Count;

        /// <summary>Fills refused because the reply was an error.</summary>
        /// <remarks>
        /// See <see cref="TryComplete"/> for why errors are not cacheable. A non-trivial count here is
        /// worth investigating on its own: errors should be rare, and caching them would have hidden that.
        /// </remarks>
        public long RefusedError => Volatile.Read(ref _refusedError);

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
        public bool OnInvalidate(ReadOnlySpan<byte> key) => _keys.Invalidate(key, local: false, Policy.ServesStale);

        /// <summary>
        /// Note that <b>this process</b> wrote a key, which is a stronger statement than an invalidation
        /// arriving from the server.
        /// </summary>
        /// <param name="key">The key we wrote.</param>
        /// <returns><c>true</c> if the key was being tracked.</returns>
        /// <remarks>
        /// <para>
        /// Two jobs. It invalidates, like any other notice - and it closes the door on
        /// <see cref="CachePolicy.InvalidationGracePeriod"/> for this entry, permanently. Serving
        /// through somebody else's write is defensible because no observer can prove the order; serving
        /// through our own is handing a caller back the value they just replaced.
        /// </para>
        /// <para>
        /// Also worth calling on the way out of a write even with server-assisted tracking switched on: the
        /// echo takes a round trip to come back, and this closes the window in between. It is safe to
        /// over-call - invalidating something twice costs a miss, which is the direction this design always
        /// errs in.
        /// </para>
        /// </remarks>
        public bool OnLocalWrite(ReadOnlySpan<byte> key) => _keys.Invalidate(key, local: true, Policy.ServesStale);

        /// <summary>
        /// Note that this process is <b>about to send</b> a command that changes keys, and invalidate every
        /// key it names.
        /// </summary>
        /// <param name="frame">The rendered command, read for its keys.</param>
        /// <returns>The number of keys stamped.</returns>
        /// <remarks>
        /// <para>
        /// <b>Before the send, not after the reply</b>, and that is the whole point. A read's dependencies
        /// are captured when it is sent, so a stamp that lands after our reply can be overtaken by a read
        /// issued in between - which would then be considered valid, and stored. Stamping first widens the
        /// window; the cost of a window that is too wide is a miss.
        /// </para>
        /// <para>
        /// If the write then fails, we invalidated for nothing. That is the right way to be wrong.
        /// </para>
        /// <para>
        /// <b>A write whose key marks overflowed stamps every argument instead of flushing.</b> A frame can
        /// only mark keys up to argument 62, so a large <c>MSET</c> or <c>DEL</c> reports "I have keys but
        /// cannot tell you which". Guessing a subset is not allowed - that is the whole reason the frame
        /// refuses to report one - but the arguments are a <i>superset</i> of the keys, and stamping a
        /// superset is correct. It costs one needless miss for any value that happens to equal a cached
        /// key, and it keeps the damage inside the command instead of taking out the entire cache, which is
        /// what this used to do. Bulk writes are exactly the workload that would have suffered.
        /// </para>
        /// <para>
        /// Only a frame that cannot be read at all falls back to <see cref="OnFlush"/>.
        /// </para>
        /// </remarks>
        internal int OnLocalWrite(in RespRequest frame)
        {
            var keyCount = frame.KeyCount;
            if (keyCount == 0) return 0;

            // negative means the marks overflowed; the arguments are a superset of the keys, and there are
            // at most ArgCount of them
            var wanted = keyCount < 0 ? frame.ArgCount : keyCount;
            Span<KeyRange> ranges = wanted <= 16 ? stackalloc KeyRange[16] : new KeyRange[wanted];

            var count = keyCount < 0 ? frame.TryGetAllArguments(ranges) : frame.TryGetKeys(ranges);
            if (count < 0)
            {
                // unreadable rather than merely unmarked; nothing left but the blunt instrument
                OnFlush();
                return -1;
            }

            for (var i = 0; i < count; i++)
            {
                OnLocalWrite(frame.GetKey(ranges[i]));
            }

            return count;
        }

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
            => TryGet(in frame, database, long.MaxValue, out payload);

        /// <summary>Look for a cached response, subject to a freshness requirement.</summary>
        /// <param name="frame">The rendered request.</param>
        /// <param name="database">The database the request ran against.</param>
        /// <param name="maxAgeTicks">
        /// The caller's own freshness requirement, from <see cref="RespContext.WithMaxCacheAge"/>;
        /// <see cref="long.MaxValue"/> when they did not state one.
        /// </param>
        /// <param name="payload">The cached reply, retained.</param>
        /// <remarks>
        /// <para>
        /// Age is checked on <b>read</b>, against the stricter of the policy's lifetime and the caller's
        /// requirement - never stamped when the entry was stored. The entry is shared, so one copy serves
        /// callers with different tolerances; stamping at store time would force identical replies to be
        /// cached once per distinct lifetime, which is the opposite of what a shared cache is for.
        /// </para>
        /// <para>
        /// An expired entry is left resident rather than removed, exactly as an invalidated one is: this is
        /// a read path, and <see cref="Sweep"/> is where entries go.
        /// </para>
        /// </remarks>
        public bool TryGet(in RespRequest frame, int database, long maxAgeTicks, [NotNullWhen(true)] out RespPayload? payload)
            => TryGet(in frame, database, maxAgeTicks, out payload, out _);

        /// <inheritdoc cref="TryGet(in RespRequest, int, long, out RespPayload?)"/>
        /// <param name="frame">The rendered request.</param>
        /// <param name="database">The database the request ran against.</param>
        /// <param name="maxAgeTicks">The caller's own freshness requirement.</param>
        /// <param name="payload">The cached reply, retained.</param>
        /// <param name="shouldRefresh">
        /// <c>true</c> if this caller has <b>claimed</b> the job of refreshing an ageing entry, and must
        /// now do it. At most one caller is told this per refresh.
        /// </param>
        /// <remarks>
        /// The claim is made here rather than by the caller because it has to be atomic with the lookup:
        /// deciding "this is old" and "I will be the one to fix it" in separate steps lets every reader past
        /// the threshold decide both, which is the stampede again wearing a different hat.
        /// </remarks>
        public bool TryGet(
            in RespRequest frame,
            int database,
            long maxAgeTicks,
            [NotNullWhen(true)] out RespPayload? payload,
            out bool shouldRefresh)
        {
            shouldRefresh = false;

            if (!_entries.TryGetValue(new EntryKey(frame, database), out var entry))
            {
                payload = null;
                return false;
            }

            // invalidated, but perhaps still servable for a moment - see TryServeStale for why that is a
            // decision rather than a shortcut
            if (!entry.IsValid) return TryServeStale(entry, out payload, out shouldRefresh);

            if (entry.Payload.TryRetain())
            {
                // re-check after retaining: an invalidation between the check and the retain would otherwise
                // let one stale read through the door it had already closed
                if (entry.IsValid)
                {
                    var limit = Math.Min(Policy.TimeToLiveTicks, maxAgeTicks);
                    if (!CachePolicy.IsOlderThan(entry.FilledAt, limit))
                    {
                        // served either way; the only question is whether this reader also goes and gets a
                        // newer one. Note the soft threshold is the POLICY's, not the caller's: a caller
                        // asking for fresher than it gets a miss, which is a stronger answer than a refresh.
                        if (Policy.RefreshesEarly
                            && CachePolicy.IsOlderThan(entry.FilledAt, Policy.RefreshAfterTicks)
                            && entry.TryClaimRefresh())
                        {
                            Interlocked.Increment(ref _refreshes);
                            shouldRefresh = true;
                        }

                        payload = entry.Payload;
                        return true;
                    }

                    Interlocked.Increment(ref _expired);
                }

                entry.Payload.Release();
            }

            payload = null;
            return false;
        }

        /// <summary>
        /// Serve an <b>invalidated</b> entry, briefly, while a refresh runs - if the policy allows it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The stampede that matters most. Time-based refresh smooths out entries that age at different
        /// moments; an invalidation arrives for <i>every</i> reader of a popular key at the same instant, and
        /// no amount of time-smoothing helps, because the trigger was not time.
        /// </para>
        /// <para>
        /// Three gates, each doing real work:
        /// </para>
        /// <list type="number">
        /// <item><description>
        /// The policy has to have asked for it. Serving a value the server has called wrong is a decision.
        /// </description></item>
        /// <item><description>
        /// <b>Never for our own writes.</b> "No observer can prove the order" justifies serving through
        /// somebody else's write; it says nothing about ours, and handing a caller back the value they just
        /// replaced is reported as corruption rather than as staleness.
        /// </description></item>
        /// <item><description>
        /// A window measured from first notice, which doubles as the absolute cap: on a hot-written key
        /// every refresh is invalidated before it can be stored, so without a bound this would serve stale
        /// for ever.
        /// </description></item>
        /// </list>
        /// </remarks>
        private bool TryServeStale(Entry entry, out RespPayload? payload, out bool shouldRefresh)
        {
            shouldRefresh = false;
            payload = null;

            if (!Policy.ServesStale || entry.WrittenLocally) return false;

            // measured from the INVALIDATION, not from whenever somebody first looked: a key nobody has read
            // for an hour should expire, not be resurrected by the next reader to wander past
            var staleSince = entry.StaleSince;
            if (staleSince == 0 || CachePolicy.IsOlderThan(staleSince, Policy.ServeStaleTicks)) return false;

            if (!entry.Payload.TryRetain()) return false;

            if (entry.TryClaimRefresh())
            {
                Interlocked.Increment(ref _refreshes);
                shouldRefresh = true;
            }

            Interlocked.Increment(ref _servedStale);
            payload = entry.Payload;
            return true;
        }

        /// <summary>
        /// Begin a fill for a <b>background refresh</b>, from a request that has already been rendered.
        /// </summary>
        /// <param name="request">The request to refresh; borrowed, and retained internally if this succeeds.</param>
        /// <param name="database">The database it runs against.</param>
        /// <param name="fill">The fill to complete once the reply arrives.</param>
        /// <remarks>
        /// The ordinary <see cref="TryBeginFill(ref RespRequestFrame, int, CommandFlags, out RespFill)"/> takes a
        /// freshly rendered frame and <i>detaches</i> it. A refresh has no frame to render - the whole point
        /// is that the cache key already <i>is</i> the request - so this retains rather than detaches, and
        /// ownership of the caller's copy is unaffected.
        /// <para>
        /// Key generations are captured here, before the refresh is sent, exactly as for a first fill: a
        /// write landing while the refresh is in flight must lose, not win.
        /// </para>
        /// <para>
        /// No <see cref="CacheOptions.Prefixes"/> check, deliberately: a refresh only ever exists for an
        /// entry the first fill already admitted, and the policy is fixed for the life of the cache, so
        /// re-testing it would be work that cannot change the answer.
        /// </para>
        /// </remarks>
        public bool TryBeginRefresh(in RespRequest request, int database, out RespFill fill)
        {
            if (!IsCacheable(request.Flags))
            {
                Interlocked.Increment(ref _refusedByFlags);
                fill = default;
                return false;
            }

            var keyCount = request.KeyCount;
            if (keyCount <= 0)
            {
                Interlocked.Increment(ref _refusedNoKeys);
                fill = default;
                return false;
            }

            Span<KeyRange> ranges = keyCount <= 16 ? stackalloc KeyRange[16] : new KeyRange[keyCount];
            var count = request.TryGetKeys(ranges);
            if (count < 0 || !request.TryRetain(out var key))
            {
                fill = default;
                return false;
            }

            var deps = count == 0 ? [] : new Dependency[count];
            for (var i = 0; i < count; i++)
            {
                var node = _keys.GetOrAdd(request.GetKey(ranges[i]), out var generation);
                deps[i] = new Dependency(node, generation);
            }

            // no in-flight registration: a refresh is not something anybody should wait for. The entry is
            // still being served, so a concurrent miss wanting this request is asking a different question -
            // it has nothing yet, and should fetch rather than queue behind a nicety.
            fill = new RespFill(key, database, deps, this, slot: null, replaces: true);
            return true;
        }

        /// <summary>Give back a refresh claim, so a later read can try again.</summary>
        /// <param name="frame">The request whose entry was being refreshed.</param>
        /// <param name="database">The database it ran against.</param>
        /// <remarks>
        /// Call on <b>every</b> outcome, success or failure. A refresh that completes replaces the entry, so
        /// the claim goes with the old one; a refresh that throws must hand the claim back, or the entry is
        /// pinned stale until its hard expiry while still being served - silent, and exactly the state this
        /// feature exists to avoid.
        /// </remarks>
        public void EndRefresh(in RespRequest frame, int database)
        {
            if (_entries.TryGetValue(new EntryKey(frame, database), out var entry)) entry.ReleaseRefreshClaim();
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
        public bool TryBeginFill(ref RespRequestFrame frame, int database, out RespFill fill)
            => TryBeginFill(ref frame, database, CommandFlags.CommandRetryReadOnly, out fill);

        /// <inheritdoc cref="TryBeginFill(ref RespRequestFrame, int, out RespFill)"/>
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
        public bool TryBeginFill(ref RespRequestFrame frame, int database, CommandFlags flags, out RespFill fill)
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

            // EVERY key, not any: the entry depends on all of them, so one key the server was never asked
            // to watch is enough to make the whole reply uninvalidatable. MGET tracked untracked is not
            // "mostly fine".
            if (Options.HasPrefixes)
            {
                for (var i = 0; i < count; i++)
                {
                    if (!Options.IsTracked(frame.GetKey(ranges[i])))
                    {
                        Interlocked.Increment(ref _refusedNotTracked);
                        fill = default;
                        return false;
                    }
                }
            }

            var deps = count == 0 ? [] : new Dependency[count];
            for (var i = 0; i < count; i++)
            {
                var node = _keys.GetOrAdd(frame.GetKey(ranges[i]), out var generation);
                deps[i] = new Dependency(node, generation);
            }

            var key = frame.Detach(flags);

            // Register as the leader for this request, so concurrent misses can wait on us instead of each
            // sending their own copy. Losing the race is not a failure: we simply lead without a slot, which
            // is exactly the behaviour before single-flight existed, and RedundantFills still counts it.
            var slot = new InFlight(deps);
            if (!_inFlight.TryAdd(new EntryKey(key, database), slot)) slot = null;

            fill = new RespFill(key, database, deps, this, slot);
            return true;
        }

        /// <summary>
        /// Wait for a request that is <b>already in flight</b> rather than sending a second copy of it.
        /// </summary>
        /// <param name="frame">The rendered request.</param>
        /// <param name="database">The database the request runs against.</param>
        /// <param name="pending">Completes when the leader's reply has been dealt with.</param>
        /// <returns><c>true</c> if there was something to wait for.</returns>
        /// <remarks>
        /// <para>
        /// <b>The wait yields no value</b> - the caller re-probes the cache afterwards. Handing the leader's
        /// payload across is the obvious design and it is worse: the payload is reference-counted, so a
        /// waiter resuming after the leader released its reference would have to be handed a dead buffer or
        /// a racily-retained one. Re-probing reuses <c>TryGet</c>, whose retain-and-recheck is
        /// already correct, and gives the right answer for free when the leader's reply turned out not to be
        /// cacheable at all.
        /// </para>
        /// <para>
        /// <b>Attaching is refused if anything the leader depends on has changed since it sent.</b> This is
        /// the read-your-own-writes case: if this process wrote one of those keys after the leader's send,
        /// the reply in flight predates the write, and serving it would be observably wrong rather than
        /// merely stale. The check is <see cref="Dependency.AllValid"/> - the same invariant that decides
        /// whether a fill may be <i>stored</i> decides whether a waiter may <i>attach</i>.
        /// </para>
        /// <para>
        /// Sharing is sound otherwise because no observer can tell the difference: the leader sent at T0,
        /// the waiter arrived at T greater than T0, the reply lands at T1 greater than T. Had the waiter
        /// sent its own request at T it would have been answered at about T1 too, so the shared reply is a
        /// legitimate answer to its read. See design notes section 6.15.
        /// </para>
        /// </remarks>
        public bool TryAwaitInFlight(in RespRequest frame, int database, [NotNullWhen(true)] out Task? pending)
        {
            if (_inFlight.TryGetValue(new EntryKey(frame, database), out var slot)
                && Dependency.AllValid(slot.Dependencies))
            {
                Interlocked.Increment(ref _coalesced);
                pending = slot.Completion;
                return true;
            }

            pending = null;
            return false;
        }

        /// <summary>
        /// Release this fill's in-flight registration, waking anything that attached to it.
        /// </summary>
        /// <remarks>
        /// <b>Remove before publishing.</b> The other order leaves a window in which a woken waiter re-probes
        /// the cache, misses, and re-attaches to a registration that is about to be removed - waiting on a
        /// reply that has already arrived. Removing first makes the registration unreachable before anyone
        /// is told to look again.
        /// <para>
        /// Idempotent, and a no-op for a fill that lost the race to register (which leads anyway - see
        /// <see cref="TryBeginFill(ref RespRequestFrame, int, CommandFlags, out RespFill)"/>).
        /// </para>
        /// </remarks>
        internal void Unregister(in RespFill fill)
        {
            // MUST run while fill.Key is still alive: the key is the dictionary key, so removing it hashes
            // and compares the buffer. Completing a fill disposes that key, so this cannot be folded into
            // the finally alongside Publish.
            if (fill.Slot is InFlight) _inFlight.TryRemove(new EntryKey(fill.Key, fill.Database), out _);
        }

        /// <summary>Wake anything waiting on this fill, once its result is visible.</summary>
        /// <remarks>
        /// Strictly after <see cref="Unregister"/>, so a woken waiter that misses cannot re-attach to a
        /// registration whose reply has already arrived and wait for a second one that never comes. A waiter
        /// arriving in the gap between the two finds neither a registration nor an entry and sends for
        /// itself - a missed coalescing opportunity, not a wrong answer.
        /// </remarks>
        internal static void Publish(in RespFill fill) => (fill.Slot as InFlight)?.Publish();

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

            Unregister(in fill);
            try
            {
                return TryCompleteCore(in fill, response);
            }
            finally
            {
                // AFTER the store, on every path including the refusals: a waiter wakes and re-probes the
                // cache, so it must not be woken before there is anything to find. Refusals wake them too -
                // they then miss and fetch for themselves, which is what they would have done anyway.
                Publish(in fill);
            }
        }

        private bool TryCompleteCore(in RespFill fill, RespPayload response)
        {
            if (!IsCacheableReply(response.Span))
            {
                Interlocked.Increment(ref _refusedError);
                fill.Key.Dispose();
                return false;
            }

            if (!Dependency.AllValid(fill.Dependencies))
            {
                Interlocked.Increment(ref _refusedRaced);
                fill.Key.Dispose();
                return false;
            }

            // last of the refusals, so this counter only ever means "nothing else was wrong with it"
            if (Options.MaxPayloadBytes is int max && response.Span.Length > max)
            {
                Interlocked.Increment(ref _refusedTooLarge);
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

            var entryKey = new EntryKey(stored, fill.Database);

            // A refresh REPLACES the entry it was started for. Swap the value in place rather than
            // remove-then-add: the dictionary keeps the key object it already has, so its retained request
            // stays owned by the dictionary. TryRemove hands back the value but NOT the stored key, so
            // removing would strand that key's reference - and disposing our own copy instead would release
            // the wrong one.
            var entry = new Entry(response, fill.Dependencies);
            if (fill.Replaces
                && _entries.TryGetValue(entryKey, out var previous)
                && _entries.TryUpdate(entryKey, entry, previous))
            {
                Interlocked.Add(ref _bytes, entry.Bytes - previous.Bytes);
                previous.Payload.Dispose(); // the superseded reply
                stored.Dispose();           // our key copy was spare; the dictionary kept its own
                fill.Key.Dispose();
                Interlocked.Increment(ref _stored);
                EvictToBudget();
                return true;
            }

            if (_entries.TryAdd(entryKey, entry))
            {
                Interlocked.Add(ref _bytes, entry.Bytes);
                fill.Key.Dispose(); // the dictionary holds its own references now
                Interlocked.Increment(ref _stored);
                EvictToBudget();
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
        /// Drop entries that can no longer be served, releasing their payloads and keys.
        /// </summary>
        /// <returns>The number of entries removed.</returns>
        /// <remarks>
        /// <para>
        /// Invalidation deliberately does no work beyond stamping a generation, and expiry is decided when
        /// an entry is <i>read</i>, so this is where the memory actually comes back. It is O(entries) and
        /// belongs on a timer, not on either of those paths.
        /// </para>
        /// <para>
        /// <b>Both kinds of dead entry</b>, not only invalidated ones. An entry that simply aged out is
        /// refused on read but never removed by reading, so for a key nothing comes back for it stays
        /// resident for ever - which is the case a lifetime is least able to help with, since nobody is
        /// there to notice it has passed.
        /// </para>
        /// </remarks>
        public int Sweep()
        {
            var removed = 0;
            var lifetime = Policy.TimeToLiveTicks;
            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (entry.IsValid && !CachePolicy.IsOlderThan(entry.FilledAt, lifetime)) continue;
                if (_entries.TryRemove(pair.Key, out var removing))
                {
                    Release(pair.Key, removing);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Sweep, but only if <see cref="CacheOptions.SweepInterval"/> has elapsed since the last one.
        /// </summary>
        /// <returns>The number of entries removed; zero if it was not yet due.</returns>
        /// <remarks>
        /// <para>
        /// The cadence lives here rather than in whatever is driving it, so the driver - today the
        /// multiplexer heartbeat - does not have to know the cache's business, and a test can call this
        /// directly instead of waiting on a timer.
        /// </para>
        /// <para>
        /// The timestamp is claimed with a compare-exchange <i>before</i> the work starts, so two drivers
        /// arriving together produce one sweep rather than two, and a sweep that overruns its interval is
        /// not restarted by every tick it overran. <b>This is a cost property, not a correctness one</b>:
        /// overlapping sweeps are already safe, because the removal is a <c>TryRemove</c> and only the
        /// caller that wins it disposes anything. Said plainly because the concurrency test below can
        /// demonstrate the safety but not reliably the collapse - the window is microseconds wide.
        /// </para>
        /// </remarks>
        public int SweepIfDue()
        {
            if (!Options.Sweeps) return 0;

            var last = Volatile.Read(ref _lastSweep);
            var now = Stopwatch.GetTimestamp();
            if (now - last < Options.SweepIntervalTicks) return 0;
            if (Interlocked.CompareExchange(ref _lastSweep, now, last) != last) return 0;

            return Sweep();
        }

        /// <summary>
        /// Let go of an entry that has already been removed from the table: its payload, its key, and its
        /// share of the budget.
        /// </summary>
        /// <remarks>
        /// One place, called only by whoever won the <c>TryRemove</c>, so the byte count cannot drift and a
        /// payload cannot be released twice - which would hand a live buffer back to the pool, since
        /// <c>Release()</c> is a bare decrement with no idempotence guard.
        /// </remarks>
        private void Release(in EntryKey key, Entry entry)
        {
            Interlocked.Add(ref _bytes, -entry.Bytes);
            entry.Payload.Dispose();
            key.Frame.Dispose();
        }

        /// <summary>
        /// Drop entries until the cache is back inside <see cref="CacheOptions.MaxBytes"/> and
        /// <see cref="CacheOptions.MaxEntries"/>.
        /// </summary>
        /// <returns>The number of entries dropped.</returns>
        /// <remarks>
        /// <para>
        /// <b>Sampled, oldest-of-the-sample.</b> True LRU needs the moment of last use, which means writing
        /// to an entry on every read - and a read is the one path here that is currently free. Redis
        /// approximates its own keyspace LRU by sampling for the same reason. See
        /// <see cref="CacheOptions.EvictionSampleSize"/>.
        /// </para>
        /// <para>
        /// <b>Invalid entries first, and for free.</b> The sample is walked anyway, so anything already dead
        /// is taken on sight rather than being scored: it costs nothing to release and, unlike a live entry,
        /// nobody wanted it. Only if the sample was all-live does the oldest of them go.
        /// </para>
        /// <para>
        /// Runs after a store, so the budget is a target the cache returns to rather than a wall - it is
        /// briefly overshot by one entry. Refusing the store instead would throw away a reply already paid
        /// for in full.
        /// </para>
        /// <para>
        /// The loop is bounded by <see cref="Count"/> rather than by "until it fits": under concurrent
        /// stores it might otherwise never catch up, and evicting for ever is a worse failure than being
        /// briefly over budget.
        /// </para>
        /// </remarks>
        private int EvictToBudget()
        {
            if (!Options.HasBudget) return 0;

            var evicted = 0;
            for (var attempts = _entries.Count; attempts > 0 && IsOverBudget(); attempts--)
            {
                if (!TryEvictOne()) break;
                evicted++;
            }

            if (evicted != 0) Interlocked.Add(ref _evicted, evicted);
            return evicted;
        }

        private bool IsOverBudget()
            => (Options.MaxBytes is long maxBytes && Volatile.Read(ref _bytes) > maxBytes)
               || (Options.MaxEntries is int maxEntries && _entries.Count > maxEntries);

        /// <remarks>
        /// <para>
        /// The enumerator of a <see cref="ConcurrentDictionary{TKey, TValue}"/> is a moving target and that
        /// is fine here: a sample does not need to be a snapshot, only a handful of real entries. Taking the
        /// first few is a poor sample when the enumeration order is stable, which is why the starting point
        /// moves - otherwise the same few entries would be offered up every time and evicted in turn,
        /// regardless of age.
        /// </para>
        /// <para>
        /// Two details that are easy to get subtly wrong. The start is chosen so a <b>whole</b> sample is
        /// always available - stopping at the end of the enumeration rather than wrapping would truncate
        /// samples that began near it, which quietly under-samples everything at the front of the table.
        /// And the cursor advances per call rather than coming from the clock: a burst of evictions happens
        /// far faster than <c>Environment.TickCount</c> changes, so a clock-derived start would hand out
        /// the same window repeatedly within one burst.
        /// </para>
        /// </remarks>
        private bool TryEvictOne()
        {
            var sampleSize = Options.EvictionSampleSize;
            var count = _entries.Count;
            var skip = count <= sampleSize
                ? 0
                : (int)((uint)Interlocked.Increment(ref _evictCursor) % (uint)(count - sampleSize + 1));

            EntryKey oldestKey = default;
            Entry? oldest = null;
            var seen = 0;
            var index = 0;

            foreach (var pair in _entries)
            {
                if (index++ < skip) continue;

                // already dead: no scoring needed, and nobody is losing anything they wanted
                if (!pair.Value.IsValid)
                {
                    if (!_entries.TryRemove(pair.Key, out var dead)) continue;
                    Release(pair.Key, dead);
                    return true;
                }

                if (oldest is null || pair.Value.FilledAt < oldest.FilledAt)
                {
                    oldest = pair.Value;
                    oldestKey = pair.Key;
                }

                if (++seen >= sampleSize) break;
            }

            if (oldest is null)
            {
                // the enumeration started past the end of a table that has since shrunk; the caller's
                // bounded loop will come back round if we are still over budget
                return false;
            }

            if (!_entries.TryRemove(oldestKey, out var removed)) return false;
            Release(oldestKey, removed);
            return true;
        }

        /// <summary>Release every cached payload and key.</summary>
        public void Dispose()
        {
            foreach (var pair in _entries)
            {
                if (!_entries.TryRemove(pair.Key, out var entry)) continue;
                Release(pair.Key, entry);
            }

            _keys.InvalidateAll();
        }

        /// <summary>
        /// Whether a reply may be cached: it has a content element, and that element is not an error.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Errors are never cached. The invariant that makes this cache sound is that a reply is a function
        /// of the keys it depends on, and that the server will say when those change. An error need not be:
        /// it can come from server configuration, cluster topology, ACLs, memory pressure or a module's own
        /// state, none of which key invalidation covers - so nothing would ever evict it.
        /// </para>
        /// <para>
        /// That turns a transient failure into a permanent one, which is the same class of bug as caching a
        /// keyless command. <c>-WRONGTYPE</c> genuinely IS a function of the key and would be invalidated
        /// correctly, but telling those apart needs per-code knowledge for a case that should be rare -
        /// and if errors are not rare, caching them hides the problem rather than solving it.
        /// </para>
        /// <para>
        /// A <b>null</b> reply is not an error: it is a value, and Redis tracks every key "mentioned in the
        /// context of a read-only command" whether or not it exists, so creating the key invalidates the
        /// entry. Negative caching therefore works, and works correctly.
        /// </para>
        /// <para>
        /// <b>This reads the first content element, not the first byte.</b> RESP3 permits attribute
        /// metadata (<c>|</c>) ahead of a value, and nothing in the specification exempts errors or nulls
        /// from carrying it - so a leading-byte test would classify <c>|1\r\n...\r\n-ERR ...</c> as
        /// cacheable and store an error. No server is known to emit that today, which is exactly why it
        /// would go unnoticed. <see cref="RespReader.TryMoveNext(bool)"/> skips attributes, and
        /// <c>checkError: false</c> stops it throwing on the case being detected.
        /// </para>
        /// <para>
        /// A reply with no content element at all - metadata only, or empty - is also refused: it cannot be
        /// classified, and unclassifiable fails closed.
        /// </para>
        /// </remarks>
        private static bool IsCacheableReply(ReadOnlySpan<byte> response)
        {
            if (response.IsEmpty) return false;

            // Attributes are the ONLY construct that can precede a value, so any other first byte IS the
            // first content element's prefix - which makes this exact, not approximate. Parsing is reserved
            // for the one case that needs it; no server emits attributes today, so that is in practice never.
            return (RespPrefix)response[0] switch
            {
                RespPrefix.Attribute => IsCacheableBehindAttributes(response),
                RespPrefix.SimpleError or RespPrefix.BulkError => false,
                _ => true,
            };
        }

        /// <summary>
        /// The attribute case: skip the metadata and classify the element that follows.
        /// </summary>
        /// <remarks>
        /// Deliberately separate and not inlined. <see cref="RespReader"/> is a sizeable <c>ref struct</c>,
        /// and constructing one in a cold branch changes codegen for the whole method - the same reason the
        /// fallbacks in <c>MessageWriter</c> are split out.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsCacheableBehindAttributes(ReadOnlySpan<byte> response)
        {
            var reader = new RespReader(response);

            // checkError:false - the default overload throws on an error, which is what we are detecting
            return reader.TryMoveNext(checkError: false) && !reader.IsError;
        }

        /// <summary>
        /// As <see cref="IsCacheable"/>, but <b>observed</b>: a refusal is counted.
        /// </summary>
        /// <remarks>
        /// The orchestration skips the cache entirely for a command whose flags forbid it - it does not
        /// probe and then decline - so <see cref="TryBeginFill(ref RespRequestFrame, int, CommandFlags, out RespFill)"/>
        /// is never reached and could never count those. That made <see cref="RefusedByFlags"/> unreachable
        /// in real use, which is worse than not having it: a diagnostic that reads zero because it is never
        /// asked looks like evidence. Routing the decision through the cache fixes that without making the
        /// cache probe things it has been told not to.
        /// </remarks>
        internal bool PermitsCaching(CommandFlags flags)
        {
            if (IsCacheable(flags)) return true;
            Interlocked.Increment(ref _refusedByFlags);
            return false;
        }

        /// <summary>
        /// Whether the command's flags permit caching at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The category is a 5-bit severity ladder where zero means "nobody declared one". Both halves of
        /// this test matter: <c>!= 0</c> rejects the undeclared case, and <c>&lt;=</c> uses the ladder the
        /// flags were built to support, so anything at or beyond a write - or server-admin - is out.
        /// </para>
        /// <para>
        /// <b><see cref="CommandFlags.FireAndForget"/> is excluded even when the command is read-only</b>,
        /// and the reason is the probe rather than the store. Fire-and-forget promises the caller
        /// <c>default</c>; a cache hit would hand back a real value instead, so the same call would answer
        /// differently depending on whether something else had happened to read that key first. A cache may
        /// make a call faster. It may not make it return something else.
        /// </para>
        /// <para>
        /// The store side is merely impossible rather than wrong: no reply is observed, so there is nothing
        /// to keep and no way to run the error check that keeps a failure from being cached. A
        /// fire-and-forget read as cache <i>warming</i> is the one coherent reading of the combination, and
        /// it cannot work for exactly that reason.
        /// </para>
        /// </remarks>
        internal static bool IsCacheable(CommandFlags flags)
        {
            if ((flags & (CommandFlags.NoClientCache | CommandFlags.FireAndForget)) != 0) return false;

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

            /// <summary>Whether this process wrote this key after the dependency was captured.</summary>
            internal bool LocalWriteSince => _node.LocalWriteAt > _generation;

            /// <summary>The earliest recorded invalidation among these keys, or zero if none was recorded.</summary>
            internal static long EarliestInvalidation(Dependency[] dependencies)
            {
                long earliest = 0;
                foreach (var dependency in dependencies)
                {
                    if (dependency.IsValid) continue;

                    var at = dependency._node.InvalidatedAt;
                    if (at != 0 && (earliest == 0 || at < earliest)) earliest = at;
                }

                return earliest;
            }

            /// <summary>Whether any of these keys was written by this process since they were captured.</summary>
            internal static bool AnyLocalWrite(Dependency[] dependencies)
            {
                foreach (var dependency in dependencies)
                {
                    if (dependency.LocalWriteSince) return true;
                }

                return false;
            }

            internal static bool AllValid(Dependency[] dependencies)
            {
                foreach (var dependency in dependencies)
                {
                    if (!dependency.IsValid) return false;
                }

                return true;
            }
        }

        /// <summary>A request currently being fetched, and what it depended on when it was sent.</summary>
        /// <remarks>
        /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> so that completing a fill never
        /// runs a waiter's continuation - and therefore its parse - on the thread that is finishing the
        /// leader's own reply.
        /// </remarks>
        private sealed class InFlight(Dependency[] dependencies)
        {
            private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal Dependency[] Dependencies { get; } = dependencies;

            internal Task Completion => _completion.Task;

            /// <summary>Release the waiters; they re-probe the cache for themselves.</summary>
            internal void Publish() => _completion.TrySetResult(true);
        }

        private sealed class Entry(RespPayload payload, Dependency[] dependencies)
        {
            private int _refreshing;

            internal RespPayload Payload { get; } = payload;

            /// <summary>What this entry holds, for the budget; see <see cref="CacheOptions.MaxBytes"/>.</summary>
            /// <remarks>
            /// Captured once rather than read from the payload each time: the payload is released when the
            /// entry goes, and the budget has to be credited by exactly what it was debited, whichever side
            /// of that release the accounting happens on.
            /// </remarks>
            internal int Bytes { get; } = payload.RetainedBytes;

            /// <summary>When this entry was filled, for expiry. See <see cref="CachePolicy.TimeToLive"/>.</summary>
            internal long FilledAt { get; } = Stopwatch.GetTimestamp();

            internal bool IsValid => Dependency.AllValid(dependencies);

            /// <summary>Whether this process wrote any of the keys this entry depends on, since it was filled.</summary>
            /// <remarks>
            /// The read-your-own-writes gate. An entry invalidated by our own write must never be served
            /// afterwards, however briefly - that is not staleness the caller can shrug at, it is the caller
            /// being handed back the value they just replaced.
            /// </remarks>
            internal bool WrittenLocally => Dependency.AnyLocalWrite(dependencies);

            /// <summary>
            /// When this entry became stale: the earliest invalidation among the keys it depends on.
            /// </summary>
            /// <remarks>
            /// Earliest, because that is the moment the entry stopped being right - a later invalidation of
            /// a second key does not restart the grace period. Zero when nothing recorded a time, which
            /// means the policy was not asking for one.
            /// </remarks>
            internal long StaleSince => Dependency.EarliestInvalidation(dependencies);

            /// <summary>
            /// Claim the right to refresh this entry, once.
            /// </summary>
            /// <remarks>
            /// The flag lives on the <b>shared entry</b> while the thresholds that lead here are
            /// per-context, and that is deliberate: whoever crosses their own soft bar first starts a
            /// refresh everyone benefits from. Without the claim, every concurrent reader past the
            /// threshold would start one - the background refresh would itself be the stampede it exists to
            /// prevent.
            /// </remarks>
            internal bool TryClaimRefresh() => Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0;

            /// <summary>
            /// Give the claim back, so a later read can try again.
            /// </summary>
            /// <remarks>
            /// Must happen on failure as well as success, or one failed refresh pins the entry stale until
            /// its hard expiry - the entry is still being served the whole time, so the damage is silent.
            /// </remarks>
            internal void ReleaseRefreshClaim() => Volatile.Write(ref _refreshing, 0);
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
        public readonly struct RespFill
        {
            internal RespFill(RespRequest key, int database, Dependency[] dependencies, RespClientCache? owner = null, object? slot = null, bool replaces = false)
            {
                Key = key;
                Database = database;
                Dependencies = dependencies;
                Owner = owner;
                Slot = slot;
                Replaces = replaces;
            }

            /// <summary>
            /// Whether completing this fill should <b>replace</b> an entry that is already there.
            /// </summary>
            /// <remarks>
            /// A first fill must not overwrite: losing that race means somebody else answered the same
            /// question first, and their answer is as good as ours. A <i>refresh</i> is the opposite - the
            /// entry it is replacing is the very one it was started for, so add-only would make every
            /// refresh a no-op that still counted as a redundant fill.
            /// </remarks>
            internal bool Replaces { get; }

            internal RespRequest Key { get; }

            internal int Database { get; }

            internal Dependency[] Dependencies { get; }

            /// <summary>
            /// The in-flight registration to release when this fill ends, if this fill won the race to make
            /// one. Typed as <see cref="object"/> because the slot type is private to the cache.
            /// </summary>
            internal object? Slot { get; }

            /// <summary>The cache that issued this fill, and which owns releasing the registration.</summary>
            internal RespClientCache? Owner { get; }

            /// <summary>Abandon the fill without caching anything.</summary>
            /// <remarks>
            /// Waiters are released here too. A failed request that kept its registration would strand
            /// everyone who attached to it until their own cancellation fired - and they would be waiting on
            /// a reply that is never coming.
            /// </remarks>
            public void Abandon()
            {
                Owner?.Unregister(in this); // while Key is still alive - it is the dictionary key
                Publish(in this);
                Key.Dispose();
            }
        }
    }
}
