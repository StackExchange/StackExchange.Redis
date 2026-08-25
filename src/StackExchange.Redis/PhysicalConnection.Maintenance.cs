using System;
using System.Globalization;
using System.Net;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Maintenance;

namespace StackExchange.Redis;

internal sealed partial class PhysicalConnection
{
    /// <summary>
    /// Reads a maintenance notification and raises it as an event; observation only.
    /// </summary>
    /// <remarks>
    /// Parsed leniently on purpose. These frames are dispatched here *before* anything reads element 1 as a
    /// channel name, because element 1 is a sequence number - so a malformed one must be swallowed here, and
    /// never fall through to the command matcher, where it would take a reply belonging to something else.
    /// The shapes are (per the contract, all elements after the type being scalars):
    /// <list type="bullet">
    /// <item><c>MOVING seq time endpoint</c></item>
    /// <item><c>MIGRATING seq time shards</c>, <c>FAILING_OVER seq time shards</c></item>
    /// <item><c>MIGRATED seq shards</c>, <c>FAILED_OVER seq shards</c></item>
    /// <item><c>SMIGRATING seq slots</c>, <c>SMIGRATED seq slots</c></item>
    /// </list>
    /// Integers may arrive as <c>:</c> or as a bulk string, trailing elements are explicitly allowed, and the
    /// endpoint may be an explicit null. So the type decides *whether* a time is expected rather than the
    /// content deciding it - a slot list of "123" must not be mistaken for a duration - but a notification
    /// that omits or adds one is still accepted.
    /// </remarks>
    private OutOfBandResult OnMaintenanceNotification(ConnectionMultiplexer muxer, PushKind kind, ref RespReader reader)
    {
        _readStatus = ReadStatus.MaintenanceNotification;

        // at most three elements follow the type in any defined shape; anything beyond that is ignored
        string? e1 = null, e2 = null, e3 = null;
        int count = 0;
        while (reader.SafeTryMoveNext())
        {
            if (!reader.IsScalar)
            {
                // no defined notification nests anything; give up rather than guess
                Trace($"{kind}: non-scalar element {count + 1}");
                return OutOfBandResult.Handled;
            }

            var element = reader.IsNull ? null : reader.ReadString();
            switch (++count)
            {
                case 1: e1 = element; break;
                case 2: e2 = element; break;
                case 3: e3 = element; break;
            }
        }

        var type = ToNotificationType(kind);
        if (count == 0 || !TryParseInt64(e1, out var sequenceId))
        {
            // the sequence id is the one field every shape has; without it we know too little to report
            OnMaintenanceNotificationDropped(type, $"no sequence id in a {count + 1}-element frame");
            return OutOfBandResult.Handled;
        }

        long? timeSeconds = null;
        string? payload;
        if (CarriesTime(type))
        {
            if (TryParseInt64(e2, out var seconds))
            {
                timeSeconds = seconds;
                payload = e3;
            }
            else
            {
                // tolerated: a server that omits the time it was supposed to send
                payload = e3 ?? e2;
            }
        }
        else if (count >= 3 && TryParseInt64(e2, out var seconds))
        {
            // tolerated the other way: a time on a notification the contract says has none
            timeSeconds = seconds;
            payload = e3;
        }
        else
        {
            payload = e3 ?? e2;
        }

        var server = BridgeCouldBeNull?.ServerEndPoint;
        EndPoint? newEndPoint = null;
        if (type == MaintenanceNotificationType.Moving && !string.IsNullOrEmpty(payload))
        {
            // "?" and an empty host are the contract's placeholders for "no address", and are no more
            // dialable than the null form; they must never be taken to mean "the server that told us"
            if (payload != "?" && Format.TryParseEndPoint(payload, out var parsed) && !IsPlaceholder(parsed))
            {
                newEndPoint = parsed;
            }
            else
            {
                Trace($"{kind}: no usable endpoint in '{payload}'");
            }
        }

        var time = timeSeconds is { } value ? TimeSpan.FromSeconds(value) : (TimeSpan?)null;
        var raw = Describe(kind, sequenceId, timeSeconds, payload);
        Trace($"maintenance notification: {raw}");
        OnDetailLog($"maintenance notification: {raw}");

        // relax before reporting: the event handler is consumer code, and the window should already be open
        // by the time anyone sees the notification that opened it
        if (server is not null)
        {
            if (IsWindowOpening(type))
            {
                server.OnMaintenanceWindowOpened(type, sequenceId, time);
            }
            else if (IsWindowClosing(type))
            {
                server.OnMaintenanceWindowClosed(type, sequenceId);
            }
        }

        var evt = new PushMaintenanceEvent(type, sequenceId, server?.EndPoint, time, newEndPoint, payload, raw);
        muxer.OnServerMaintenanceEvent(evt);
        return OutOfBandResult.Handled;

        static bool IsPlaceholder(EndPoint endpoint) => endpoint switch
        {
            DnsEndPoint dns => dns.Port == 0 || dns.Host is "" or "?",
            IPEndPoint ip => ip.Port == 0,
            _ => false,
        };
    }

    private void OnMaintenanceNotificationDropped(MaintenanceNotificationType type, string reason)
    {
        // never fatal: a notification we cannot read is a diagnostic, not a protocol failure - the frame has
        // been consumed either way, so the connection is not at risk
        Trace($"dropped {type} notification: {reason}");
        OnDetailLog($"dropped {type} notification: {reason}");
    }

    private static bool TryParseInt64(string? value, out long result)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// Whether this notification announces a disruption starting (or still running).
    /// </summary>
    /// <remarks>
    /// <see cref="MaintenanceNotificationType.Moving"/> is an opener with no closer: its window can only end
    /// by the deadline the server gave us, or - later - by the handoff completing.
    /// </remarks>
    private static bool IsWindowOpening(MaintenanceNotificationType type) => type is
        MaintenanceNotificationType.Moving
        or MaintenanceNotificationType.Migrating
        or MaintenanceNotificationType.FailingOver
        or MaintenanceNotificationType.SlotMigrating;

    /// <summary>
    /// Whether this notification announces that a disruption has finished.
    /// </summary>
    private static bool IsWindowClosing(MaintenanceNotificationType type) => type is
        MaintenanceNotificationType.Migrated
        or MaintenanceNotificationType.FailedOver
        or MaintenanceNotificationType.SlotMigrated;

    /// <summary>
    /// Whether the contract gives this notification a <c>time</c> element.
    /// </summary>
    private static bool CarriesTime(MaintenanceNotificationType type) => type is
        MaintenanceNotificationType.Moving
        or MaintenanceNotificationType.Migrating
        or MaintenanceNotificationType.FailingOver;

    private static MaintenanceNotificationType ToNotificationType(PushKind kind) => kind switch
    {
        PushKind.Moving => MaintenanceNotificationType.Moving,
        PushKind.Migrating => MaintenanceNotificationType.Migrating,
        PushKind.Migrated => MaintenanceNotificationType.Migrated,
        PushKind.FailingOver => MaintenanceNotificationType.FailingOver,
        PushKind.FailedOver => MaintenanceNotificationType.FailedOver,
        PushKind.SlotMigrating => MaintenanceNotificationType.SlotMigrating,
        PushKind.SlotMigrated => MaintenanceNotificationType.SlotMigrated,
        _ => MaintenanceNotificationType.None,
    };

    private static string Describe(PushKind kind, long sequenceId, long? timeSeconds, string? payload)
    {
        var sb = new System.Text.StringBuilder(kind.ToString().ToUpperInvariant());
        sb.Append(" seq=").Append(sequenceId);
        if (timeSeconds is { } seconds) sb.Append(" time=").Append(seconds).Append('s');
        if (payload is not null) sb.Append(' ').Append(payload);
        return sb.ToString();
    }
}
