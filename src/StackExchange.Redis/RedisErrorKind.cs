using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// Well-known server error conditions, identified from the error-reply prefix (and in some cases the
/// message text). Used to classify faults consistently - in particular to decide whether a fault is
/// transient (worth retrying / awaiting failover) or permanent (retrying will never help).
/// </summary>
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
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
    [AsciiHash("ERR")] // matched via the "ERR " branch, not the first token
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
    /// A <c>MOVED</c> or <c>ASK</c> redirect whose target cannot be identified, so it cannot be followed;
    /// the slot has moved, but the reply does not say where to.
    /// </summary>
    /// <remarks>
    /// This is a client-side classification rather than a server error code: it is a special case of
    /// <see cref="Moved"/> / <see cref="Ask"/>. It arises when the node answering prefers hostnames while the
    /// target has announced none, giving <c>MOVED &lt;slot&gt; ?:&lt;port&gt;</c> - and <c>?</c> denotes an
    /// *unknown* node, so unlike a missing or empty endpoint it must not be treated as the answering node.
    /// A topology refresh is requested when this happens, so a retry may well succeed.
    /// </remarks>
    UnknownRedirectTarget,
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

    internal static unsafe RedisErrorKind Classify(string message)
        => RespReader.TryParseScalar(message.AsSpan(), &TryParse, out RedisErrorKind kind) ? kind : RedisErrorKind.Unknown;

    internal static bool TryParse(ReadOnlySpan<byte> value, out RedisErrorKind kind)
    {
        var space = value.IndexOf((byte)' ');
        // Deal with the exact matches on the first token first - many errors may or may not
        // have descriptive text after the leading token.
        var firstToken = space > 0 ? value.Slice(0, space) : value;
        if (TryParseFirstTokenCI(firstToken, out kind))
        {
            // check for more specific "ERR ..." scenarios
            if (kind is RedisErrorKind.UnknownError & space > 0)
            {
                // get the message text after "ERR "
                value = value.Slice(space + 1);

                // some ERR conditions can be identified further, noting that the text may or
                // may not have some tokens - sometimes we need partial match.
                var valueHash = AsciiHash.HashUC(value);
                if (value.Length is OperationNotPermitted.Length && OperationNotPermitted.IsCI(value, valueHash))
                {
                    kind = RedisErrorKind.NotPermitted;
                }
                else if (value.Length >= UnknownCommand.Length &&
                           AsciiHash.SequenceEqualsCI(value.Slice(0, UnknownCommand.Length), UnknownCommand.U8))
                {
                    kind = RedisErrorKind.UnknownCommand;
                }
            }
            return true;
        }

        kind = value.IsEmpty ? RedisErrorKind.None : RedisErrorKind.Unknown;
        return true;
    }

    [AsciiHash("operation not permitted")]
    private static partial class OperationNotPermitted { }

    [AsciiHash("unknown command")]
    private static partial class UnknownCommand { }

    /* while these are recognizable, we issue SELECT *on behalf* of the user
     and react internally, so reporting them seems... unnecessary.

    [AsciiHash("DB index is out of range")]
    private static partial class DbIndexOutOfRange { }

    [AsciiHash("invalid DB index")]
    private static partial class InvalidDbIndex { }

    [AsciiHash("SELECT is not allowed in cluster mode")]
   private static partial class SelectNotAllowedInClusterMode { }
    */
}
