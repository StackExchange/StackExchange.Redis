using System.Diagnostics.CodeAnalysis;
using RESPite;
using StackExchange.Redis.Maintenance;

namespace StackExchange.Redis;

/// <summary>
/// Carries maintenance context on the faults a disruption can cause, so that a timeout during an announced
/// migration is distinguishable from an ordinary one.
/// </summary>
/// <remarks>
/// This follows the established pattern on these types - <c>Commandstatus</c> and <c>Flags</c> on
/// <see cref="RedisTimeoutException"/>, <c>FailureType</c> on <see cref="RedisConnectionException"/> - rather
/// than introducing an exception type nobody is catching yet. Both properties are named for the role they
/// play, not for the type, matching <c>FailureType</c>.
/// </remarks>
public sealed partial class RedisTimeoutException
{
    /// <summary>
    /// The maintenance notification in force when this timed out, or
    /// <see cref="MaintenanceNotificationType.None"/> if the server had not announced anything.
    /// </summary>
    /// <remarks>
    /// A value here says the server told us to expect disruption, and that the timeout you are looking at
    /// happened inside that window - including the tail after the disruption reported completion. It does not
    /// promise that the maintenance *caused* the timeout, only that the two coincided; that is still the most
    /// useful thing to know when reading a log after the fact.
    /// </remarks>
    [Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
    public MaintenanceNotificationType MaintenanceType { get; internal init; }
}

/// <inheritdoc cref="RedisTimeoutException.MaintenanceType"/>
public sealed partial class RedisConnectionException
{
    /// <summary>
    /// The maintenance notification in force when this connection faulted, or
    /// <see cref="MaintenanceNotificationType.None"/> if the server had not announced anything.
    /// </summary>
    /// <remarks>
    /// Present on this type as well as on <see cref="RedisTimeoutException"/> because a handoff that misses
    /// its deadline surfaces either way round: as a timeout when the command was already in flight, and as a
    /// connection fault when the endpoint went away underneath it.
    /// </remarks>
    [Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
    public MaintenanceNotificationType MaintenanceType { get; internal init; }
}
