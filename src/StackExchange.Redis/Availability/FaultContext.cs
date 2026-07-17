using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Provides information about a circuit-breaker test.
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
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
                break;
            case RedisTimeoutException timeout:
                kind = RedisErrorKind.Timeout;
                flags = timeout.Flags;
                break;
            case TimeoutException:
                kind = RedisErrorKind.Timeout;
                break;
            case RedisException redis:
                flags = redis.Flags;
                break;
        }

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
    /// The connection failure type associated with the fault, if any.
    /// </summary>
    public ConnectionFailureType ConnectionFailureType => _connectionFailureType;
}
