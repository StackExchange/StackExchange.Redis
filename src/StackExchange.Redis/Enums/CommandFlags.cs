using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// Behaviour markers associated with a given command.
    /// </summary>
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
        [Obsolete("From 2.0, this flag is not used, this will be removed in 3.0.", false)]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        HighPriority = 1,

        /// <summary>
        /// The caller is not interested in the result; the caller will immediately receive a default-value
        /// of the expected return type (this value is not indicative of anything at the server).
        /// </summary>
        FireAndForget = 2,

        /// <summary>
        /// This operation should be performed on the primary if it is available, but read operations may
        /// be performed on a replica if no primary is available. This is the default option.
        /// </summary>
        PreferMaster = 0,

#if NET8_0_OR_GREATER
        /// <summary>
        /// This operation should be performed on the replica if it is available, but will be performed on
        /// a primary if no replicas are available. Suitable for read operations only.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(PreferReplica) + " instead, this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        PreferSlave = 8,
#endif

        /// <summary>
        /// This operation should only be performed on the primary.
        /// </summary>
        DemandMaster = 4,

#if !NET8_0_OR_GREATER
        /// <summary>
        /// This operation should be performed on the replica if it is available, but will be performed on
        /// a primary if no replicas are available. Suitable for read operations only.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(PreferReplica) + " instead, this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        PreferSlave = 8,
#endif

        /// <summary>
        /// This operation should be performed on the replica if it is available, but will be performed on
        /// a primary if no replicas are available. Suitable for read operations only.
        /// </summary>
        PreferReplica = 8, // note: we're using a 2-bit set here, which [Flags] formatting hates; position is doing the best we can for reasonable outcomes here

#if NET8_0_OR_GREATER
        /// <summary>
        /// This operation should only be performed on a replica. Suitable for read operations only.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(DemandReplica) + " instead, this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        DemandSlave = 12,
#endif

        /// <summary>
        /// This operation should only be performed on a replica. Suitable for read operations only.
        /// </summary>
        DemandReplica = 12, // note: we're using a 2-bit set here, which [Flags] formatting hates; position is doing the best we can for reasonable outcomes here

#if !NET8_0_OR_GREATER
        /// <summary>
        /// This operation should only be performed on a replica. Suitable for read operations only.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(DemandReplica) + " instead, this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        DemandSlave = 12,
#endif

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
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandRetryAlways = 1 << 13, // pre-shift value 1

        /// <summary>
        /// The command relates to the connection or to safe metadata (for example <c>CLIENT SETNAME</c>,
        /// <c>COMMAND</c>, or <c>CONFIG GET</c>) and can be retried at the connection level.
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandRetryConnection = 4 << 13, // pre-shift value 4

        /// <summary>
        /// The command only reads data (for example <c>GET</c>) and can be safely retried.
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandRetryReadOnly = 8 << 13, // pre-shift value 8

        /// <summary>
        /// The command writes data conditionally (for example <c>SETNX</c> or <c>SET ... IFEQ</c>), so a retry
        /// is checked against server state.
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandRetryWriteChecked = 12 << 13, // pre-shift value 12

        /// <summary>
        /// The command writes data such that a retry simply overwrites (last-writer-wins, for example <c>SET</c>).
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandRetryWriteLastWins = 16 << 13, // pre-shift value 16

        /// <summary>
        /// The command writes data cumulatively (for example <c>INCR</c> or <c>LPOP</c>), so a retry can
        /// double-apply and change the result.
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandRetryWriteAccumulating = 20 << 13, // pre-shift value 20

        /// <summary>
        /// The command performs server administration (for example <c>REPLICAOF</c> or <c>CONFIG SET</c>); these
        /// will commonly also carry <see cref="CommandServerSpecific"/>.
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandRetryServerAdmin = 24 << 13, // pre-shift value 24

        /// <summary>
        /// The command should never be retried.
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandRetryNever = 31 << 13, // pre-shift value 31 (the full retry-category region)

        /// <summary>
        /// The command is tied to a specific endpoint and must never be retried on a different endpoint
        /// (for example cursor-based operations); orthogonal to the retry-category region.
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        CommandServerSpecific = 1 << 18,
    }
}
