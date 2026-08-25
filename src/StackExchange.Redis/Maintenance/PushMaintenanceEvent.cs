using System;
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
/// Observation only at present: receiving one of these raises
/// <see cref="ConnectionMultiplexer.ServerMaintenanceEvent"/> and does nothing else. Acting on them - relaxing
/// timeouts, then moving off a doomed endpoint - is deliberately separate work, so a consumer can watch what
/// its servers are announcing before any behaviour depends on it.
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
        string rawMessage)
    {
        NotificationType = notificationType;
        SequenceId = sequenceId;
        EndPoint = endPoint;
        Time = time;
        NewEndPoint = newEndPoint;
        Payload = payload;
        RawMessage = rawMessage;
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
    /// No specification defines what these mean beyond being a number that goes up, so treat any use of them
    /// as heuristic; in particular, do not assume that they are contiguous, or that they are scoped the same
    /// way across notification types.
    /// </remarks>
    public long SequenceId { get; }

    /// <summary>
    /// The server that sent the notification.
    /// </summary>
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

    /// <inheritdoc/>
    public override string? ToString() => RawMessage;
}
