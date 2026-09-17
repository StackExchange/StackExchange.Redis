using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. How the server decides which keys to tell us about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two modes of <c>CLIENT TRACKING</c>, and they are a trade rather than a ranking: broadcasting
    /// costs the client noise, and per-key tracking costs the server memory. Which is cheaper depends on
    /// whose resource is scarce, which is not something a library can know.
    /// </para>
    /// <para>
    /// An enum rather than a <c>bool Broadcast</c> because the server has two further modes -
    /// <c>OPTIN</c> and <c>OPTOUT</c> - and a boolean cannot grow to hold them. Whether they will ever be
    /// offered here is genuinely open; see <see cref="PerKey"/>.
    /// </para>
    /// </remarks>
    public enum CacheTrackingMode
    {
        /// <summary>
        /// <c>BCAST</c>: the server announces every changed key matching
        /// <see cref="CacheOptions.Prefixes"/>, whoever changed it, without remembering what we read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default, because the cost lands on the side that can see it. The server keeps no per-client
        /// key table at all, so it cannot run out of room for one; what we pay instead is being told about
        /// keys we never asked for, which <see cref="CacheOptions.Prefixes"/> exists to bound.
        /// </para>
        /// <para>
        /// The consequence that reaches the cache: a key outside the prefixes is never announced, so it is
        /// never cached either - see <see cref="RespClientCache.RefusedNotTracked"/>.
        /// </para>
        /// </remarks>
        Broadcast = 0,

        /// <summary>
        /// Default mode: the server remembers which keys <i>we</i> read, and announces only those.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Precise - no key we did not ask about is ever mentioned - and <see cref="CacheOptions.Prefixes"/>
        /// is meaningless here, because there is nothing to filter. The cost moves to the server, which must
        /// hold a table of keys per client and evicts from it under pressure
        /// (<c>tracking-table-max-keys</c>), announcing what it drops.
        /// </para>
        /// <para>
        /// <b>Two hazards specific to this mode.</b> The server stops tracking a key once it has told us
        /// about it, so anything that reads without re-registering goes quietly stale - which is also why
        /// <c>NOLOOP</c> is not simply switched on (design notes 6.13). And <c>OPTIN</c>/<c>OPTOUT</c>, the
        /// two refinements that only exist here, are driven by <c>CLIENT CACHING YES|NO</c> applying to the
        /// <i>next command on that connection</i> - which a multiplexer does not give a caller any way to
        /// control. Offering them would need the pipeline to write the pair atomically, the way it already
        /// does for a transaction; until that exists they are not on the table, which is the honest reason
        /// they are absent from this enum rather than present and throwing.
        /// </para>
        /// </remarks>
        PerKey = 1,
    }
}
