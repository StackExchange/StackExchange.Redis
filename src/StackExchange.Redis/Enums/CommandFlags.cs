using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// Behaviour markers associated with a given command.
    /// </summary>
    /*
     WARNING: the *declaration order* of the members below is load-bearing, and is not the obvious/natural order.

     This enum has duplicate values (the obsolete "slave" aliases, and PreferMaster which is zero like None) and
     multi-bit values (PreferReplica/DemandReplica occupy a 2-bit region), both of which [Flags] formatting handles
     badly. Which of two same-valued names ToString() picks depends on where they land in the *sorted* name/value
     arrays that the runtime builds, and that sort is unstable - so it is a function of declaration order, member
     count, and runtime. The order here was determined empirically to give sane output on both .NET Framework and
     modern .NET (see FormatTests.CommandFlagsFormatting for the expectations).

     Consequence: adding or removing members can silently perturb ToString() for *existing* values. If
     CommandFlagsFormatting starts failing after such a change, juggling the declaration order (in particular the
     positions of the obsolete PreferSlave/DemandSlave aliases) is the fix, not a change to the values.
    */
    [Flags]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Compatibility")]
    public enum CommandFlags
    {
        /// <summary>
        /// Default behaviour.
        /// </summary>
        None = 0,

        /// <summary>
        /// From 2.0, this flag is not used.
        /// </summary>
        [Obsolete("From 2.0, this flag is not used, this will be removed in 4.0.", error: true)]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        HighPriority = 1,

        /// <summary>
        /// The caller is not interested in the result; the caller will immediately receive a default-value
        /// of the expected return type (this value is not indicative of anything at the server).
        /// </summary>
        FireAndForget = 2,

        // note: the two obsolete aliases below are declared *before* their replacements, deliberately; see the
        // warning above the enum - this is what makes ToString() prefer the "replica" names over the legacy ones

        /// <summary>
        /// This operation should be performed on the replica if it is available, but will be performed on
        /// a primary if no replicas are available. Suitable for read operations only.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(PreferReplica) + " instead, this will be removed in 4.0.", error: true)]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        PreferSlave = 8,

        /// <summary>
        /// This operation should only be performed on a replica. Suitable for read operations only.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(DemandReplica) + " instead, this will be removed in 4.0.", error: true)]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        DemandSlave = 12,

        /// <summary>
        /// This operation should be performed on the primary if it is available, but read operations may
        /// be performed on a replica if no primary is available. This is the default option.
        /// </summary>
        PreferMaster = 0,

        /// <summary>
        /// This operation should only be performed on the primary.
        /// </summary>
        DemandMaster = 4,

        /// <summary>
        /// This operation should be performed on the replica if it is available, but will be performed on
        /// a primary if no replicas are available. Suitable for read operations only.
        /// </summary>
        PreferReplica = 8, // note: we're using a 2-bit set here, which [Flags] formatting hates; position is doing the best we can for reasonable outcomes here

        /// <summary>
        /// This operation should only be performed on a replica. Suitable for read operations only.
        /// </summary>
        DemandReplica = 12, // note: we're using a 2-bit set here, which [Flags] formatting hates; position is doing the best we can for reasonable outcomes here

        // 16: reserved for additional "demand/prefer" options

        // 32: used for "asking" flag; never user-specified, so not visible on the public API

        /// <summary>
        /// Indicates that this operation should not be forwarded to other servers as a result of an ASK or MOVED response.
        /// </summary>
        NoRedirect = 64,

        // 128: used for "internal call"; never user-specified, so not visible on the public API

        // 256: used for "script unavailable"; never user-specified, so not visible on the public API

        /// <summary>
        /// Indicates that script-related operations should use EVAL, not SCRIPT LOAD + EVALSHA.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Read it as "a weaker claim on the server's script cache", not as "no cache".</b> The name
        /// promises more than it can deliver, and the server has moved since it was chosen: a script is
        /// cached when it is run, not only when it is loaded - <i>"every script you execute with EVAL is
        /// stored in a dedicated cache that the server keeps"</i> - so nothing a client sends can keep a
        /// script out of that cache. To remove one, the command is <c>SCRIPT FLUSH</c>.
        /// </para>
        /// <para>
        /// <b>What it does control</b> is the protocol: the script body is sent with every call, no
        /// <c>SCRIPT LOAD</c> is issued, and no client-side hash is kept - so there is no <c>NOSCRIPT</c>
        /// round trip to recover from, at the cost of the body on the wire each time.
        /// </para>
        /// <para>
        /// <b>And since Redis 7.4 that is a real choice rather than merely a cheaper one.</b> The server
        /// evicts scripts <i>loaded with EVAL or EVAL_RO</i> when the cache grows too large,
        /// least-recently-used first, and that does not extend to <c>SCRIPT LOAD</c>. So this is the
        /// considerate way to send a script you cannot reuse - a one-off, or one generated per call, which
        /// the Redis documentation calls an anti-pattern precisely because such scripts accumulate. Sent
        /// this way they age out under pressure; loaded the usual way they do not.
        /// </para>
        /// <para>
        /// The corollary: do <b>not</b> reach for this on a hot script. There it only adds the body to
        /// every call and puts the script in the pool that gets evicted first.
        /// </para>
        /// <para>
        /// <b>The two populations do not overlap, which is what keeps this simple.</b> A script sent this
        /// way is never recorded as loaded and is never afterwards addressed by hash, so its eviction is
        /// invisible to us; a script we do track got there by <c>SCRIPT LOAD</c>, which the eviction rule
        /// does not name. That is why the client needs no notion of "believed loaded, but only for a
        /// while" - the evictable scripts are exactly the ones nothing is believed about.
        /// </para>
        /// </remarks>
        NoScriptCache = 512,

        // 524288 (bit 19): "probe" - health-check traffic; never user-specified, so not visible on the public API
        // (see CommandFlagsInternal.ProbeFlag)

        /// <summary>
        /// This command must not be served from, or stored in, the client-side cache - and, where the
        /// server is tracking what this client reads, the server is told not to track it either.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Client-side caching is opt-out by default: a command that declares a retry category no more
        /// severe than <see cref="CommandRetryReadOnly"/> and names at least one key is cacheable. This
        /// suppresses that for one call: the cache is neither consulted nor filled.
        /// </para>
        /// <para>
        /// <b>The wire half.</b> Under <see cref="Caching.CacheTrackingMode.Tracking"/> the server keeps a
        /// table of the keys this client has read, and a read it will never cache is a table entry and an
        /// invalidation push for nothing; so there the command goes out behind <c>CLIENT CACHING NO</c>,
        /// written as one unit with it, and the key is not tracked. Under
        /// <see cref="Caching.CacheTrackingMode.Broadcast"/> nothing is sent, because the server tracks
        /// nothing per client; under <see cref="Caching.CacheTrackingMode.TrackingOptIn"/> nothing is sent,
        /// because nothing is tracked unless asked. Writes are never tracked, so the flag on a write does
        /// nothing at all.
        /// </para>
        /// <para>
        /// Two reasons to reach for it. A value that changes so often that invalidation traffic costs more
        /// than the cache saves - probabilistic and time-series types are the documented examples, and the
        /// Redis guidance is to keep such data off a caching connection entirely. And a custom module
        /// command whose reads the server does not register for invalidation: it would otherwise be cached
        /// and never invalidated, and the library cannot know that on your behalf.
        /// </para>
        /// <para>
        /// A third, with a caveat: a read-only script (<c>EVAL_RO</c>/<c>EVALSHA_RO</c>) that reads a key it
        /// did not declare in <c>KEYS[]</c>. Invalidation tracks the declared keys, so an undeclared read is
        /// never invalidated and the result stays stale. The caveat is that such a script is <b>already</b>
        /// broken: the declared keys are what the client routes on, so in cluster it may not even have
        /// reached the node holding the key it computed, hash tags or no. Caching inherits that error rather
        /// than introducing it, and cannot fix it - declaring every key touched is the fix, and was the fix
        /// before any of this existed.
        /// </para>
        /// <para>
        /// Combining this with <see cref="CacheOptIn"/> is rejected: the pair is reserved.
        /// </para>
        /// </remarks>
        CacheOptOut = 1 << 20,

        /// <summary>
        /// This command's reply should be cached, and the server told to track its keys, even where the
        /// default would be not to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Under <see cref="Caching.CacheTrackingMode.TrackingOptIn"/> this is the only way anything is
        /// cached.</b> The server tracks nothing unless a read is preceded by <c>CLIENT CACHING YES</c>;
        /// this flag writes that ahead of the command, as one unit with it, and stores the reply. A read
        /// without it goes to the server and is not kept, because nothing would ever invalidate it.
        /// </para>
        /// <para>
        /// Under the other modes the server already tracks (or broadcasts) everything, so nothing is sent;
        /// what remains is the local effect: the reply is stored on its first miss, bypassing
        /// <see cref="Caching.CacheOptions.Admission"/>. The caller has said "cache this", and making them
        /// say it twice would be odd.
        /// </para>
        /// <para>
        /// It cannot make a command cacheable that is not: a write, a keyless read, a read whose answer is
        /// not a function of its keys (random members, TTLs), or - under
        /// <see cref="Caching.CacheTrackingMode.Broadcast"/> - a key outside
        /// <see cref="Caching.CacheOptions.Prefixes"/>, is still refused, with the same counters saying why.
        /// Combining this with <see cref="CacheOptOut"/> is rejected: the pair is reserved.
        /// </para>
        /// </remarks>
        CacheOptIn = 1 << 21,

        // 4194304 (bit 22): "never cached" - set by the command surface for reads whose reply is not a function
        // of their keys; never user-specified, so not visible on the public API (see CommandFlagsInternal.NeverCachedFlag)

        // 1024: used for "no flush"; never user-specified, so not visible on the public API

        // 2048: Use subscription connection type; never user-specified, so not visible on the public API

        // 4096: Identifies handshake completion messages; never user-specified, so not visible on the public API

        /*
         Command side-effect/retry logic:
         The values below reserve a chunk of bits (bits 13-17, i.e. << 13) for command
         retry logic/categorization; by default, nothing in this chunk will be passed and
         the library will supply a value based on the command being issued. The caller can
         override, though - ultimately it is their data/server! They will need to supply a
         suitable value for Execute[Async] etc, otherwise we'll assume the worst.

         The math here is intended so that we can specify a numeric "max" that we'll
         retry, and can filter with <=, so the higher numbers have more side-effects.
         As such, this region is not flags per-se, and we are intentionally leaving gaps
         to slide other things in later. Note that 0 is the implicit "not specified" value
         (distinct from CommandRetryAlways), and must be resolved to a concrete category
         downstream before any <= comparison. CommandServerSpecific (bit 18) is a genuine
         single-bit flag, orthogonal to the severity ladder.
        */

        /// <summary>
        /// The command is always safe to retry, regardless of connection or server state.
        /// </summary>
        CommandRetryAlways = 1 << 13, // pre-shift value 1

        /// <summary>
        /// The command relates to the connection or to safe metadata (for example <c>CLIENT SETNAME</c>,
        /// <c>COMMAND</c>, or <c>CONFIG GET</c>) and can be retried at the connection level.
        /// </summary>
        CommandRetryConnection = 4 << 13, // pre-shift value 4

        /// <summary>
        /// The command only reads data (for example <c>GET</c>) and can be safely retried.
        /// </summary>
        CommandRetryReadOnly = 8 << 13, // pre-shift value 8

        /// <summary>
        /// The command writes data conditionally (for example <c>SETNX</c> or <c>SET ... IFEQ</c>), so a retry
        /// is checked against server state.
        /// </summary>
        CommandRetryWriteChecked = 12 << 13, // pre-shift value 12

        /// <summary>
        /// The command writes data such that a retry simply overwrites (last-writer-wins, for example <c>SET</c>).
        /// </summary>
        CommandRetryWriteLastWins = 16 << 13, // pre-shift value 16

        /// <summary>
        /// The command writes data cumulatively (for example <c>INCR</c> or <c>LPOP</c>), so a retry can
        /// double-apply and change the result.
        /// </summary>
        CommandRetryWriteAccumulating = 20 << 13, // pre-shift value 20

        /// <summary>
        /// The command performs server administration (for example <c>REPLICAOF</c> or <c>CONFIG SET</c>); these
        /// are commonly also endpoint-specific (the internal server-specific flag).
        /// </summary>
        CommandRetryServerAdmin = 24 << 13, // pre-shift value 24

        /// <summary>
        /// The command should never be retried.
        /// </summary>
        CommandRetryNever = 31 << 13, // pre-shift value 31 (the full retry-category region)

        // 262144 (bit 18): "server specific" - the command is tied to a specific endpoint and must never be
        // retried on a different endpoint (for example cursor-based operations); orthogonal to the retry-category
        // region. Internal-only (see CommandFlagsInternal.CommandServerSpecific): the wrapper database cannot yet express
        // endpoint-stickiness over a *range* of operations, so this is not (currently) on the public API.
    }
}
