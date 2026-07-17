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

    /// <summary>
    /// Create a new <see cref="FaultContext"/>.
    /// </summary>
    /// <param name="fault">The fault associated with the operation, or <c>null</c> on success.</param>
    /// <param name="flags">The command-flags associated with the operation.</param>
    public FaultContext(Exception fault, CommandFlags flags)
    {
        _fault = fault;
        _flags = flags & Message.UserSelectableFlags; // just the user-visible ones
        ErrorKind = fault.GetErrorKind(out _connectionFailureType);
    }

    /// <summary>
    /// Create a new <see cref="FaultContext"/>.
    /// </summary>
    /// <param name="flags">The command-flags associated with the operation.</param>
    public FaultContext(CommandFlags flags)
    {
        _fault = null;
        _flags = flags & Message.UserSelectableFlags; // just the user-visible ones
        ErrorKind = RedisErrorKind.None;
        _connectionFailureType = ConnectionFailureType.None;
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
