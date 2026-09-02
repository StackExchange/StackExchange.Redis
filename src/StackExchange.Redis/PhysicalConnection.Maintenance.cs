using System;
using System.Collections.Generic;
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
        List<ClusterSlotMigration>? migrations = null;
        int count = 0;
        while (reader.SafeTryMoveNext())
        {
            count++;
            if (!reader.IsScalar)
            {
                // SMIGRATED nests: [type, seq, [[source, target, slots], ...]]. Everything else is flat, and
                // nesting there means a frame we do not understand - so the guard stays for those, since
                // guessing at an unknown shape is how a parser starts inventing data
                if (kind is PushKind.SlotMigrated or PushKind.SlotMigrating && migrations is null)
                {
                    // note the reader has to be moved *past* the aggregate: enumerating the children does not
                    // advance it, so without this the loop walks back into the triplets we just read and
                    // mistakes them for further top-level elements
                    migrations = ReadSlotMigrations(ref reader);
                    continue;
                }

                Trace($"{kind}: non-scalar element {count}");
                return OutOfBandResult.Handled;
            }

            var element = reader.IsNull ? null : reader.ReadString();
            switch (count)
            {
                case 1: e1 = element; break;
                case 2: e2 = element; break;
                case 3: e3 = element; break;
            }
        }

        var type = ToNotificationType(kind);

        // A missing or unreadable sequence id is *not* fatal. The specs say every shape carries one, but
        // go-redis - which runs against real servers - length-checks these frames at two elements and reads
        // no sequence number at all for the shard notifications. So a client that drops a frame for want of a
        // seq is stricter than one that demonstrably works. Without it we lose only dedup, which is our own
        // invention anyway; the disruption being announced is the part that matters.
        long? sequenceId = TryParseInt64(e1, out var parsedSequenceId) ? parsedSequenceId : null;
        if (sequenceId is null)
        {
            OnMaintenanceNotificationDropped(type, $"no readable sequence id in a {count + 1}-element frame; continuing without dedup");
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
            newEndPoint = ParseMigrationEndPoint(payload);
            if (newEndPoint is null)
            {
                Trace($"{kind}: no usable endpoint in '{payload}'");
            }
        }

        var time = timeSeconds is { } value ? TimeSpan.FromSeconds(value) : (TimeSpan?)null;
        var raw = Describe(kind, sequenceId, timeSeconds, payload);
        Trace($"maintenance notification: {raw}");
        OnDetailLog($"maintenance notification: {raw}");

        // A notification that arrives before the bridge reports established is the server's *catch-up* copy:
        // it retains the completion of a shard-scoped event and replays it to whoever opts in next, with no
        // measured age limit (the same FAILED_OVER came back 90 minutes later). Distinguishing the two matters
        // for the completions, which otherwise relax timeouts on a brand-new connection for an event that
        // finished long ago; the starters are unaffected, since nothing retains them.
        var isCatchUp = BridgeCouldBeNull?.IsConnected != true;

        // relax before reporting: the event handler is consumer code, and the window should already be open
        // by the time anyone sees the notification that opened it
        if (server is not null)
        {
            // Logged, not merely traced: Trace is [Conditional("VERBOSE")], so until now a received
            // notification was invisible in an ordinary deployment - including to the log-based verification
            // our own documentation recommends. This is the line that answers "why is my new connection
            // relaxed?", so it names the catch-up case explicitly.
            muxer.Logger?.LogInformationMaintenanceNotificationReceived(
                new(server), type, sequenceId ?? -1, isCatchUp ? " (catch-up)" : string.Empty);

            if (IsWindowOpening(type))
            {
                var isNew = server.OnMaintenanceWindowOpened(type, sequenceId, time);

                // ...and MOVING alone means "this endpoint is going away", which is worth acting on rather than
                // waiting to be disconnected.
                //
                // Only when the notification is *new*, and that is load-bearing rather than tidy. A server
                // re-sends MOVING to a connection that opts in while the window is still open - measured, and
                // the handoff replaces the connection, so acting on the repeat is a feedback loop: recycle,
                // reconnect, get told again, recycle. It produced twelve recycles from one event on a real
                // deployment before this guard. The per-server sequence dedup already knew it was a repeat; the
                // handoff simply was not asking.
                if (isNew && type == MaintenanceNotificationType.Moving)
                {
                    server.OnMovingAnnounced(time, newEndPoint, this);
                }
            }
            else if (IsWindowClosing(type))
            {
                server.OnMaintenanceWindowClosed(type, sequenceId, isCatchUp);

                // ...and if slots moved away from us, learn the new topology rather than waiting to be told
                // by a -MOVED. Scoped and jittered inside OnSlotsMigratedAway; see its remarks for why this
                // is the cluster family only
                if (type == MaintenanceNotificationType.SlotMigrated && migrations is not null)
                {
                    server.OnSlotsMigratedAway(migrations);
                }
            }
        }

        // Per-server work above, one event below: relaxation is per-connection and every connection is told,
        // but a consumer wants one callback per logical event rather than one per proxy that mentioned it
        if (muxer.TryClaimMaintenanceEvent(type, sequenceId))
        {
            var evt = new PushMaintenanceEvent(type, sequenceId ?? 0, server?.EndPoint, time, newEndPoint, payload, raw, migrations);
            muxer.OnServerMaintenanceEvent(evt);
        }
        else
        {
            Trace($"{kind} seq {sequenceId} already reported by another node; not raising again");
        }

        return OutOfBandResult.Handled;
    }

    /// <summary>
    /// Reads the nested <c>[[source, target, slots], ...]</c> form of a cluster slot-migration notification.
    /// </summary>
    /// <remarks>
    /// A malformed triplet is skipped rather than abandoning the whole notification - the same choice go-redis
    /// makes, and the right one: the other triplets are still actionable, and one bad entry should not lose a
    /// migration we could have applied. The slot list is a flat comma-and-range string inside each triplet.
    /// </remarks>
    private List<ClusterSlotMigration> ReadSlotMigrations(ref RespReader reader)
    {
        var results = new List<ClusterSlotMigration>();
        var outer = reader.AggregateChildren();
        while (outer.MoveNext())
        {
            var triplet = outer.Value;
            if (!triplet.IsAggregate)
            {
                Trace("slot migration: expected a triplet");
                continue;
            }

            string? source = null, target = null, slots = null;
            int index = 0;
            var inner = triplet.AggregateChildren();
            while (inner.MoveNext())
            {
                var child = inner.Value;
                var text = child.IsScalar && !child.IsNull ? child.ReadString() : null;
                switch (index++)
                {
                    case 0: source = text; break;
                    case 1: target = text; break;
                    case 2: slots = text; break;
                }
            }

            if (index < 3)
            {
                Trace($"slot migration: {index}-element triplet, skipped");
                continue;
            }

            var ranges = SlotRange.TryParseList(slots, out var parsed) ? parsed : [];
            results.Add(new ClusterSlotMigration(ParseMigrationEndPoint(source), ParseMigrationEndPoint(target), ranges, slots));
        }

        // leave the caller's reader after the aggregate, so it can carry on with any trailing elements
        outer.MovePast(out reader);
        return results;
    }

    /// <summary>
    /// Parses one end of a migration, treating the placeholder forms as "not named" rather than as an error.
    /// </summary>
    private static EndPoint? ParseMigrationEndPoint(string? value)
        => string.IsNullOrEmpty(value) || value == "?" || !Format.TryParseEndPoint(value, out var parsed) || IsPlaceholderEndPoint(parsed)
            ? null
            : parsed;

    private static bool IsPlaceholderEndPoint(EndPoint endpoint) => endpoint switch
    {
        DnsEndPoint dns => dns.Port == 0 || dns.Host is "" or "?",
        IPEndPoint ip => ip.Port == 0,
        _ => false,
    };

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

    private static string Describe(PushKind kind, long? sequenceId, long? timeSeconds, string? payload)
    {
        var sb = new System.Text.StringBuilder(kind.ToString().ToUpperInvariant());
        sb.Append(" seq=").Append(sequenceId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?");
        if (timeSeconds is { } seconds) sb.Append(" time=").Append(seconds).Append('s');
        if (payload is not null) sb.Append(' ').Append(payload);
        return sb.ToString();
    }
}
