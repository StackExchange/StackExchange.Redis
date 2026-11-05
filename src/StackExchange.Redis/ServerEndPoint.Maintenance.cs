using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// The effective mode for this server, which is not always the configured one.
    /// </summary>
    /// <remarks>
    /// Forced to <see cref="MaintenanceNotificationMode.Disabled"/> inside a multi-group (geo-redundant)
    /// connection, **including when a caller asked for it explicitly**. How maintenance notifications and
    /// cross-region failover should interact is not yet defined - upstream disables the feature alongside
    /// geographic failover, and until there is an agreed answer, acting on a handoff in one region while a
    /// group is deciding whether to fail away from it is a risk with no test behind it.
    /// <para>
    /// This is deliberately the *one* place that decides, because everything else keys off this property:
    /// whether the opt-in is sent, and whether
    /// <see cref="MaintenanceNotificationMode.Enabled"/>'s "reject the connection" rule applies. Suppressing
    /// the request without also suppressing that rule would turn an explicit opt-in into a connection that
    /// cannot be made.
    /// </para>
    /// </remarks>
    private MaintenanceNotificationMode MaintenanceMode
        => Multiplexer.IsGroupMember
            ? MaintenanceNotificationMode.Disabled
            : Multiplexer.RawConfig.MaintenanceNotifications;

    /// <summary>
    /// Whether anything can change a command's timeout against this server while it waits: relaxation and the
    /// handoff cover both come from notifications, so with the feature disabled a sync waiter can take its whole
    /// budget in one wait rather than re-reading it.
    /// </summary>
    internal bool MaintenanceMayChangeTimeouts => MaintenanceMode != MaintenanceNotificationMode.Disabled;

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

    /// <summary>
    /// The server agreed to send them.
    /// </summary>
    /// <remarks>
    /// Logged as well as recorded, so that the connect log answers "is this actually on?" outright. Previously
    /// only the *refusal* was logged, which meant a working feature left no trace and could only be inferred
    /// from the absence of a complaint - and that is indistinguishable from never having asked.
    /// </remarks>
    /// <summary>
    /// The wire value for the configured <c>moving-endpoint-type</c>, or null to send no preference.
    /// </summary>
    private RedisValue MaintenanceMovingEndpointTypeLiteral(PhysicalConnection connection)
    {
        var configured = Multiplexer.RawConfig.MaintenanceMovingEndpointType;
        if (configured == MaintenanceEndpointType.Auto)
        {
            // classify the address we actually reached, not the endpoint we dialled - the latter is usually a
            // name, and where it resolved to is what decides whether we are inside the deployment's network
            configured = MaintenanceEndpointTypeResolver.Derive(
                (connection.VolatileSocket?.RemoteEndPoint as IPEndPoint)?.Address,
                connection.IsEncrypted);
        }

        return ToLiteral(configured);
    }

    private static RedisValue ToLiteral(MaintenanceEndpointType type) => type switch
    {
        MaintenanceEndpointType.InternalIp => RedisLiterals.internal_ip,
        MaintenanceEndpointType.InternalFqdn => RedisLiterals.internal_fqdn,
        MaintenanceEndpointType.ExternalIp => RedisLiterals.external_ip,
        MaintenanceEndpointType.ExternalFqdn => RedisLiterals.external_fqdn,
        MaintenanceEndpointType.None => RedisLiterals.none,
        _ => RedisValue.Null, // ServerDefault: a bare ON, which is what we have always sent
    };

    internal void OnMaintenanceNotificationsAccepted(PhysicalConnection connection)
    {
        _maintenanceNotificationsActive = true;
        Multiplexer.Logger?.LogInformationMaintenanceNotificationsAccepted(new(this));
    }

    /// <summary>
    /// The server declined our request. Recorded rather than acted on: whether that matters is a question for
    /// <see cref="ReconcileMaintenanceNotifications"/>, which sees the negotiated protocol too.
    /// </summary>
    internal void OnMaintenanceNotificationsRefused(PhysicalConnection connection, string reason)
    {
        _maintenanceNotificationsActive = false;
        _maintenanceNotificationsRefusal = reason;

        // via the configured logger, not OnDetailLog: that is [Conditional("PARSE_DETAIL")] and compiles away
        // in any normal build, so for as long as it was the only report of a refusal, the reason a server
        // declined was invisible to everybody who was not debugging the parser.
        Multiplexer.Logger?.LogInformationMaintenanceNotificationsRefused(new(this), reason);
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
    private TickDeadline _relaxedDeadline;

    // the notification that last touched the window, reported on faults that happen inside it
    private int _relaxedType;

    // Dedup state, per notification type. No specification defines the sequence ids, but they were observed on
    // Enterprise 8.6.2 to be monotonic per database and shared across types, and to carry the same value on
    // every node broadcasting a given event - so they identify the event, which is exactly what dedup needs.
    // Keyed per type anyway: within a type the ids are still monotonic, and a per-type key cannot mistake one
    // node's earlier event for a replay of another's later one. Allocated on first notification, so a server
    // that never sees one pays nothing.
    private long[]? _lastSequenceIds;
    private bool[]? _haveSequenceIds; // zero is a real sequence number, so "unset" needs its own bit
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
        var result = configuredMilliseconds;
        if (GetRelaxedRemaining() > 0)
        {
            var relaxed = (int)Multiplexer.RawConfig.MaintenanceRelaxedTimeout.TotalMilliseconds;
            if (relaxed > result) result = relaxed;
        }

        // a handoff hold diverts commands off a live connection into the backlog; whatever the relaxation setting
        // (zero disables it), those commands must not be timed out by the hold itself - see the overload taking
        // the message, which applies the hold's cover to them and to them alone
        return result;
    }

    /// <summary>
    /// The effective timeout for a particular command: as <see cref="GetEffectiveTimeoutMilliseconds(int)"/>, and
    /// for a command a maintenance handoff held (<see cref="Message.HeldForHandoff"/>) no sooner than the handoff's
    /// deadline - the hold, the rest of the window and one connect attempt, whatever the relaxation setting: a
    /// relaxed timeout of zero is the documented way to turn relaxation off, and a sub-second configured timeout with
    /// the hold's one-second floor would otherwise time out exactly the work the hold is protecting. The deadline is
    /// stamped on the command when it is held, so it holds for as long as the command lives and nothing that happens
    /// to the handoff afterwards can withdraw it. Only such a command: an ordinary command on the same server,
    /// including one issued by an application that has turned relaxation off and runs short timeouts, keeps its
    /// configured timeout.
    /// </summary>
    internal static int GetEffectiveTimeoutMilliseconds(int effectiveMilliseconds, Message message)
    {
        var deadline = message.HoldCoverDeadlineTicks;
        if (deadline != 0)
        {
            var floor = unchecked(deadline - message.GetWriteTime());
            if (floor > effectiveMilliseconds) effectiveMilliseconds = floor;
        }
        return effectiveMilliseconds;
    }

    /// <summary>
    /// Milliseconds of relaxation left, or zero if none; clears an expired window as a side-effect.
    /// </summary>
    private int GetRelaxedRemaining() => _relaxedDeadline.RemainingMilliseconds();

    /// <summary>
    /// An announced disruption has started (or is still running): open or extend the relaxed window.
    /// </summary>
    /// <returns>
    /// Whether this notification was new. A replay extends nothing and, importantly, must not be *acted* on -
    /// see the handoff, where re-acting on a repeat is a feedback loop rather than merely wasted work.
    /// </returns>
    internal bool OnMaintenanceWindowOpened(MaintenanceNotificationType type, long? sequenceId, TimeSpan? time)
    {
        if (!TryClaimSequenceId(type, sequenceId)) return false;

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
        return true;
    }

    /// <summary>
    /// An announced disruption has finished: replace the remaining window with the post-event tail.
    /// </summary>
    /// <remarks>
    /// Replace rather than extend-or-shorten. The server has told us the operation completed, so whatever it
    /// previously said about duration is stale - but the tail still applies, because completion is when every
    /// other client that received the same notification re-engages.
    /// </remarks>
    /// <param name="type">Which completion this is.</param>
    /// <param name="sequenceId">The server's sequence number, for repeat detection.</param>
    /// <param name="isCatchUp">
    /// Whether this arrived as part of establishing the connection rather than on a live one - in which case
    /// it is the server's retained copy of an event that has already finished, and gets no tail.
    /// </param>
    internal void OnMaintenanceWindowClosed(MaintenanceNotificationType type, long? sequenceId, bool isCatchUp)
    {
        if (!TryClaimSequenceId(type, sequenceId)) return;

        // A completion delivered while we were still connecting is the server's catch-up channel, and
        // measurement says that channel has no age limit: the same FAILED_OVER was replayed to fresh
        // connections three hours after the failover, and completions carry no time field, so nothing in the
        // frame distinguishes "just happened" from "happened this morning". Without this, every new
        // connection to a database that had ever failed over began life with the full post-event tail of
        // relaxed timeouts, and reported any timeout inside it as caused by maintenance that was long over.
        //
        // Note this declines to *open* a window rather than closing one. Relaxation belongs to the
        // ServerEndPoint and is shared by both bridges, so a catch-up arriving on a reconnecting subscription
        // bridge must not cancel a window that a live notification opened on the established interactive one.
        if (isCatchUp)
        {
            Multiplexer.Trace($"{type}: catch-up copy of a finished event; no relaxation", ToString());
            return;
        }

        Volatile.Write(ref _relaxedType, (int)type);
        var tail = Multiplexer.RawConfig.MaintenancePostEventRelaxedDuration;

        // While a handoff is in flight, the drain depends on the relaxation it extended: a completion for some other
        // operation arriving mid-drain (a MIGRATED for a shard move) must not pull the deadline back under the drain's
        // cap and have the sweep fail the very commands the drain is waiting for. So during a handoff a close can
        // lengthen the window but not shorten it; the replace semantics above apply the rest of the time.
        var handoffInFlight = Volatile.Read(ref _handoffInFlight) != 0;
        if (tail <= TimeSpan.Zero)
        {
            if (!handoffInFlight) _relaxedDeadline.SetIn(TimeSpan.Zero); // ended now, and recorded as such for fault attribution
            Multiplexer.Trace($"{type}: relaxation ended", ToString());
            return;
        }

        if (handoffInFlight) _relaxedDeadline.TryExtendBy(tail, out _);
        else _relaxedDeadline.SetIn(tail);
        Multiplexer.Trace($"{type}: relaxation continues for {tail.TotalSeconds}s (post-event)", ToString());
    }

    private volatile EndPoint? _handoffTarget;
    private TickDeadline _handoffTargetExpiry;
    private int _handoffInFlight, _handoffRecycles;

    /// <summary>
    /// Where the next connection attempt should go, when a server has named a replacement.
    /// </summary>
    /// <remarks>
    /// Deliberately a *connect* target rather than a new endpoint in the collection. This
    /// <see cref="ServerEndPoint"/> keeps its identity, its place in server selection, and - the part that
    /// matters most - its TLS host: certificate validation and SNI are derived from
    /// <see cref="ServerEndPoint.EndPoint"/>, so moving the socket without moving the endpoint means a handoff
    /// cannot perturb them. Adding the moved-to address as an endpoint would; that is a documented trap in the
    /// cross-client contract.
    /// <para>
    /// Expires, and that is not decoration. Without it, a server that named an address which turns out to be
    /// unreachable would pin this endpoint to it for the lifetime of the multiplexer, because every reconnect
    /// would keep trying the same dead target. On expiry we fall back to resolving the endpoint normally, which
    /// is what we would have done anyway. Until then it stays, whatever establishes: both bridges reconnect
    /// through it, and the second one's reconnect must not be sent back to DNS - still naming the node being
    /// retired - because the first one's replacement happened to establish first.
    /// </para>
    /// </remarks>
    internal EndPoint? HandoffTarget
    {
        get
        {
            var target = _handoffTarget;
            if (target is null) return null;

            if (!_handoffTargetExpiry.IsActive)
            {
                _handoffTarget = null; // expired; resolve the endpoint the usual way from here on
                return null;
            }

            return target;
        }
    }

    /// <summary>
    /// Points the next connection attempt at a named replacement, for as long as the announced window lasts.
    /// </summary>
    internal void SetHandoffTarget(EndPoint target, TimeSpan window)
    {
        _handoffTargetExpiry.SetIn(window < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : window);
        _handoffTarget = target;
    }

    /// <summary>
    /// Drops the pin when a later <c>MOVING</c> names no successor: the pinned node is then, as likely as not, the
    /// very one now being retired, and a reconnect dialled through the stale pin would land on it.
    /// </summary>
    private void ClearHandoffTarget() => _handoffTarget = null;

    private volatile string? _lastHandoffOutcome;

    /// <summary>
    /// What the last <c>MOVING</c> handoff decided, and why.
    /// </summary>
    /// <remarks>
    /// Recorded rather than only traced because <c>Multiplexer.Trace</c> is <c>[Conditional("VERBOSE")]</c> - it
    /// compiles away in any normal build, so a handoff that misbehaves in production would leave no trace at
    /// all. This is the minimum that survives: what was decided, readable afterwards.
    /// </remarks>
    internal string? LastHandoffOutcome => _lastHandoffOutcome;

    /// <summary>How many times a handoff has replaced this server's connections.</summary>
    internal int HandoffRecycles => Volatile.Read(ref _handoffRecycles);

    /// <summary>
    /// Acts on a <c>MOVING</c>: find where to go, then get off this connection before we are pushed.
    /// </summary>
    /// <remarks>
    /// The value here is entirely in the *timing*. Without it we already survive a <c>MOVING</c> - the socket
    /// closes and we reconnect - but the reconnect happens when the server decides, and re-resolves to whatever
    /// DNS says at that moment, which has been measured as still naming the node being retired. Measured on a
    /// real deployment: <c>MOVING</c> arrives about six seconds in, DNS moves somewhere between four and
    /// nineteen seconds later, and the socket closes seventeen to nineteen seconds after the notification. So
    /// the window exists to be *used*, and using it means waiting for DNS to move and then choosing the moment.
    /// <para>
    /// Fire-and-forget by design: this runs while the connection it concerns is still serving commands, and the
    /// notification is delivered on the read loop, which must not wait for a DNS poll.
    /// </para>
    /// </remarks>
    internal void OnMovingAnnounced(TimeSpan? window, EndPoint? successor, PhysicalConnection connection)
    {
        // One at a time per server. A rolling operation delivers one MOVING per connection, so a second one
        // arriving while a handoff is in flight is a repeat or a much later event; either way, starting a
        // second poll against the same endpoint achieves nothing.
        if (Interlocked.CompareExchange(ref _handoffInFlight, 1, 0) != 0)
        {
            Multiplexer.Trace("MOVING: a handoff is already in flight", ToString());
            return;
        }

        var budget = window is { } value && value > TimeSpan.Zero
            ? value
            : Multiplexer.RawConfig.MaintenanceRelaxedTimeout; // no window given: use the relaxation floor
        if (budget > MaxHandoffWindow) budget = MaxHandoffWindow; // an absurd announced window must not overflow a delay or a deadline
        var current = connection.TryGetRemoteEndPoint(); // null if the socket is gone or not connected: nothing to compare against

        _ = HandoffAsync(budget, successor, current);
    }

    private async Task HandoffAsync(TimeSpan window, EndPoint? successor, IPEndPoint? currentEndPoint)
    {
        var watch = ValueStopwatch.StartNew();
        var replaced = false;
        try
        {
            // Spread the fleet, but only by a fraction of the window - see MaintenanceHandoff.GetJitter for why
            // a flat delay is wrong here.
            var jitter = MaintenanceHandoff.GetJitter(window, RandomFor(this));
            if (jitter > TimeSpan.Zero) await Task.Delay(jitter).ForAwait();

            var remaining = window - jitter;
            var decision = await MaintenanceHandoff.DecideAsync(
                EndPoint,
                successor,
                currentEndPoint?.Address,
                remaining,
                pollInterval: TimeSpan.FromSeconds(1), // records carry a 5s TTL, so this is several looks per record
                resolve: Multiplexer.AddressResolver,
                log: message => Multiplexer.Trace(message, ToString())).ForAwait();

            // the decision may have spent a good part of the window polling DNS (4 to 19 seconds of a 15 second
            // window has been measured); what the drain has to work with is what is actually left
            remaining = Remaining(window, watch);

            Multiplexer.Trace($"MOVING: {decision}", ToString());
            _lastHandoffOutcome = decision.ToString();

            // Trace is [Conditional("VERBOSE")], so without this a handoff leaves no record in a normal build -
            // and a handoff replaces connections, which is exactly the kind of thing somebody needs to be able
            // to find afterwards.
            Multiplexer.Logger?.LogInformationMaintenanceHandoff(new(this), _lastHandoffOutcome);
            switch (decision.Action)
            {
                case HandoffAction.Recycle:
                    // DNS has moved to a new address, so a reconnect reporting the old one is back on the retiring
                    // node; a pin left by an earlier handoff is stale now and must not dial it
                    ClearHandoffTarget();
                    await DrainThenRecycleAsync(remaining, decision.Reason, successor: null, replacementAddressSeen: true, currentEndPoint).ForAwait();
                    replaced = true;
                    break;
                case HandoffAction.RecycleAtHalfWindow:
                    // The contract's rule for "no replacement named", applied where there is nothing better to
                    // go on. Half of the *announced* window, less whatever the jitter already spent.
                    var half = TimeSpan.FromTicks(window.Ticks / 2) - jitter;
                    if (half > TimeSpan.Zero) await Task.Delay(half).ForAwait();
                    // nothing to re-resolve, or nothing known to compare against: no second address has been seen
                    ClearHandoffTarget();
                    await DrainThenRecycleAsync(Remaining(window, watch), decision.Reason, successor: null, replacementAddressSeen: false, currentEndPoint).ForAwait();
                    replaced = true;
                    break;
                case HandoffAction.MoveTo when decision.Target is { } target:
                    // Point the next connection at the named address and replace the connections. Previously
                    // this only re-read the topology and recycled, which measurably did not work: we recycled
                    // at +6.2s, landed back on the node being retired because DNS had not moved yet, and were
                    // closed at +21.6s anyway - exactly the outcome the handoff exists to avoid.
                    SetHandoffTarget(target, remaining);
                    await DrainThenRecycleAsync(remaining, decision.Reason, target, replacementAddressSeen: true, currentEndPoint).ForAwait();
                    replaced = true;
                    break;
                default:
                    // Nothing to do is a legitimate outcome, not a failure: the server closes the socket, the
                    // reconnect re-resolves, and the relaxed window covers the gap.
                    break;
            }

            if (replaced) await WarnIfNotEstablishedInTimeAsync(watch, window).ForAwait();
        }
        catch (Exception ex)
        {
            Multiplexer.OnInternalError(ex, EndPoint);
        }
        finally
        {
            Volatile.Write(ref _handoffInFlight, 0);
        }
    }

    /// <summary>What is left of an announced window; never negative.</summary>
    private static TimeSpan Remaining(TimeSpan window, ValueStopwatch watch)
    {
        var left = window - TimeSpan.FromMilliseconds(watch.ElapsedMilliseconds);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// How long a handoff holds new work and waits for in-flight commands before replacing the connections
    /// regardless: half the relaxed timeout (five seconds by default), never more than half of what is left of the
    /// window, never less than <see cref="DrainFloor"/>, never more than the relaxation ceiling (or the floor, if
    /// that is higher), and never more than the window.
    /// </summary>
    /// <remarks>
    /// Two things pull in opposite directions. A blocking command in flight - BLPOP, XREAD BLOCK, WAIT - is failed
    /// by the replacement if the wait is too short, when the server's close would have let it finish; but every
    /// command the application issues during the wait is held, so a long wait is a latency spike for everyone.
    /// Half the relaxed timeout lets a blocking command with a timeout up to that complete, keeps the hold well
    /// inside the relaxed timeout that applies to the held work for the duration (so nothing held times out
    /// because of the hold), and follows the one setting an application already uses to say how patient it is
    /// prepared to be during maintenance. The window still bounds it, because the server closes the socket when
    /// the window ends whatever we do: a command that cannot finish inside the window fails either way, and holding
    /// past it would only delay everything else. Half of what is left of the window is an absolute ceiling as well,
    /// whatever the relaxed timeout: a generous relaxed timeout must not turn the hold into a full-window stall that
    /// recycles at the deadline with work in flight, which is the failure this whole mechanism exists to remove.
    /// </remarks>
    private TimeSpan DrainCap(TimeSpan budget)
    {
        var cap = TimeSpan.FromTicks(Multiplexer.RawConfig.MaintenanceRelaxedTimeout.Ticks / 2);
        var halfWindow = TimeSpan.FromTicks(budget.Ticks / 2);
        if (cap > halfWindow) cap = halfWindow;
        if (cap < DrainFloor) cap = DrainFloor; // a relaxed timeout of zero disables relaxation; it must not disable the drain
        // the relaxation the drain depends on is itself bounded by the user's ceiling: holding for longer than the
        // sweep will relax would pay the hold's latency and still fail the command. The floor still applies, as for a
        // relaxed timeout of zero: a sub-second ceiling says "do not relax", not "do not drain".
        var ceiling = Multiplexer.RawConfig.MaintenanceRelaxedWindowMax;
        if (ceiling < DrainFloor) ceiling = DrainFloor;
        if (cap > ceiling) cap = ceiling;
        return cap > budget ? budget : cap;
    }

    /// <summary>The least the drain waits for in-flight commands, whatever the relaxed timeout: one round trip on any network, with room to spare.</summary>
    private static readonly TimeSpan DrainFloor = TimeSpan.FromSeconds(1);

    /// <summary>How much longer than the cap the forced pass may wait for the write lock before giving the connection up to the server's close.</summary>
    private static readonly TimeSpan LockGrace = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest MOVING window acted on. Measured windows are 15 seconds; an hour is already absurd, and anything
    /// larger (a buggy or hostile announcement) would overflow the delays, cancellation sources and tick deadlines
    /// downstream, or hold traffic for days.
    /// </summary>
    private static readonly TimeSpan MaxHandoffWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Holds new caller work back, lets what has already been written finish, then replaces the connections.
    /// </summary>
    /// <remarks>
    /// Draining first because the socket is still working: anything already written may still be answered, and
    /// dropping it would fail commands that were about to succeed.
    /// <para>
    /// Holding new work back is what makes the drain finish. Measured live on a connection carrying about 450
    /// operations a second: there was always something in flight, so a drain that waited for "no caller work"
    /// waited out the entire 15s window and then recycled at the deadline - failing the eight commands in flight
    /// at that instant, with no time left to establish the replacement. That is the exact outcome the handoff
    /// exists to avoid. With new work held in the backlog, the written commands complete within a round trip, the
    /// recycle follows, and the held work is written to the replacement once it is established.
    /// </para>
    /// <para>
    /// The wait is bounded by <see cref="DrainCap"/>, and timeouts are relaxed for the hold so that neither the
    /// held work nor a blocking command being waited for is timed out by the hold itself. Past the cap the
    /// connections are replaced regardless, and whatever was still in flight fails as the server's close would
    /// have failed it. Every replacement happens under the bridge's write lock, and only ever to the connection the
    /// hold began on: a replacement established meanwhile is never touched, and if the lock cannot be had at all
    /// the connection is left to the server's close rather than recycled around a writer.
    /// </para>
    /// <para>
    /// Both bridges, not just the one that was told. The measured blast radius is the *node*: four connections
    /// to one proxy, differing only in handshake, all closed simultaneously, and only the ones that had opted in
    /// were warned.
    /// </para>
    /// </remarks>
    /// <param name="budget">What is left of the announced window.</param>
    /// <param name="reason">Why; for the trace.</param>
    /// <param name="successor">The successor the server named and reconnection is pinned to, if any: a connection
    /// dialled through it is the replacement.</param>
    /// <param name="replacementAddressSeen">Whether the handoff has seen a second address - the server named a
    /// successor, or DNS moved to a new address - so that a reconnect reporting the retiring address is known to be
    /// back on the retiring node, and is retired in its turn; see <see cref="PhysicalBridge.TryRetireTargetAsync"/>.
    /// Where no second address was ever involved, the address says nothing and only time tells.</param>
    /// <param name="retiringEndPoint">The address and port of the node being retired, as seen from the connection that
    /// was told, so that a replacement which landed elsewhere is recognised as such.</param>
    private async Task DrainThenRecycleAsync(TimeSpan budget, string reason, EndPoint? successor, bool replacementAddressSeen, IPEndPoint? retiringEndPoint)
    {
        var watch = ValueStopwatch.StartNew();
        // With less than the floor left of the window there is no hold worth having: the server is about to close
        // the socket regardless, so the connections are replaced at once (still under the lock) and the replacement
        // gets what little time remains. Otherwise the hold lasts up to the cap.
        var cap = budget < DrainFloor ? TimeSpan.Zero : DrainCap(budget);

        // The held work, and any blocking command being waited for, must not be timed out by the hold itself, nor
        // by the reconnect that follows it; the relaxed window covers it where relaxation is on, and the handoff's
        // deadline below covers it regardless, stamped on each held command. The reconnect can take until the end
        // of the window plus one connect attempt: a MoveTo pins the socket target to the named successor for the
        // rest of the window, and a successor that does not answer is tried until that expires before the endpoint
        // is resolved the ordinary way.
        var relax = cap + Multiplexer.RawConfig.MaintenanceRelaxedTimeout;
        if (relax > Multiplexer.RawConfig.MaintenanceRelaxedWindowMax) relax = Multiplexer.RawConfig.MaintenanceRelaxedWindowMax; // the user's ceiling applies here as on the notification path
        ExtendRelaxedWindow(relax, "MOVING handoff");
        var drainSpan = cap + LockGrace + TimeSpan.FromMilliseconds(Multiplexer.RawConfig.ConnectTimeout);
        var pinned = cap + LockGrace > budget ? cap + LockGrace : budget;
        var cover = pinned + TimeSpan.FromMilliseconds(Multiplexer.RawConfig.ConnectTimeout);
        // the user's ceiling on relaxation bounds the cover too - a huge announced window must not floor the held
        // commands' timeouts for its whole length - but the cover always spans at least the drain itself and one
        // connect attempt, which is what the held commands need; it applies to them alone, so an application that
        // has turned relaxation off keeps its short timeouts for everything else
        var ceiling = Multiplexer.RawConfig.MaintenanceRelaxedWindowMax;
        if (ceiling < drainSpan) ceiling = drainSpan;
        if (cover > ceiling) cover = ceiling;

        // snapshots: Dispose nulls the fields, and a handoff in progress must not fault on a server going away
        var interactiveBridge = interactive;
        var subscriptionBridge = subscription;

        // Whether a socket's remote address says anything about *which node* it reached: only when this handoff has
        // itself seen a second address, and there is no tunnel in the way. Behind a Tunnel every connection reports
        // the tunnel's address; and where the only address ever involved is the one in front of the cluster (a load
        // balancer or VIP) comparing it with the retiring one would make the genuine replacement look like a return
        // to the retiring node forever. Then only time can tell: once the target is gone, what is current is the
        // replacement.
        var addressIdentifiesNode = replacementAddressSeen && Multiplexer.RawConfig.Tunnel is null;
        interactiveBridge?.BeginHold(cover, retiringEndPoint, successor, addressIdentifiesNode);
        subscriptionBridge?.BeginHold(cover, retiringEndPoint, successor, addressIdentifiesNode);

        // One loop per bridge, each with its own lock deadline, run side by side: a writer wedged on one bridge's
        // lock must not spend the other's time, and the other's idle connection must still be retired before the
        // server closes it. Each bridge is released the moment its own connection is settled.
        var interactiveTask = RetireBridgeAsync(interactiveBridge, watch, cap, reason);
        var subscriptionTask = RetireBridgeAsync(subscriptionBridge, watch, cap, reason);
        var interactiveResult = await interactiveTask.ForAwait();
        var subscriptionResult = await subscriptionTask.ForAwait();

        var recycled = interactiveResult.Recycled == true || subscriptionResult.Recycled == true;
        if (recycled) Interlocked.Increment(ref _handoffRecycles);
        Multiplexer.Trace(
            $"MOVING: {(recycled ? "recycled" : "nothing to recycle")} after {watch.ElapsedMilliseconds}ms"
            + (interactiveResult.Forced || subscriptionResult.Forced ? " (still busy at the drain cap)" : " (drained)")
            + (interactiveResult.Recycled is null || subscriptionResult.Recycled is null ? " (write lock not obtained; left to the server)" : ""),
            ToString());
    }

    /// <summary>
    /// What <see cref="RetireBridgeAsync"/> did: whether the connection was recycled (<c>null</c>: the write lock
    /// could not be had, and the connection was left to the server's close), and whether the cap was reached with
    /// work still in flight. A struct rather than a tuple: the library does not reference System.ValueTuple.
    /// </summary>
    private readonly struct BridgeRetirement(bool? recycled, bool forced)
    {
        public bool? Recycled { get; } = recycled;
        public bool Forced { get; } = forced;
    }

    /// <summary>
    /// Retires one bridge's connection once drained, or at the cap regardless, and releases its hold; see
    /// <see cref="DrainThenRecycleAsync"/>.
    /// </summary>
    private async Task<BridgeRetirement> RetireBridgeAsync(PhysicalBridge? bridge, ValueStopwatch watch, TimeSpan cap, string reason)
    {
        if (bridge is null) return new(false, false);
        bool forced = false;
        try
        {
            using var lockDeadline = new CancellationTokenSource(cap + LockGrace);
            while (true)
            {
                var force = watch.ElapsedMilliseconds >= cap.TotalMilliseconds;
                forced |= force;
                var result = await bridge.TryRetireTargetAsync(force, reason, lockDeadline.Token).ForAwait();
                if (result is { } done)
                {
                    // whether we replaced the connection or it went away by itself, a replacement is now on its way,
                    // and the protection stays up for it until it establishes or the span runs out
                    return new(done, forced);
                }
                if (lockDeadline.IsCancellationRequested)
                {
                    // The write lock could not be had even for the forced pass: something is wedged on it. Leaving the
                    // connection to the server's window-end close would fail everything in flight and leave the bridge
                    // dark for the reconnect - the whole window's traffic, to protect at most one wedged writer's
                    // message from a write fault. So, as a last resort, retire without the lock, which is what every
                    // recycle did before handoffs existed. The protection stays up for the diverted work either way.
                    return new(bridge.RetireTargetWithoutLock(reason) ? true : null, forced);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(20)).ForAwait();
            }
        }
        catch (Exception ex)
        {
            // as the give-up above: stop holding new work, keep protecting what was already diverted
            Multiplexer.OnInternalError(ex, EndPoint);
            return new(null, forced);
        }
        finally
        {
            try
            {
                bridge.EndDrain(); // idempotent; one bridge failing to release must not affect the other
            }
            catch (Exception ex)
            {
                Multiplexer.OnInternalError(ex, EndPoint);
            }
        }
    }

    /// <summary>
    /// Warns when a replacement connection is not fully established by the time the announced window ends.
    /// </summary>
    /// <remarks>
    /// The contract asks for the new connection to be *fully established* - handshake complete, not merely
    /// socket-connected - before the deadline, and to say so when that is exceeded. It is the difference
    /// between a handoff that worked and one the server finished for us by closing the socket, and from the
    /// outside those look identical: commands succeed either way, because the relaxed window covers the gap.
    /// <para>
    /// Only reached when connections were actually replaced. Where the decision was to do nothing, waiting for
    /// the server to close the socket *is* the plan, so reconnecting after the deadline is the intended path
    /// rather than a miss, and warning about it would be noise.
    /// </para>
    /// </remarks>
    private async Task WarnIfNotEstablishedInTimeAsync(ValueStopwatch watch, TimeSpan window)
    {
        var deadline = window.TotalMilliseconds;
        while (watch.ElapsedMilliseconds < deadline)
        {
            if (isDisposed) return; // nothing to report about an endpoint that has gone away
            if (IsConnected && IsSubscriberConnectedOrNotRequired) return; // in time
            await Task.Delay(50).ForAwait();
        }

        if (isDisposed || (IsConnected && IsSubscriberConnectedOrNotRequired)) return;

        Multiplexer.Logger?.LogWarningMaintenanceHandoffMissedDeadline(
            new(this),
            (long)window.TotalMilliseconds,
            IsConnected,
            IsSubscriberConnected);
    }

    [ThreadStatic]
    private static Random? _random;

    /// <summary>
    /// A per-thread <see cref="Random"/>, seeded so that two processes handing off at once do not pick the same
    /// jitter.
    /// </summary>
    private static Random RandomFor(ServerEndPoint server) =>
        _random ??= new Random(Environment.TickCount ^ server.GetHashCode());

    private void ExtendRelaxedWindow(TimeSpan duration, string cause)
    {
        // a new notification never shortens an existing window: a FAILING_OVER arriving inside a longer
        // MIGRATING window must not cut it short
        if (_relaxedDeadline.TryExtendBy(duration, out _))
        {
            Multiplexer.Trace($"timeouts relaxed: {cause}", ToString());
        }
    }

    /// <summary>How long after a window closes a fault may still be attributed to it, at minimum.</summary>
    /// <remarks>
    /// The bridge heartbeat raises timeouts on roughly a one-second cadence rather than at the deadline, so a
    /// timeout can be reported up to about a second after the moment it actually expired.
    /// </remarks>
    private const int FaultAttributionFloorMilliseconds = 1000;

    /// <summary>
    /// Which notification to blame for a fault, which is a different question from
    /// <see cref="ActiveMaintenanceType"/>.
    /// </summary>
    /// <remarks>
    /// A command that timed out was outstanding for its whole timeout before anybody noticed, and the
    /// heartbeat that notices runs about once a second - so by the time an exception is built, a window that
    /// covered the command's entire life may already have closed. Reading the *active* type then reports
    /// <see cref="MaintenanceNotificationType.None"/> for a timeout that maintenance plainly caused, which is
    /// the opposite of what this property exists for.
    /// <para>
    /// So a window that closed no longer ago than the command could have been waiting still counts. That is
    /// the tightest bound that catches every genuine case, and it is deliberately a bound rather than a
    /// certainty: without per-message state - which the timeout sweeps rule out, see
    /// <see cref="GetEffectiveTimeoutMilliseconds(int)"/> - a client cannot know whether *this* command overlapped
    /// the window, only whether it could have.
    /// </para>
    /// </remarks>
    /// <param name="timeoutMilliseconds">The timeout that applied to the faulted command.</param>
    internal MaintenanceNotificationType GetMaintenanceTypeForFault(int timeoutMilliseconds)
    {
        var active = ActiveMaintenanceType;
        if (active != MaintenanceNotificationType.None) return active;

        // the deadline is kept after it passes (see TickDeadline), so it is also the record of when the most recent
        // window ended: there is no single moment at which to stamp "the window closed", and the deadline is it
        var ended = _relaxedDeadline.Ticks;
        if (ended == 0) return MaintenanceNotificationType.None; // never had a window at all

        var since = unchecked(Environment.TickCount - ended);
        var grace = Math.Max(timeoutMilliseconds, FaultAttributionFloorMilliseconds);
        return since >= 0 && since <= grace
            ? (MaintenanceNotificationType)Volatile.Read(ref _relaxedType)
            : MaintenanceNotificationType.None;
    }

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
            var have = _haveSequenceIds ??= new bool[MaintenanceNotificationTypeCount];
            if ((uint)index >= (uint)ids.Length) return true; // unknown type: don't dedup what we can't index

            // The "have we seen one" bit is separate because zero is a real sequence number - observed on
            // Enterprise 8.6.2 as the first event of a chain (`>4 $6 MOVING :0 :15 _`). Treating a stored zero
            // as "unset", which an earlier cut did, quietly disabled dedup for whichever notification happened
            // to open the chain.
            //
            // note "<=", not "<": a replay carries the id we already acted on
            if (have[index] && id <= ids[index])
            {
                Multiplexer.Trace($"{type}: ignoring replayed sequence id {id}", ToString());
                return false;
            }

            ids[index] = id;
            have[index] = true;
            return true;
        }
    }

    private const int MaintenanceNotificationTypeCount = 8; // None + the seven notification types

    /// <summary>
    /// How long to smear a notification-triggered topology refresh over.
    /// </summary>
    /// <remarks>
    /// Not configurable, deliberately. The relaxed-window durations are options because their right value is
    /// deployment-specific and we invented them; this is a fixed small smear that exists only so that a fleet
    /// which was all told the same thing at the same instant does not all query the same node at the same
    /// instant. Nobody needs to tune it, and every option is public surface to keep. Promote it if that turns
    /// out to be wrong.
    /// <para>
    /// Note <see cref="ConnectionMultiplexer.ReconfigureIfNeeded"/> already declines while one refresh is in
    /// flight, so the *local* storm is handled; this is purely about the fleet.
    /// </para>
    /// </remarks>
    private static readonly int MaintenanceRefreshJitterMilliseconds = 1000;

    // 1 while a jittered refresh is scheduled and has not yet started
    private int _refreshPending;

    /// <summary>
    /// Whether this server is one of the sources in a slot-migration delta - i.e. whether the movement being
    /// described is movement away from <em>us</em>.
    /// </summary>
    /// <remarks>
    /// Every node in the cluster reports the same movements, so the sender is not implicitly a source and most
    /// notifications describe somebody else. Acting only on our own is what stops a fleet re-reading topology
    /// because one shard moved somewhere unrelated - go-redis makes the same choice.
    /// <para>
    /// Resolution goes through the identity map rather than comparing endpoints directly: a node answers to
    /// its address and to its announced hostname, and the delta may name either.
    /// </para>
    /// </remarks>
    private bool IsSourceOf(IReadOnlyList<ClusterSlotMigration> migrations)
    {
        foreach (var migration in migrations)
        {
            if (migration.Source is { } source && ReferenceEquals(Multiplexer.TryResolveServerEndPoint(source), this))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refreshes the topology after slots have moved away from this server, once the fleet has had a moment to
    /// spread out.
    /// </summary>
    /// <remarks>
    /// Deliberately only for the cluster family: <c>MIGRATED</c> and <c>FAILED_OVER</c> arrive in proxied
    /// deployments where the client addresses a single endpoint, so there is no topology for a refresh to
    /// learn - they get relaxation and nothing else. Endpoints left serving nothing are handled by the
    /// existing absence-based pruning, which a refresh feeds; there is deliberately no second, faster
    /// retirement path here.
    /// </remarks>
    internal void OnSlotsMigratedAway(IReadOnlyList<ClusterSlotMigration> migrations)
    {
        if (migrations.Count == 0 || !IsSourceOf(migrations)) return;

        // Coalesce *before* the jitter, not after. ReconfigureIfNeeded declines only while a refresh is
        // actually in flight, and the jitter spreads a burst of notifications out far enough that each one
        // completes before the next begins - so relying on that alone turns ten notifications into ten
        // topology passes. Found by the test that counts them.
        if (Interlocked.CompareExchange(ref _refreshPending, 1, 0) != 0)
        {
            Multiplexer.Trace("topology refresh already pending; folding this notification into it", ToString());
            return;
        }

        var cause = $"slots migrated from {Format.ToString(EndPoint)}";
        Multiplexer.Trace($"{cause}; refreshing topology after jitter", ToString());

        // fire-and-forget: we are on the read loop, and the refresh must not block it
        _ = RefreshAfterJitterAsync(cause, migrations);
    }

    private async Task RefreshAfterJitterAsync(string cause, IReadOnlyList<ClusterSlotMigration> migrations)
    {
        try
        {
            await Task.Delay(ServerSelectionStrategy.SharedRandom.Next(MaintenanceRefreshJitterMilliseconds)).ForAwait();

            // deliberately *after* the delay - see the remarks on ResubscribeStrandedShardedChannels
            ResubscribeStrandedShardedChannels(migrations);

            // released before the refresh runs, not after: anything that arrives from here on describes a
            // state this pass may not have seen, and deserves its own pass
            Volatile.Write(ref _refreshPending, 0);
            Multiplexer.ReconfigureIfNeeded(EndPoint, fromBroadcast: false, cause);
        }
        catch (Exception ex)
        {
            // a refresh we failed to start is a missed optimization, not a fault: the next -MOVED still works
            Volatile.Write(ref _refreshPending, 0);
            Multiplexer.Trace($"topology refresh after {cause} failed: {ex.Message}", ToString());
        }
    }

    /// <summary>
    /// Re-establishes sharded subscriptions that the slot movement stranded and nothing else recovered.
    /// </summary>
    /// <remarks>
    /// Mostly belt-and-braces: a server that migrates a slot also sends an unsolicited <c>SUNSUBSCRIBE</c>,
    /// and <see cref="PhysicalConnection.OnOutOfBand"/> already resubscribes on that. This adds two things.
    /// It is *pre-emptive* where <c>SMIGRATED</c> arrives first, and it *covers* the case where the
    /// unsolicited unsubscribe never arrives or is lost - in which case the only other signal is a message
    /// that silently stops being delivered, which nothing detects.
    /// <para>
    /// It also knows which slots moved, so only the affected channels are touched rather than everything
    /// subscribed on this server.
    /// </para>
    /// <para>
    /// <b>Why this runs after the jitter, and only for a subscription connected nowhere.</b> An earlier cut ran
    /// it immediately, on the reasoning that a silently-unsubscribed subscriber is a correctness problem while a
    /// stale slot map is only a round trip. Measured against a fake that emits the realistic sequence, that
    /// produced an extra resubscribe per channel - because the unsolicited unsubscribe had already started one,
    /// and mid-flight the subscription still looked like ours. Being a genuine *fallback* means acting only
    /// once the other path has had its chance, which costs a stranded subscription up to the jitter in
    /// recovery time and costs nothing when it was not needed.
    /// </para>
    /// <para>
    /// Measured against the fake's realistic sequence: four (re)subscribes with notifications off, five with
    /// them on. The extra one is this fallback acting on a subscription the unsolicited-unsubscribe path left
    /// attached to nothing - i.e. it is the feature working, not duplicate work. One extra attempt per
    /// stranded channel, not one per notification.
    /// </para>
    /// <para>
    /// Note it resubscribes via <em>this</em> server, not the migration target, reusing
    /// <see cref="RedisSubscriber.ResubscribeToServer"/> unchanged: the outgoing node is the one we know has
    /// the new route, and sending there follows the redirect. The target is named in the notification and
    /// could be dialled directly, but it may be a node we have never seen, or named in a form we cannot dial,
    /// and the redirect path is the one already proven by the <c>SUNSUBSCRIBE</c> case.
    /// </para>
    /// </remarks>
    private void ResubscribeStrandedShardedChannels(IReadOnlyList<ClusterSlotMigration> migrations)
    {
        var subscriptions = Multiplexer.GetSubscriptions();
        if (subscriptions.IsEmpty) return;

        var strategy = Multiplexer.ServerSelectionStrategy;
        foreach (var pair in subscriptions)
        {
            var channel = pair.Key;

            // ordinary pub/sub is not slot-bound, so a slot moving says nothing about it
            if (!channel.IsSharded) continue;

            var slot = strategy.HashSlot(channel);
            if (slot == ServerSelectionStrategy.NoSlot || !IsInMigratedRange(migrations, slot)) continue;

            // Skip only if it is attached somewhere *else* - that means the other path already moved it. Still
            // attached to us is the pre-emptive case (the notification beat the unsubscribe, and the
            // subscription is now stale); attached nowhere is the stranded case. Both need acting on, and
            // distinguishing them from "already handled" is the whole job of this check.
            var subscription = pair.Value;
            var current = subscription.GetAnyCurrentServer();
            if (current is not null && !ReferenceEquals(current, this))
            {
                Multiplexer.Trace($"slot {slot} moved, and {channel} has already moved with it; leaving it alone", ToString());
                continue;
            }

            Multiplexer.Trace($"slot {slot} moved; resubscribing {channel}", ToString());
            Multiplexer.DefaultSubscriber.ResubscribeToServer(subscription, channel, this, cause: "smigrated");
        }
    }

    private static bool IsInMigratedRange(IReadOnlyList<ClusterSlotMigration> migrations, int slot)
    {
        foreach (var migration in migrations)
        {
            foreach (var range in migration.Slots)
            {
                if (slot >= range.From && slot <= range.To) return true;
            }
        }

        return false;
    }
}
