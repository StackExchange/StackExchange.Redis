using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Maintenance;

/// <summary>
/// The kind of a server-native maintenance notification.
/// </summary>
/// <remarks>
/// Two families share this enum: the Enterprise proxy notifications (<see cref="Moving"/> through
/// <see cref="FailedOver"/>) and the OSS cluster ones (<see cref="SlotMigrating"/>,
/// <see cref="SlotMigrated"/>). Unrecognized types are dropped rather than surfaced, so no member here means
/// "something we didn't understand".
/// </remarks>
[Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
public enum MaintenanceNotificationType
{
    /// <summary>
    /// Not a maintenance notification; the default, for contexts where this is simply not applicable.
    /// </summary>
    None = 0,

    /// <summary>
    /// This endpoint is being replaced; the notification names its successor, or names nothing at all if the
    /// server has no address to offer.
    /// </summary>
    Moving,

    /// <summary>
    /// A shard is migrating away from this node. Expect latency; expect a <see cref="Migrated"/> after it.
    /// </summary>
    Migrating,

    /// <summary>
    /// A migration announced by <see cref="Migrating"/> has completed.
    /// </summary>
    Migrated,

    /// <summary>
    /// This node is failing over. Expect latency; expect a <see cref="FailedOver"/> after it.
    /// </summary>
    FailingOver,

    /// <summary>
    /// A failover announced by <see cref="FailingOver"/> has completed.
    /// </summary>
    FailedOver,

    /// <summary>
    /// Slots are migrating (the OSS cluster family).
    /// </summary>
    SlotMigrating,

    /// <summary>
    /// Slots announced by <see cref="SlotMigrating"/> have migrated.
    /// </summary>
    SlotMigrated,
}
