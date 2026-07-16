using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Well-known server error conditions, identified from the error-reply prefix (and in some cases the
/// message text). Used to classify faults consistently - in particular to decide whether a fault is
/// transient (worth retrying / awaiting failover) or permanent (retrying will never help).
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
public enum RedisErrorKind
{
    /// <summary>
    /// No error condition; the reply was not an error.
    /// </summary>
    [AsciiHash("")]
    None = 0,

    /// <summary>
    /// The error was not recognized as one of the well-known conditions and did not start with <c>ERR</c>.
    /// </summary>
    [AsciiHash("")]
    Unknown,

    /// <summary>
    /// The error was not recognized as one of the well-known conditions, but started with <c>ERR</c>.
    /// </summary>
    [AsciiHash("")]
    UnknownError,

    /// <summary>
    /// The error was due to a connection fault, categorized separately by <see cref="ConnectionFailureType"/>.
    /// </summary>
    [AsciiHash("")]
    ConnectionFault,

    /// <summary>
    /// The operation timed out; this is a client-side condition and does not correspond to a server reply.
    /// </summary>
    [AsciiHash("")]
    Timeout,

    // --- availability / typically transient (retry or failover may recover) ---

    /// <summary>
    /// <c>LOADING</c> - the server is still loading its dataset into memory and is not yet ready.
    /// </summary>
    Loading,

    /// <summary>
    /// <c>CLUSTERDOWN</c> - the cluster is down; the hash slot is not currently being served.
    /// </summary>
    ClusterDown,

    /// <summary>
    /// <c>MASTERDOWN</c> - the link with the primary is down and <c>replica-serve-stale-data</c> is <c>no</c>.
    /// </summary>
    MasterDown,

    /// <summary>
    /// <c>TRYAGAIN</c> - a multi-key operation spans slots that are being migrated; the client should retry.
    /// </summary>
    TryAgain,

    /// <summary>
    /// <c>NOREPLICAS</c> - not enough healthy replicas to satisfy the configured <c>min-replicas-to-write</c>.
    /// </summary>
    NoReplicas,

    /// <summary>
    /// <c>MISCONF</c> - RDB/AOF persistence is misconfigured and writes are currently refused.
    /// </summary>
    [AsciiHash("MISCONF")]
    Misconfigured,

    /// <summary>
    /// <c>OOM</c> - the command was refused because used memory is above the <c>maxmemory</c> limit.
    /// </summary>
    [AsciiHash("OOM")]
    OutOfMemory,

    /// <summary>
    /// <c>BUSY</c> - a script or function is running and blocking the server (needs <c>SCRIPT KILL</c> etc.).
    /// </summary>
    Busy,

    /// <summary>
    /// <c>MAXCLIENTS</c> - the configured maximum number of client connections has been reached.
    /// </summary>
    MaxClients,

    // --- cluster slot routing ---

    /// <summary>
    /// <c>MOVED</c> - the hash slot has permanently moved to another endpoint.
    /// </summary>
    Moved,

    /// <summary>
    /// <c>ASK</c> - the hash slot is temporarily served by another endpoint during migration.
    /// </summary>
    Ask,

    /// <summary>
    /// <c>CROSSSLOT</c> - the keys in a multi-key operation do not all hash to the same slot.
    /// </summary>
    CrossSlot,

    // --- authentication / authorization (will not recover without a credential or ACL change) ---

    /// <summary>
    /// <c>NOAUTH</c> - authentication is required before commands can be issued.
    /// </summary>
    NoAuth,

    /// <summary>
    /// <c>WRONGPASS</c> - the supplied username/password pair was rejected.
    /// </summary>
    WrongPass,

    /// <summary>
    /// <c>NOPERM</c> - the authenticated ACL user is not permitted to run this command/key/channel.
    /// </summary>
    [AsciiHash("NOPERM")]
    NoPermission,

    /// <summary>
    /// <c>ERR operation not permitted</c> - the operation is not permitted in the current context.
    /// </summary>
    [AsciiHash("")] // matched via the "ERR " branch, not the first token
    NotPermitted,

    // --- client / usage errors (permanent - retry and failover will never help) ---

    /// <summary>
    /// <c>ERR unknown command</c> - the command is not known to the server (typo, disabled, or unsupported version).
    /// </summary>
    [AsciiHash("")] // matched via the "ERR " branch, not the first token
    UnknownCommand,

    /// <summary>
    /// <c>WRONGTYPE</c> - the operation was applied against a key holding an incompatible value type.
    /// </summary>
    WrongType,

    /// <summary>
    /// <c>EXECABORT</c> - the transaction was discarded because of an earlier error while queuing commands.
    /// </summary>
    ExecAbort,

    /// <summary>
    /// <c>READONLY</c> - a write was attempted against a read-only replica.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// <c>NOSCRIPT</c> - no matching script for <c>EVALSHA</c>; the script must be re-loaded.
    /// </summary>
    NoScript,

    /// <summary>
    /// <c>ERR DB index is out of range</c> / <c>ERR invalid DB index</c> - the database index passed to
    /// <c>SELECT</c> is out of range or not a valid integer.
    /// </summary>
    [AsciiHash("")] // matched via the "ERR " branch, not the first token
    InvalidDatabaseIndex,

    /// <summary>
    /// <c>ERR SELECT is not allowed in cluster mode</c> - selecting a non-default database is not supported
    /// on this server/topology (e.g. cluster mode, or a server without multi-database support).
    /// </summary>
    [AsciiHash("")] // matched via the "ERR " branch, not the first token
    DatabaseSelectDisabled,
}

internal static partial class RedisErrorKindMetadata
{
    [AsciiHash(CaseSensitive = false)]
    private static partial bool TryParseFirstTokenCI(ReadOnlySpan<byte> value, out RedisErrorKind kind);

    /// <summary>
    /// Classifies the error currently held by <paramref name="reader"/>. The caller is assumed to have
    /// already established that this is an error, so an unrecognized reply yields
    /// <see cref="RedisErrorKind.Unknown"/> (never <see cref="RedisErrorKind.None"/>).
    /// </summary>
    internal static unsafe RedisErrorKind Classify(in RespReader reader)
        => reader.TryParseScalar(&TryParse, out RedisErrorKind kind) ? kind : RedisErrorKind.Unknown;

    internal static bool TryParse(ReadOnlySpan<byte> value, out RedisErrorKind kind)
    {
        var space = value.IndexOf((byte)' ');
        // Deal with the exact matches on the first token first - many errors may or may not
        // have descriptive text after the leading token.
        var firstToken = space > 0 ? value.Slice(0, space) : value;
        if (TryParseFirstTokenCI(firstToken, out kind)) return true;

        // check for "ERR ..." scenarios
        if (space > 0 && Err.IsCI(firstToken, AsciiHash.HashUC(firstToken)))
        {
            value = value.Slice(space + 1); // message text after "ERR "

            // most ERR conditions are fixed messages matched in full; the exception is
            // "unknown command '<name>', with args ...", which always carries a trailing
            // command name and so is matched on its leading text (the final guarded arm)
            var valueHash = AsciiHash.HashUC(value);
            kind = value.Length switch
            {
                DbIndexOutOfRange.Length when DbIndexOutOfRange.IsCI(value, valueHash) => RedisErrorKind
                    .InvalidDatabaseIndex,
                InvalidDbIndex.Length when InvalidDbIndex.IsCI(value, valueHash) => RedisErrorKind
                    .InvalidDatabaseIndex,
                OperationNotPermitted.Length when OperationNotPermitted.IsCI(value, valueHash) => RedisErrorKind
                    .NotPermitted,
                SelectNotAllowedInClusterMode.Length when SelectNotAllowedInClusterMode.IsCI(value, valueHash) => RedisErrorKind
                    .DatabaseSelectDisabled,
                _ when value.Length >= UnknownCommand.Length
                    && AsciiHash.SequenceEqualsCI(value.Slice(0, UnknownCommand.Length), UnknownCommand.U8) => RedisErrorKind
                    .UnknownCommand,
                _ => RedisErrorKind.UnknownError,
            };
            return true;
        }

        kind = value.IsEmpty ? RedisErrorKind.None : RedisErrorKind.Unknown;
        return true;
    }

    [AsciiHash("ERR")]
    private static partial class Err { }

    [AsciiHash("DB index is out of range")]
    private static partial class DbIndexOutOfRange { }

    [AsciiHash("invalid DB index")]
    private static partial class InvalidDbIndex { }

    [AsciiHash("operation not permitted")]
    private static partial class OperationNotPermitted { }

    [AsciiHash("unknown command")]
    private static partial class UnknownCommand { }

    [AsciiHash("SELECT is not allowed in cluster mode")]
    private static partial class SelectNotAllowedInClusterMode { }
}
