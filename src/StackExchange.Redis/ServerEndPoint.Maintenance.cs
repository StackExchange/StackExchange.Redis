using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using RESPite;
using StackExchange.Redis.Maintenance;

namespace StackExchange.Redis;

internal sealed partial class ServerEndPoint
{
    private volatile bool _maintenanceNotificationsActive, _maintenanceNotificationsRequested;
    private volatile string? _maintenanceNotificationsRefusal;

    /// <summary>
    /// Whether this server has accepted our request for maintenance notifications on the current connection.
    /// </summary>
    /// <remarks>
    /// Per-connection state, so this is cleared at the start of every handshake and re-established from the
    /// reply; the scope is deliberately the server rather than the multiplexer, so one lagging node doesn't
    /// disable the feature for the whole deployment.
    /// </remarks>
    internal bool MaintenanceNotificationsActive => _maintenanceNotificationsActive;

    [Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
    private MaintenanceNotificationMode MaintenanceMode
        => Multiplexer.RawConfig.MaintenanceNotifications;

    /// <summary>
    /// Whether to ask this server for maintenance notifications during handshake.
    /// </summary>
    /// <remarks>
    /// The feature is RESP3-only: the notifications are out-of-band push frames on the connection that
    /// carries commands, and a RESP2 connection can have the request accepted and then silently receive
    /// nothing - so we don't ask unless we asked for RESP3. (Note that not asking is not the same as being
    /// satisfied: under <see cref="MaintenanceNotificationMode.Enabled"/> the reconcile below fails the
    /// connection for exactly this case.) We can't know what was *negotiated* at write time
    /// (the handshake is pipelined, and <c>HELLO</c> hasn't been answered yet), so this tests what we asked
    /// for and <see cref="ReconcileMaintenanceNotifications"/> settles it once the reply has been processed.
    /// </remarks>
    private bool ShouldRequestMaintenanceNotifications(bool isInteractive, bool negotiateResp3)
        => isInteractive
        && negotiateResp3
        && MaintenanceMode != MaintenanceNotificationMode.Disabled
        && Multiplexer.CommandMap.IsAvailable(RedisCommand.CLIENT);

    internal void OnMaintenanceNotificationsAccepted() => _maintenanceNotificationsActive = true;

    /// <summary>
    /// The server declined our request. Recorded rather than acted on: whether that matters is a question for
    /// <see cref="ReconcileMaintenanceNotifications"/>, which sees the negotiated protocol too.
    /// </summary>
    internal void OnMaintenanceNotificationsRefused(PhysicalConnection connection, string reason)
    {
        _maintenanceNotificationsActive = false;
        _maintenanceNotificationsRefusal = reason;
        connection.OnDetailLog($"maintenance notifications refused: {reason}");
    }

    /// <summary>
    /// Settles the feature for this connection now that the handshake is complete and the protocol is known.
    /// </summary>
    /// <remarks>
    /// The opt-in reply precedes the tracer on the same pipelined connection, so by the time this runs every
    /// fact is in: whether we asked, what the server said, and what protocol we ended up on.
    /// </remarks>
    private void ReconcileMaintenanceNotifications(PhysicalConnection connection)
    {
        bool resp3 = connection.Protocol is >= RedisProtocol.Resp3;
        if (!resp3)
        {
            // whatever the server said about the opt-in, nothing can arrive on a RESP2 connection
            _maintenanceNotificationsActive = false;
        }

        if (_maintenanceNotificationsActive || MaintenanceMode != MaintenanceNotificationMode.Enabled)
        {
            return;
        }

        // Enabled means required: no notifications, no connection. That includes a configuration that never
        // got as far as asking - requiring a RESP3-only feature over RESP2 is a contradiction, and failing it
        // is more useful than honouring half of it. Note the cross-client spec only calls for failing when
        // the *server* errors; extending that to the RESP2 cases is ours, and deliberate
        var reason = !resp3
            ? (_maintenanceNotificationsRequested ? "the connection negotiated RESP2" : "RESP3 was not requested")
            : _maintenanceNotificationsRefusal ?? "the server did not accept the request";

        connection.RecordConnectionFailed(
            ConnectionFailureType.ProtocolFailure,
            new RedisConnectionException(ConnectionFailureType.ProtocolFailure, CommandFlags.None, $"Maintenance notifications are enabled, but unavailable: {reason}", innerException: null));
    }

    // The relaxed-timeout window, expressed as a single deadline in Environment.TickCount terms so that it
    // can be read without a lock from the heartbeat sweeps. Zero means "no window"; a deadline that computes
    // to zero is nudged by a tick rather than complicating the sentinel. Comparisons are unchecked
    // subtraction, as elsewhere in this type, so the ~49-day wrap is a non-event.
    private int _relaxedDeadlineTicks;

    // the notification that last touched the window, reported on faults that happen inside it
    private int _relaxedType;

    // Dedup state, per notification type: the sequence ids are not defined by any specification, so this is
    // conservative - a repeat of an id we have already acted on is ignored, and nothing else is inferred.
    // Allocated on first notification, so a server that never sees one pays nothing.
    private long[]? _lastSequenceIds;
    private readonly object _maintenanceSync = new();

    /// <summary>
    /// Whether timeouts are currently relaxed for this server.
    /// </summary>
    internal bool IsMaintenanceRelaxed => GetRelaxedRemaining() > 0;

    /// <summary>
    /// The notification in force for this server, or <see cref="MaintenanceNotificationType.None"/> if no
    /// window is open; reported on faults so that a timeout during a migration says so.
    /// </summary>
    internal MaintenanceNotificationType ActiveMaintenanceType
        => GetRelaxedRemaining() > 0 ? (MaintenanceNotificationType)Volatile.Read(ref _relaxedType) : MaintenanceNotificationType.None;

    /// <summary>
    /// The effective timeout for a command against this server: the configured value, or the relaxed value if
    /// that is larger and a window is open. Relaxation is a floor and can never shorten a timeout.
    /// </summary>
    /// <remarks>
    /// Read from the timeout sweeps rather than stamped onto each message: both sweeps rely on head-of-line
    /// ordering and stop at the first message that has not timed out, which per-message timeouts would
    /// invalidate. The consequence is that relaxation applies to whatever is outstanding when a window opens,
    /// and stops applying when it closes - which is one of the reasons the post-event tail exists.
    /// </remarks>
    internal int GetEffectiveTimeoutMilliseconds(int configuredMilliseconds)
    {
        if (GetRelaxedRemaining() <= 0) return configuredMilliseconds;

        var relaxed = (int)Multiplexer.RawConfig.MaintenanceRelaxedTimeout.TotalMilliseconds;
        return relaxed > configuredMilliseconds ? relaxed : configuredMilliseconds;
    }

    /// <summary>
    /// Milliseconds of relaxation left, or zero if none; clears an expired window as a side-effect.
    /// </summary>
    private int GetRelaxedRemaining()
    {
        var deadline = Volatile.Read(ref _relaxedDeadlineTicks);
        if (deadline == 0) return 0;

        var remaining = unchecked(deadline - Environment.TickCount);
        if (remaining > 0) return remaining;

        // expired; clear it, but only if nobody has moved it on in the meantime
        Interlocked.CompareExchange(ref _relaxedDeadlineTicks, 0, deadline);
        return 0;
    }

    /// <summary>
    /// An announced disruption has started (or is still running): open or extend the relaxed window.
    /// </summary>
    internal void OnMaintenanceWindowOpened(MaintenanceNotificationType type, long? sequenceId, TimeSpan? time)
    {
        if (!TryClaimSequenceId(type, sequenceId)) return;

        var config = Multiplexer.RawConfig;
        var floor = config.MaintenanceRelaxedTimeout;
        var cap = config.MaintenanceRelaxedWindowMax;

        // no time, or a time at or below zero, means "act now" rather than "ignore this": a connection that
        // arrives mid-window is told what is left of it, and that can legitimately be negative
        var duration = time is { } value && value > TimeSpan.Zero ? value : floor;
        if (duration < floor) duration = floor;
        if (duration > cap) duration = cap;

        Volatile.Write(ref _relaxedType, (int)type);
        ExtendRelaxedWindow(duration, $"{type} for {duration.TotalSeconds}s");
    }

    /// <summary>
    /// An announced disruption has finished: replace the remaining window with the post-event tail.
    /// </summary>
    /// <remarks>
    /// Replace rather than extend-or-shorten. The server has told us the operation completed, so whatever it
    /// previously said about duration is stale - but the tail still applies, because completion is when every
    /// other client that received the same notification re-engages.
    /// </remarks>
    internal void OnMaintenanceWindowClosed(MaintenanceNotificationType type, long? sequenceId)
    {
        if (!TryClaimSequenceId(type, sequenceId)) return;

        Volatile.Write(ref _relaxedType, (int)type);
        var tail = Multiplexer.RawConfig.MaintenancePostEventRelaxedDuration;
        if (tail <= TimeSpan.Zero)
        {
            Volatile.Write(ref _relaxedDeadlineTicks, 0);
            Multiplexer.Trace($"{type}: relaxation ended", ToString());
            return;
        }

        var deadline = NudgeFromZero(unchecked(Environment.TickCount + (int)tail.TotalMilliseconds));
        Volatile.Write(ref _relaxedDeadlineTicks, deadline);
        Multiplexer.Trace($"{type}: relaxation continues for {tail.TotalSeconds}s (post-event)", ToString());
    }

    private void ExtendRelaxedWindow(TimeSpan duration, string cause)
    {
        var candidate = NudgeFromZero(unchecked(Environment.TickCount + (int)duration.TotalMilliseconds));
        while (true)
        {
            var current = Volatile.Read(ref _relaxedDeadlineTicks);

            // a new notification never shortens an existing window: a FAILING_OVER arriving inside a longer
            // MIGRATING window must not cut it short
            if (current != 0 && unchecked(candidate - current) <= 0) return;
            if (Interlocked.CompareExchange(ref _relaxedDeadlineTicks, candidate, current) == current)
            {
                Multiplexer.Trace($"timeouts relaxed: {cause}", ToString());
                return;
            }
        }
    }

    /// <summary>
    /// Zero is the "no window" sentinel, so a deadline that lands on it moves by a tick.
    /// </summary>
    private static int NudgeFromZero(int ticks) => ticks == 0 ? 1 : ticks;

    /// <summary>
    /// Whether this notification is new, per the conservative dedup described on <see cref="_lastSequenceIds"/>.
    /// </summary>
    private bool TryClaimSequenceId(MaintenanceNotificationType type, long? sequenceId)
    {
        // no id, no dedup - but the notification still counts. Dropping an announced disruption because we
        // could not read a field whose meaning nobody has defined would be the wrong way round
        if (sequenceId is not { } id) return true;

        var index = (int)type;
        lock (_maintenanceSync)
        {
            var ids = _lastSequenceIds ??= new long[MaintenanceNotificationTypeCount];
            if ((uint)index >= (uint)ids.Length) return true; // unknown type: don't dedup what we can't index

            // note "<=", not "<": a replay carries the id we already acted on
            if (ids[index] != 0 && id <= ids[index])
            {
                Multiplexer.Trace($"{type}: ignoring replayed sequence id {id}", ToString());
                return false;
            }

            ids[index] = id;
            return true;
        }
    }

    private const int MaintenanceNotificationTypeCount = 8; // None + the seven notification types
}
