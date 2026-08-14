using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Provides information about a circuit-breaker test.
/// </summary>
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public readonly struct FaultContext
{
    private readonly Exception? _fault;
    private readonly ConnectionFailureType _connectionFailureType;
    private readonly CommandFlags _flags;

    internal static readonly FaultContext Success = default;

    /// <summary>
    /// Create a new <see cref="FaultContext"/>.
    /// </summary>
    /// <param name="fault">The fault associated with the operation, or <c>null</c> on success.</param>
    public FaultContext(Exception fault)
    {
        _fault = fault;

        var kind = RedisErrorKind.None;
        _connectionFailureType = ConnectionFailureType.None;
        var flags = CommandFlags.None;
        var status = CommandStatus.Unknown;
        switch (fault)
        {
            case RedisServerException server:
                kind = server.Kind;
                flags = server.Flags;
                break;
            case RedisConnectionException connection:
                _connectionFailureType = connection.FailureType;
                kind = RedisErrorKind.ConnectionFault;
                flags = connection.Flags;
                status = connection.CommandStatus;
                break;
            case RedisTimeoutException timeout:
                kind = RedisErrorKind.Timeout;
                flags = timeout.Flags;
                status = timeout.Commandstatus;
                break;
            case TimeoutException:
                kind = RedisErrorKind.Timeout;
                break;
        }

        NotApplied = IsKnownNotApplied(kind, status);

        if (kind is not RedisErrorKind.None & _connectionFailureType is ConnectionFailureType.None)
        {
            // fill in some blanks
            switch (kind)
            {
                case RedisErrorKind.Loading:
                    _connectionFailureType = ConnectionFailureType.Loading;
                    break;
                case RedisErrorKind.NoAuth:
                case RedisErrorKind.WrongPass:
                    _connectionFailureType = ConnectionFailureType.AuthenticationFailure;
                    break;
            }
        }

        flags &= Message.UserSelectableFlags;
        if ((flags & Message.MaskRetryCategory) is 0)
        {
            // if no retry category found: assume the worst
            flags |= CommandFlags.CommandRetryNever;
        }
        _flags = flags;
        ErrorKind = kind;
    }

    /// <summary>
    /// Indicates whether a fault is present.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Fault))]
    public bool IsFault => _fault is not null; // excludes: default(FaultContext)

    /// <summary>
    /// The fault associated with the operation.
    /// </summary>
    public Exception? Fault => _fault;

    /// <summary>
    /// Get any command-flags associated with the operation.
    /// </summary>
    public CommandFlags Flags => _flags;

    /// <summary>
    /// The classified server error condition associated with the fault, if any.
    /// </summary>
    public RedisErrorKind ErrorKind { get; }

    /// <summary>
    /// Indicates that the operation is known *not* to have been applied by the server - either because it
    /// never left the client, or because the server explicitly rejected it due to its own state (still
    /// loading, cluster down, writes refused, and so on). Retrying such an operation is a *first* attempt
    /// rather than a repeat, so it cannot double-apply a side-effect; <see cref="RetryPolicy"/> therefore
    /// ignores <see cref="RetryPolicy.MaxCommandRetryCategory"/> in this case.
    /// </summary>
    /// <remarks>
    /// This is deliberately conservative: it is only reported for conditions that the server can *only*
    /// raise before running anything (so a Lua script that failed part-way through cannot be mistaken for
    /// one that never ran), and for messages the client knows it never wrote. Everything else - notably
    /// timeouts, and connection loss after the request was sent - remains ambiguous and is not flagged.
    /// </remarks>
    public bool NotApplied { get; }

    /// <summary>
    /// The connection failure type associated with the fault, if any.
    /// </summary>
    public ConnectionFailureType ConnectionFailureType => _connectionFailureType;

    private static bool IsKnownNotApplied(RedisErrorKind kind, CommandStatus status)
    {
        // the client never handed it to the socket, so the server cannot have seen it
        if (status is CommandStatus.WaitingToBeSent or CommandStatus.WaitingInBacklog) return true;

        // an error *reply* usually means the server declined to run the command, but not always: a Lua
        // script can fail part-way through, having already applied earlier writes, and it propagates the
        // inner error verbatim (WRONGTYPE, and so on). So we only trust the conditions that describe the
        // *server's own state*, which it can only report before running anything.
        switch (kind)
        {
            case RedisErrorKind.Loading: // still loading the dataset
            case RedisErrorKind.ClusterDown: // slot not currently served
            case RedisErrorKind.MasterDown: // replica cannot serve, primary is unavailable
            case RedisErrorKind.TryAgain: // slot mid-migration
            case RedisErrorKind.Moved: // wrong node; this one did not run it
            case RedisErrorKind.Ask: // ditto, mid-migration
            case RedisErrorKind.UnknownRedirectTarget: // a redirect we could not follow, so still not run
            case RedisErrorKind.ReadOnly: // writes refused by a replica
            case RedisErrorKind.Misconfigured: // e.g. persistence failing, so writes are refused
            case RedisErrorKind.NoReplicas: // not enough replicas to accept the write
            case RedisErrorKind.Busy: // a script is hogging the server
            case RedisErrorKind.MaxClients: // refused at the door
                return true;
            default:
                return false;
        }
    }
}
