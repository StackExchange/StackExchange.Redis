using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Caching
{
    /// <summary>
    /// How the server decides which keys to tell us about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The modes of <c>CLIENT TRACKING</c>, and they are a trade rather than a ranking: broadcasting
    /// costs the client noise, and per-key tracking costs the server memory. Which is cheaper depends on
    /// whose resource is scarce, which is not something a library can know.
    /// </para>
    /// <para>
    /// Per-key tracking comes in two refinements, <c>OPTOUT</c> (<see cref="Tracking"/>) and <c>OPTIN</c>
    /// (<see cref="TrackingOptIn"/>), which differ in what a read gets by default and which way the
    /// per-command flags - <see cref="CommandFlags.CacheOptOut"/> and <see cref="CommandFlags.CacheOptIn"/> -
    /// push. Both are honoured by writing <c>CLIENT CACHING YES|NO</c> as one unit with the command it
    /// modifies, so the "next command on this connection" the server speaks of really is that command.
    /// </para>
    /// </remarks>
    public enum CacheTrackingMode
    {
        /// <summary>
        /// Let the library choose; today that is <see cref="Tracking"/>, or <see cref="TrackingByPrefix"/>
        /// when <see cref="CacheOptions.Prefixes"/> are given.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Zero means "no opinion", not a mode.</b> Which mode is cheaper depends on whose resource is
        /// scarce - the remarks on this type say the library cannot know that - and wiring a real mode to
        /// zero says the opposite: that an unset value is a considered choice. It also makes the two
        /// indistinguishable, so the library can never tell a caller who asked for a mode from one who
        /// never thought about it, and can never change its mind without silently overriding the first.
        /// </para>
        /// <para>
        /// So the answer this resolves to may move - if server or client heuristics make one clearly
        /// better, or if it starts depending on more of what else is configured. Name the mode explicitly
        /// if you need it pinned.
        /// </para>
        /// </remarks>
        Default = 0,

        /// <summary>
        /// <c>BCAST</c>: the server announces every changed key matching
        /// <see cref="CacheOptions.Prefixes"/>, whoever changed it, without remembering what we read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A choice to make by name: <see cref="Default"/> does not resolve to it even when
        /// <see cref="CacheOptions.Prefixes"/> are given (that is <see cref="TrackingByPrefix"/>), because
        /// what broadcasting costs is set by the server's write rate, which this client cannot see or bound.
        /// The server keeps no per-client key table at all, so it
        /// cannot run out of room for one; what we pay instead is being told about every key anyone
        /// changes, whether or not we hold it, which <see cref="CacheOptions.Prefixes"/> exists to bound.
        /// Without prefixes on a busy server that is a lot of pushes, nearly all of them misses here.
        /// </para>
        /// <para>
        /// The consequence that reaches the cache: a key outside the prefixes is never announced, so it is
        /// never cached either - see <see cref="RespClientCache.RefusedNotTracked"/>. The per-command
        /// flags have only their local half here: <see cref="CommandFlags.CacheOptOut"/> bypasses the cache
        /// for a call, <see cref="CommandFlags.CacheOptIn"/> stores on the first miss; neither can say
        /// anything to a server that tracks nothing per client, and neither can reach a key outside the
        /// prefixes.
        /// </para>
        /// </remarks>
        Broadcast = 1,

        /// <summary>
        /// <c>CLIENT TRACKING ON OPTOUT</c>: the server remembers which keys <i>we</i> read, and announces
        /// only those; every read is tracked and cacheable unless a call says otherwise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What <see cref="Default"/> resolves to today, unless <see cref="CacheOptions.Prefixes"/> are
        /// given (then <see cref="TrackingByPrefix"/>). Precise - no key we did not ask about is ever
        /// mentioned, so the invalidation traffic is
        /// proportional to what this client holds rather than to what the server does - and
        /// <see cref="CacheOptions.Prefixes"/> is meaningless here, because there is nothing to filter.
        /// The cost moves to the server, which must hold a table of keys per client and evicts from it
        /// under pressure (<c>tracking-table-max-keys</c>), announcing what it drops; an eviction there
        /// costs us a refill, never a stale read.
        /// </para>
        /// <para>
        /// <c>OPTOUT</c> rather than plain <c>ON</c> because, measured against Redis 8.9, the two are
        /// identical except that <c>OPTOUT</c> permits <c>CLIENT CACHING NO</c>. That is the wire half of
        /// <see cref="CommandFlags.CacheOptOut"/>: a read the cache will not keep is sent behind it, and
        /// the server does not add the key to its table or push an invalidation for it. The library does
        /// the same for a transaction that contains a read, since reads inside <c>MULTI</c> are never
        /// cached.
        /// </para>
        /// <para>
        /// <b>One hazard specific to per-key tracking.</b> The server stops tracking a key once it has told
        /// us about it, so anything that reads without re-registering goes quietly stale - which is also
        /// why <c>NOLOOP</c> is not simply switched on (design notes 6.13).
        /// </para>
        /// </remarks>
        Tracking = 2,

        /// <summary>
        /// <c>CLIENT TRACKING ON OPTIN</c>: the server remembers only the keys we explicitly ask it to, and
        /// nothing is cached unless a call says so with <see cref="CommandFlags.CacheOptIn"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The inverse of <see cref="Tracking"/>, for a workload where a few hot reads deserve caching and
        /// everything else should cost the server nothing: no table entry, no invalidation push. A read
        /// without <see cref="CommandFlags.CacheOptIn"/> goes to the server as it would without a cache,
        /// and is not stored - it could not be, because nothing would ever invalidate it. A read with it is
        /// sent behind <c>CLIENT CACHING YES</c>, as one unit, tracked, and stored.
        /// </para>
        /// <para>
        /// The price is one extra frame and one extra reply per opted-in miss, so this is a mode for
        /// "cache these particular keys", not for general use; for "cache everything except a few",
        /// <see cref="Tracking"/> with <see cref="CommandFlags.CacheOptOut"/> on the few is the cheaper
        /// shape. <see cref="CacheOptions.Prefixes"/> are rejected here as they are for
        /// <see cref="Tracking"/> - opting in by prefix rather than by flag is <see cref="TrackingByPrefix"/>;
        /// <see cref="CommandFlags.CacheOptOut"/> has only its local meaning, there being nothing on the
        /// server to opt out of.
        /// </para>
        /// </remarks>
        TrackingOptIn = 3,

        /// <summary>
        /// <see cref="TrackingOptIn"/> where <see cref="CacheOptions.Prefixes"/> decide for the reads that do
        /// not say: a read whose keys all fall under a prefix is treated as <see cref="CommandFlags.CacheOptIn"/>,
        /// any other read as it would be without a cache.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What <see cref="Default"/> resolves to when <see cref="CacheOptions.Prefixes"/> are given. The
        /// same wire negotiation as <see cref="TrackingOptIn"/> - <c>CLIENT TRACKING ON OPTIN</c>, no
        /// <c>PREFIX</c>, which the server would refuse without <c>BCAST</c> - so the server tracks only
        /// what is sent behind <c>CLIENT CACHING YES</c>, and the prefixes are applied here, not there:
        /// an unflagged read inside them goes out behind <c>YES</c>, is tracked, and is stored on its first
        /// miss, exactly as if the call had said <see cref="CommandFlags.CacheOptIn"/>; an unflagged read
        /// outside them, or one whose keys straddle the boundary, is sent plain and kept nowhere.
        /// </para>
        /// <para>
        /// <b>The flags still win.</b> Prefixes are the policy for reads that did not state one;
        /// <see cref="CommandFlags.CacheOptIn"/> on a read outside the prefixes caches it (the server can
        /// track any key here, unlike under <see cref="Broadcast"/>), and <see cref="CommandFlags.CacheOptOut"/>
        /// on a read inside them leaves it uncached and untracked. That is what distinguishes this from
        /// <see cref="Broadcast"/> with the same list, where the prefixes are a hard boundary nothing can
        /// cross.
        /// </para>
        /// <para>
        /// <c>OPTIN</c> rather than <c>OPTOUT</c> because of where the extra frame lands: on the misses of
        /// the set chosen for caching, which is the traffic the cache exists to shrink, rather than on every
        /// read outside it. A prefix list is by nature "these few things"; "everything but" is
        /// <see cref="Tracking"/> with <see cref="CommandFlags.CacheOptOut"/> on the few. Preferred over
        /// <see cref="Broadcast"/> for the default because its cost is bounded by this client's own misses,
        /// where broadcasting is billed by the server's write rate.
        /// </para>
        /// </remarks>
        TrackingByPrefix = 4,
    }
}
