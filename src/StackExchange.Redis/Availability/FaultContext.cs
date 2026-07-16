using System;
using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Provides information about a circuit-breaker test.
/// </summary>
public readonly struct FaultContext
{
    private readonly Exception? _fault;
    private readonly ConnectionFailureType _connectionFailureType;

    internal static readonly FaultContext Success = default;

    /// <summary>
    /// Create a new <see cref="FaultContext"/>.
    /// </summary>
    /// <param name="fault">The fault associated with the operation, or <c>null</c> on success.</param>
    public FaultContext(Exception? fault)
    {
        _fault = fault;
        ErrorKind = fault.GetErrorKind(out _connectionFailureType);
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
    /// The classified server error condition associated with the fault, if any.
    /// </summary>
    public RedisErrorKind ErrorKind { get; }

    /// <summary>
    /// The connection failure type associated with the fault, if any.
    /// </summary>
    public ConnectionFailureType ConnectionFailureType => _connectionFailureType;
}
