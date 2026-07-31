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
        [Obsolete("From 2.0, this flag is not used, this will be removed in 3.2.", error: true)]
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
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(PreferReplica) + " instead, this will be removed in 3.2.", error: true)]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        PreferSlave = 8,

        /// <summary>
        /// This operation should only be performed on a replica. Suitable for read operations only.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(DemandReplica) + " instead, this will be removed in 3.2.", error: true)]
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
        NoScriptCache = 512,

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
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CommandRetryAlways = 1 << 13, // pre-shift value 1

        /// <summary>
        /// The command relates to the connection or to safe metadata (for example <c>CLIENT SETNAME</c>,
        /// <c>COMMAND</c>, or <c>CONFIG GET</c>) and can be retried at the connection level.
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CommandRetryConnection = 4 << 13, // pre-shift value 4

        /// <summary>
        /// The command only reads data (for example <c>GET</c>) and can be safely retried.
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CommandRetryReadOnly = 8 << 13, // pre-shift value 8

        /// <summary>
        /// The command writes data conditionally (for example <c>SETNX</c> or <c>SET ... IFEQ</c>), so a retry
        /// is checked against server state.
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CommandRetryWriteChecked = 12 << 13, // pre-shift value 12

        /// <summary>
        /// The command writes data such that a retry simply overwrites (last-writer-wins, for example <c>SET</c>).
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CommandRetryWriteLastWins = 16 << 13, // pre-shift value 16

        /// <summary>
        /// The command writes data cumulatively (for example <c>INCR</c> or <c>LPOP</c>), so a retry can
        /// double-apply and change the result.
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CommandRetryWriteAccumulating = 20 << 13, // pre-shift value 20

        /// <summary>
        /// The command performs server administration (for example <c>REPLICAOF</c> or <c>CONFIG SET</c>); these
        /// are commonly also endpoint-specific (the internal server-specific flag).
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CommandRetryServerAdmin = 24 << 13, // pre-shift value 24

        /// <summary>
        /// The command should never be retried.
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CommandRetryNever = 31 << 13, // pre-shift value 31 (the full retry-category region)

        // 262144 (bit 18): "server specific" - the command is tied to a specific endpoint and must never be
        // retried on a different endpoint (for example cursor-based operations); orthogonal to the retry-category
        // region. Internal-only (see Message.CommandServerSpecific): the wrapper database cannot yet express
        // endpoint-stickiness over a *range* of operations, so this is not (currently) on the public API.
    }
}
