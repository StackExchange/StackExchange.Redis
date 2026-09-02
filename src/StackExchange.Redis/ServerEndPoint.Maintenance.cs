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
    private int _relaxedDeadlineTicks;

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

    private volatile EndPoint? _handoffTarget;
    private int _handoffTargetExpiryTicks;
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
    /// is what we would have done anyway.
    /// </para>
    /// </remarks>
    internal EndPoint? HandoffTarget
    {
        get
        {
            var target = _handoffTarget;
            if (target is null) return null;

            if (unchecked(Environment.TickCount - Volatile.Read(ref _handoffTargetExpiryTicks)) >= 0)
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
        Volatile.Write(ref _handoffTargetExpiryTicks, unchecked(Environment.TickCount + (int)Math.Max(window.TotalMilliseconds, 1000)));
        _handoffTarget = target;
    }

    /// <summary>
    /// Forgets any handoff target, once a connection has been established.
    /// </summary>
    /// <remarks>
    /// Called on full establishment rather than on the connect attempt: if the attempt fails we want the next
    /// one to try the target again, within its window. Once a connection is up, normal resolution resumes -
    /// by then DNS has usually caught up anyway.
    /// </remarks>
    internal void ClearHandoffTarget() => _handoffTarget = null;
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
        var current = (connection.VolatileSocket?.RemoteEndPoint as IPEndPoint)?.Address;

        _ = HandoffAsync(budget, successor, current);
    }

    private async Task HandoffAsync(TimeSpan window, EndPoint? successor, IPAddress? currentAddress)
    {
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
                currentAddress,
                remaining,
                pollInterval: TimeSpan.FromSeconds(1), // records carry a 5s TTL, so this is several looks per record
                resolve: Multiplexer.AddressResolver,
                log: message => Multiplexer.Trace(message, ToString())).ForAwait();

            Multiplexer.Trace($"MOVING: {decision}", ToString());
            _lastHandoffOutcome = decision.ToString();

            // Trace is [Conditional("VERBOSE")], so without this a handoff leaves no record in a normal build -
            // and a handoff replaces connections, which is exactly the kind of thing somebody needs to be able
            // to find afterwards.
            Multiplexer.Logger?.LogInformationMaintenanceHandoff(new(this), _lastHandoffOutcome);
            switch (decision.Action)
            {
                case HandoffAction.Recycle:
                    await DrainThenRecycleAsync(remaining, decision.Reason).ForAwait();
                    break;
                case HandoffAction.RecycleAtHalfWindow:
                    // The contract's rule for "no replacement named", applied where there is nothing better to
                    // go on. Half of the *announced* window, less whatever the jitter already spent.
                    var half = TimeSpan.FromTicks(window.Ticks / 2) - jitter;
                    if (half > TimeSpan.Zero) await Task.Delay(half).ForAwait();
                    await DrainThenRecycleAsync(window - jitter - (half > TimeSpan.Zero ? half : TimeSpan.Zero), decision.Reason).ForAwait();
                    break;
                case HandoffAction.MoveTo when decision.Target is { } target:
                    // Point the next connection at the named address and replace the connections. Previously
                    // this only re-read the topology and recycled, which measurably did not work: we recycled
                    // at +6.2s, landed back on the node being retired because DNS had not moved yet, and were
                    // closed at +21.6s anyway - exactly the outcome the handoff exists to avoid.
                    SetHandoffTarget(target, remaining);
                    await DrainThenRecycleAsync(remaining, decision.Reason).ForAwait();
                    break;
                default:
                    // Nothing to do is a legitimate outcome, not a failure: the server closes the socket, the
                    // reconnect re-resolves, and the relaxed window covers the gap.
                    break;
            }
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

    /// <summary>
    /// Lets in-flight caller work finish, then replaces the connections.
    /// </summary>
    /// <remarks>
    /// Draining first because the socket is still working: anything already written may still be answered, and
    /// dropping it would fail commands that were about to succeed. Bounded by what is left of the window,
    /// because the server closes the socket at the end of it regardless - so anything not drained by then was
    /// going to fail either way, and draining strictly dominates.
    /// <para>
    /// Both bridges, not just the one that was told. The measured blast radius is the *node*: four connections
    /// to one proxy, differing only in handshake, all closed simultaneously, and only the ones that had opted in
    /// were warned.
    /// </para>
    /// </remarks>
    private async Task DrainThenRecycleAsync(TimeSpan budget, string reason)
    {
        var watch = ValueStopwatch.StartNew();
        while (HasCallerWork() && watch.ElapsedMilliseconds < budget.TotalMilliseconds)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20)).ForAwait();
        }

        var drained = !HasCallerWork();
        var recycled = (interactive?.RecycleConnection(reason) == true) | (subscription?.RecycleConnection(reason) == true);
        if (recycled) Interlocked.Increment(ref _handoffRecycles);
        Multiplexer.Trace(
            $"MOVING: {(recycled ? "recycled" : "nothing to recycle")} after {watch.ElapsedMilliseconds}ms"
            + (drained ? " (drained)" : " (still busy; the window ran out)"),
            ToString());
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
