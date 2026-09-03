using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using RESPite;

namespace StackExchange.Redis.Maintenance;

/// <summary>
/// A server-native maintenance notification, received as a RESP3 push frame on the connection that carries
/// commands. One class with a <see cref="NotificationType"/> discriminator rather than a type per
/// notification: the payloads are near-identical, and it keeps the handling in one place.
/// </summary>
/// <remarks>
/// The client acts on these itself - relaxing timeouts for the duration, learning a new topology, and moving
/// off an endpoint that says it is going away - so an application that only watches is watching work that has
/// already happened. See the <c>ServerMaintenanceEvent</c> documentation for what each notification does.
/// <para>
/// One notification is raised once, however many nodes announced it: every node broadcasts a given event, so
/// the copies are collapsed on their sequence number and <see cref="EndPoint"/> names whichever node arrived
/// first. And one case is deliberately *not* raised: a completion that the server retained and replayed to a
/// connection opting in later. That is history rather than news - it carries no time, so its age is
/// unknowable - and it is recorded in the log instead of being handed to a consumer who could only guess at
/// what to do with it.
/// </para>
/// </remarks>
[Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
public sealed class PushMaintenanceEvent : ServerMaintenanceEvent
{
    internal PushMaintenanceEvent(
        MaintenanceNotificationType notificationType,
        long sequenceId,
        EndPoint? endPoint,
        TimeSpan? time,
        EndPoint? newEndPoint,
        string? payload,
        string rawMessage,
        IReadOnlyList<ClusterSlotMigration>? slotMigrations = null)
    {
        NotificationType = notificationType;
        SequenceId = sequenceId;
        EndPoint = endPoint;
        Time = time;
        NewEndPoint = newEndPoint;
        Payload = payload;
        RawMessage = rawMessage;
        SlotMigrations = slotMigrations ?? [];
        if (time is { } value && value > TimeSpan.Zero)
        {
            StartTimeUtc = ReceivedTimeUtc + value;
        }
    }

    /// <summary>
    /// Which notification this is.
    /// </summary>
    public MaintenanceNotificationType NotificationType { get; }

    /// <summary>
    /// The sequence number the server attached to this notification.
    /// </summary>
    /// <remarks>
    /// No specification defines these, but observation does: on Enterprise 8.6.2 they are monotonic per
    /// database, start at zero on a fresh one, are shared *across* notification types (a
    /// <see cref="MaintenanceNotificationType.SlotMigrating"/> at 16 followed by its
    /// <see cref="MaintenanceNotificationType.SlotMigrated"/> at 17), and carry the same value on every node
    /// that broadcasts a given event - so they identify the event rather than the connection that delivered
    /// it. That makes them genuinely useful for spotting a replay.
    /// <para>
    /// Still treat cross-deployment use as heuristic: this is one build of one product, and nothing obliges a
    /// different implementation to behave the same way.
    /// </para>
    /// </remarks>
    public long SequenceId { get; }

    /// <summary>
    /// The server that sent the notification.
    /// </summary>
    /// <remarks>
    /// More precisely: whichever node told us first. Every node broadcasts a given event, so a three-proxy
    /// deployment delivers one migration three times, on three connections, with the same
    /// <see cref="SequenceId"/>. All three are acted on internally - the timeout relaxation is per-server, so
    /// each connection genuinely has to see it - but the event is raised once, for the first arrival, and the
    /// rest are dropped. Do not read this as "the node being maintained": for the cluster notifications it is
    /// usually a bystander reporting someone else's movements (see <see cref="SlotMigrations"/>).
    /// </remarks>
    public EndPoint? EndPoint { get; }

    /// <summary>
    /// How long the announced event is expected to take, or how much of it remains.
    /// </summary>
    /// <remarks>
    /// The meaning is per-notification: for <see cref="MaintenanceNotificationType.Moving"/> it is the budget
    /// for completing the move, and for the others it is the remaining duration of the announced disruption.
    /// It can legitimately be zero or negative - a connection that arrives mid-window is told what is left of
    /// it - which means "act now" rather than being an error. <c>null</c> where the notification carries no
    /// time at all.
    /// </remarks>
    public TimeSpan? Time { get; }

    /// <summary>
    /// For <see cref="MaintenanceNotificationType.Moving"/>, the endpoint this one is being replaced by.
    /// </summary>
    /// <remarks>
    /// <c>null</c> is a documented outcome, not just a parse failure: a server with no address to offer sends
    /// an explicit null, and it also does so when it cannot honour the endpoint type that was requested. The
    /// intended handling in that case is to reconnect using the endpoint already configured, rather than to
    /// treat the notification as invalid. See <see cref="Payload"/> for what actually arrived.
    /// </remarks>
    public EndPoint? NewEndPoint { get; }

    /// <summary>
    /// The final element of the notification, as received: the affected shard ids, the affected slots, or the
    /// endpoint of a <see cref="MaintenanceNotificationType.Moving"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately opaque. Nothing a client is asked to *do* depends on which shards are involved, so this is
    /// carried through for diagnostics rather than parsed into a model that the contract does not pin down.
    /// </remarks>
    public string? Payload { get; }

    /// <summary>
    /// For the cluster notifications, the slot movements described; empty for everything else.
    /// </summary>
    /// <remarks>
    /// A notification carries several of these, and the node that sent it is not necessarily the source of
    /// any of them - every node reports the same movements.
    /// </remarks>
    public IReadOnlyList<ClusterSlotMigration> SlotMigrations { get; }

    /// <inheritdoc/>
    public override string? ToString() => RawMessage;
}
