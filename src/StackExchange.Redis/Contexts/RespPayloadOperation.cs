using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Buffers;
using RESPite.Messages;
using RESPite.Operations;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>An operation whose result is the reply frame itself.</summary>
    /// <remarks>
    /// <para>
    /// Overrides <c>ParseFrame</c> rather than <c>Parse</c>, because the response <i>is</i> the frame:
    /// the context surface's handlers read it, and the client-side cache stores it. Reading it into a
    /// value here and re-rendering it would be exactly the copy this design removes.
    /// </para>
    /// <para>
    /// <b>One copy remains, and it is the transport's fault rather than ours.</b> The frame handed to
    /// <c>ParseFrame</c> is owned by the receive buffer and valid only for the call, so the payload
    /// has to be taken out of it before the buffer is compacted. Handing out a reference-counted slice
    /// of the receive buffer instead is a real win and a separate piece of work.
    /// </para>
    /// </remarks>
        internal sealed class RespPayloadOperation : RespMessageBase<RespPayload>, IFaultSubject
    {
        /// <summary>
        /// Recycled operations, so a steady-state send allocates no operation at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A fixed array with <see cref="Interlocked.Exchange{T}(ref T, T)"/> on each slot, rather than a
        /// queue: a queue allocates a node per operation, which is the thing being removed. Missing the
        /// pool is not a failure - it allocates, exactly as before - so the pool can be small and lossy.
        /// </para>
        /// <para>
        /// Only operations that reached a <b>definite</b> outcome come back here, because that is the only
        /// case where the pipeline provably has no further use for the instance; see the pooling policy in
        /// the design notes. A timeout or a connection fault leaves the instance alone for the GC.
        /// </para>
        /// <para>
        /// <b>Shared rather than per-executor.</b> An endpoint that reconnects builds a new executor over
        /// the new connection, and a pool that died with the old one would allocate afresh for every
        /// operation after every reconnect - exactly when the system is already under stress.
        /// </para>
        /// </remarks>
        private static readonly RespPayloadOperation?[] Pool = new RespPayloadOperation[PoolSize];

        private const int PoolSize = 128;

        internal static RespPayloadOperation Rent()
        {
            for (var i = 0; i < Pool.Length; i++)
            {
                if (Interlocked.Exchange(ref Pool[i], null) is { } reused) return reused;
            }

            return new RespPayloadOperation();
        }

        private static void Return(RespPayloadOperation operation)
        {
            for (var i = 0; i < Pool.Length; i++)
            {
                if (Interlocked.CompareExchange(ref Pool[i], operation, null) is null) return;
            }

            // pool full; drop it, which is why this is lossy by design rather than by accident
        }

        private CommandFlags _flags;

        /// <summary>Whether this command has already been re-issued after a redirect.</summary>
        /// <remarks>
        /// <b>Once is enough.</b> A second redirect for the same command is pathological rather than
        /// routine - two nodes that disagree, or a topology changing faster than commands complete - so
        /// short-circuiting it is warranted. The command then fails with the server's own error, which
        /// says more about what is wrong than a redirect loop or a hang would. This mirrors the shipped
        /// core, which sets <see cref="CommandFlags.NoRedirect"/> on the message when it re-issues.
        /// </remarks>
        internal bool HasFollowedRedirect { get; set; }

        /// <summary>
        /// Whether this command's next reply is a <c>+QUEUED</c> receipt rather than its result.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Inside <c>MULTI</c>, the server answers each command with <c>+QUEUED</c> and holds the real
        /// results until <c>EXEC</c>, which returns them as one array. So a queued command receives
        /// <b>two</b> replies, and the first one is a receipt: completing the operation with it would hand
        /// the caller "QUEUED" where its value should be.
        /// </para>
        /// <para>
        /// This is where batch and transaction genuinely differ. In a batch there is no return channel to
        /// design, because each operation's own reply <i>is</i> its result. In a transaction RESP itself
        /// multiplexes the results into a single array, so a distribution step is unavoidable - but it
        /// happens once, in the <c>EXEC</c> operation's parse, rather than as a completion source per
        /// element.
        /// </para>
        /// </remarks>
        internal bool ExpectsQueuedReceipt { get; set; }

        /// <summary>The cluster slot this command's keys resolved to, for batch grouping.</summary>
        /// <remarks>
        /// Carried on the operation because the request it was rendered from is gone by the time a batch
        /// is dispatched - the bytes were copied and the caller's frame released.
        /// </remarks>
        internal int Slot { get; set; } = ServerSelectionStrategy.NoSlot;

        /// <summary>Which database this command belongs to, or <c>-1</c> when it belongs to none.</summary>
        /// <remarks>
        /// <b>On the operation for the same reason <see cref="Slot"/> is</b>: by the time it is written the
        /// request it came from may be long gone. It matters most on the backlog - commands for several
        /// databases can be queued together while a connection comes up, and each has to be preceded by its
        /// OWN <c>SELECT</c> as it drains. Reading the executor's database there would select once and run
        /// everything behind it, which is silent and wrong.
        /// </remarks>
        internal int Database { get; set; } = -1;

        /// <summary>The flags the request carried, which the retry and redirect layers read.</summary>
        internal CommandFlags Flags => _flags;

        /// <summary>How this command should be named in a diagnostic: the command and its first key.</summary>
        /// <remarks>
        /// <b>Read off the rendered frame rather than carried alongside it.</b> The bytes are already here
        /// - they have to be, to be written - so the key costs nothing until something actually goes wrong
        /// and needs to name it. Paid on the caller's own unwind, where this operation is not going to be
        /// sent, rather than on every command against the chance that one fails.
        /// <para>
        /// The FIRST argument, which is the key for the overwhelming majority of commands and is what the
        /// shipped <c>Message.CommandAndKey</c> reports. It is an approximation for the few where the first
        /// argument is something else - a subcommand, a script body - and a slightly wrong label on an
        /// error is a far better trade than carrying a key on every operation ever issued.
        /// </para>
        /// </remarks>
        internal string CommandAndKey
        {
            get
            {
                var command = Command == RedisCommand.NONE ? string.Empty : Command.ToString();
                var frame = RequestForDiagnostics;
                if (frame.IsEmpty) return command;

                try
                {
                    var reader = new RespReader(frame.Span);
                    if (!reader.TryMoveNext() || !reader.IsAggregate) return command;
                    if (reader.AggregateLength() < 2) return command;
                    if (!reader.TryMoveNext()) return command; // the command itself
                    if (!reader.TryMoveNext() || !reader.IsScalar) return command;

                    var key = reader.ReadString();
                    return string.IsNullOrEmpty(key) ? command : $"{command} {key}";
                }
                catch
                {
                    // a diagnostic that throws is worse than a diagnostic that is vague
                    return command;
                }
            }
        }

        /// <summary>Which command this is, for diagnostics after the request itself is gone.</summary>
        /// <remarks>
        /// <b>Kept for the same reason <see cref="Slot"/> and <see cref="Database"/> are</b>: the rendered
        /// request is bytes and the caller's frame is released, so by the time this command fails there is
        /// nothing left to say what it was. It matters most on the backlog, which is exactly where a
        /// failure is reported without the command ever having been written - and "no connection was
        /// available to service this operation" is a good deal less useful when it cannot name the
        /// operation.
        /// </remarks>
        internal RedisCommand Command { get; set; }

        /// <summary>
        /// Set when the server redirected somewhere that cannot be dialled; the error this reply becomes
        /// says so instead of restating the raw redirect.
        /// </summary>
        /// <remarks>
        /// <b>A redirect nobody can follow is not a redirect, and reporting it as one is misleading in a
        /// specific way</b>: the caller is told the slot moved to a place, when in fact the server said it
        /// does not know where the slot went. The distinction has a kind of its own
        /// (<see cref="RedisErrorKind.UnknownRedirectTarget"/>) because retry policy reads it - the
        /// command provably never ran, so it is safe to retry once the topology is refreshed, which is not
        /// true of an arbitrary server error.
        /// <para>
        /// Carried on the operation rather than decided here because only the connection has the parsed
        /// redirect, and only <c>ParseFrame</c> turns a frame into an exception. This is the handoff
        /// between them.
        /// </para>
        /// </remarks>
        internal string? UnroutableRedirectMessage { get; set; }

        /// <summary>The profiling record for this command, when anyone is profiling.</summary>
        /// <remarks>
        /// Lives in <c>Diagnostics.HostState</c>, which design notes section 4 reserved for exactly this:
        /// the operation carries an opaque slot for the host's per-command object, so RESPite never has
        /// to know what a <see cref="Profiling.ProfiledCommand"/> is.
        /// </remarks>
        internal Profiling.ProfiledCommand? Profile
        {
            get => Diagnostics.HostState as Profiling.ProfiledCommand;
            set => Diagnostics.HostState = value;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>The library's own timeout type, which is what callers catch.</b> A bare
        /// <see cref="TimeoutException"/> is accurate and nobody handles it: every caller that has ever
        /// dealt with a timeout from this library catches <see cref="RedisTimeoutException"/>, and the
        /// shipped core has always thrown one. It carries the flags and how far the command got, both of
        /// which the retry layer reads to decide whether re-sending is safe.
        /// </remarks>
        protected override Exception CreateTimeoutException()
        {
            var elapsed = (int)Diagnostics.Age.TotalMilliseconds;
            if (Server is not { Multiplexer: { } multiplexer } server)
            {
                // nobody models this endpoint - an executor built directly over a transport, which the tests
                // do and nothing else does. Say the true thing that can be said without a client.
                return new RedisTimeoutException(
                    _flags,
                    $"Timeout awaiting response ({elapsed}ms elapsed), command={CommandAndKey}",
                    (CommandStatus)Diagnostics.Status)
                {
                    MaintenanceType = MaintenanceTypeForFault,
                };
            }

            // the timeout that APPLIED, which is what the reader needs in order to judge the elapsed number
            // next to it - the configured one raised by any maintenance window, exactly as the sweep that
            // raised this decided it
            var configured = IsAwaited ? multiplexer.AsyncTimeoutMilliseconds : multiplexer.TimeoutMilliseconds;
            var timeout = server.GetEffectiveTimeoutMilliseconds(configured);

            return ExceptionFactory.Timeout(
                multiplexer,
                $"Timeout awaiting response ({elapsed}ms elapsed, timeout is {timeout}ms)",
                this,
                server);
        }

        /// <summary>Which announced disruption, if any, a fault on this command should be blamed on.</summary>
        /// <remarks>
        /// <b>Bounded by this command's own age rather than the configured timeout</b>, which is the same
        /// question <see cref="ExceptionFactory"/> asks with less to go on. A window that closed while this
        /// command was outstanding still counts - a command that timed out was waiting for its whole
        /// timeout before anybody looked, so reading the <i>active</i> type would report
        /// <see cref="Maintenance.MaintenanceNotificationType.None"/> for a timeout maintenance plainly
        /// caused. The shipped core has to approximate the age from configuration; here the operation
        /// knows exactly how long it waited.
        /// </remarks>
        internal Maintenance.MaintenanceNotificationType MaintenanceTypeForFault
            => Server?.GetMaintenanceTypeForFault((int)Diagnostics.Age.TotalMilliseconds)
                ?? Maintenance.MaintenanceNotificationType.None;

        /// <summary>The server this command was sent to, when it is one this client models.</summary>
        /// <remarks>
        /// Carried on the operation for the same reason <see cref="Observer"/> is: by the time a fault is
        /// built the routing decision is long gone, and the answer has to be the endpoint this command
        /// actually went to.
        /// </remarks>
        internal ServerEndPoint? Server { get; set; }

        /// <inheritdoc/>
        string IFaultSubject.CommandAndKey => CommandAndKey;

        /// <inheritdoc/>
        string IFaultSubject.CommandString => Command.ToString();

        /// <inheritdoc/>
        CommandFlags IFaultSubject.Flags => _flags;

        /// <inheritdoc/>
        CommandStatus IFaultSubject.Status => (CommandStatus)Diagnostics.Status;

        /// <inheritdoc/>
        bool IFaultSubject.IsBacklogged => (CommandStatus)Diagnostics.Status == CommandStatus.WaitingInBacklog;

        /// <inheritdoc/>
        bool IFaultSubject.IsAsync => IsAwaited;

        /// <inheritdoc/>
        /// <remarks>
        /// This core gives the subscription connection its own executor rather than a flag on the command,
        /// so the question is answered by whoever dispatched it; see <see cref="IsSubscription"/>.
        /// </remarks>
        bool IFaultSubject.IsForSubscriptionBridge => IsSubscription;

        /// <inheritdoc/>
        /// <remarks>Already computed by routing, which is the only thing that needed it.</remarks>
        int IFaultSubject.GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => Slot;

        /// <summary>Whether this command was sent on the subscription connection.</summary>
        internal bool IsSubscription { get; set; }

        /// <inheritdoc/>
        protected override void OnSent() => Profile?.SetRequestSent();

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Finished, not consumed.</b> The record is pushed to its session here rather than when the
        /// caller reads the result, because a fire-and-forget command has no reader and would otherwise
        /// never appear in a profile at all.
        /// </remarks>
        protected override void OnFinished(Exception? fault)
        {
            Profile?.SetCompleted();

            // the client's own timeout tally, which GetStatus and GetCounters report. The shipped core
            // counts in two places because it raises the two kinds in two places - async on the bridge
            // heartbeat, sync in the caller's own wait. This core raises both from one sweep, so the
            // question "was anybody awaiting this" is asked here instead; see RespMessageBase.IsAwaited
            // for why a false answer means only "not as far as we know".
            if (fault is RedisTimeoutException && Server is { Multiplexer: { } multiplexer })
            {
                if (IsAwaited) multiplexer.OnAsyncTimeout();
                else multiplexer.OnSyncTimeout();
            }

            // and the circuit breaker, from the same hook and for the same reason: this is the one point
            // that sees EVERY ending - a reply, a server error, a cancellation, a timeout, a connection
            // fault - which is exactly the set an availability breaker is counting. The shipped core
            // observes at the equivalent point, in Message.Complete.
            Observer?.ObserveOutcome(fault);
        }

        /// <summary>Told how this command ended, for availability accounting; null when nobody is counting.</summary>
        /// <remarks>
        /// On the operation rather than looked up at completion time because by then the routing decision
        /// is long gone - and the answer has to be the endpoint this command actually went to, not whichever
        /// one it would be routed to now.
        /// </remarks>
        internal IRespOutcomeObserver? Observer { get; set; }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>This instance is pooled</b>, so a life's state has to be cleared or it becomes the next
        /// life's starting position - here, a command that had followed a redirect would hand a
        /// "already redirected once" to whatever command reused the instance, silently refusing to follow
        /// a legitimate redirect for it. Exactly the hazard design notes section 4 describes, and the
        /// reason <c>Reset</c> is exhaustive by construction rather than by inspection.
        /// </remarks>
        protected override void OnReset()
        {
            HasFollowedRedirect = false;
            Command = RedisCommand.NONE;
            Observer = null;
            Server = null;
            IsSubscription = false;
            UnroutableRedirectMessage = null;
            ExpectsQueuedReceipt = false;
            Slot = ServerSelectionStrategy.NoSlot;
            Profile = null; // the next life gets its own record, or none
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Called only after a definite outcome has been consumed, with the instance already reset and
        /// its version moved on - so anything still holding the old token is locked out before this
        /// hands the instance to the next caller.
        /// </remarks>
        protected override void OnRecyclable() => Return(this);

        internal void Attach(ReadOnlySpan<byte> request, CommandFlags flags, CancellationToken cancellationToken)
        {
            _flags = flags;
            var pool = ArrayPool<byte>.Shared;
            var buffer = pool.Rent(request.Length);
            request.CopyTo(buffer);
            SetRequest(new ReadOnlyMemory<byte>(buffer, 0, request.Length), pool, cancellationToken);
        }

        /// <summary>Fault an operation the connection refused, naming how far it actually got.</summary>
        /// <param name="flags">The request's flags, which the retry policy reads.</param>
        /// <remarks>
        /// The status cast is an identity: <c>RespCommandStatus</c> mirrors the shipped
        /// <see cref="CommandStatus"/> deliberately, values included, and a test pins it. This is what
        /// that decision was for - the exception can say "still in the backlog" rather than "unknown",
        /// and <c>FaultContext.NotApplied</c> can then tell retry the server never saw it.
        /// </remarks>
        internal void EnsureFaulted(CommandFlags flags) => EnsureFaulted(flags, null);

        /// <summary>Fail this command because it could not be sent.</summary>
        /// <param name="flags">The command's flags, which the exception carries.</param>
        /// <param name="fault">
        /// The failure to report, when the caller can describe it better than this can. The fallback says
        /// only that a connection was not available, which is true and nearly useless: the shipped core
        /// answers the same situation with which endpoints were tried, what each of them last failed with,
        /// and how far connecting had got - and callers have been reading that for years.
        /// </param>
        internal void EnsureFaulted(CommandFlags flags, Exception? fault)
        {
            fault ??= new RedisConnectionException(
                ConnectionFailureType.SocketClosed,
                flags,
                "The connection is not available.",
                null,
                (CommandStatus)Diagnostics.Status);

            TrySetException(Token, fault, definite: false);
        }

        /// <summary>Turn a reply frame into a payload, or an error reply into an exception.</summary>
        /// <param name="frame">The complete reply frame.</param>
        /// <param name="source">Who owns the frame, when it can be retained rather than copied.</param>
        /// <remarks>
        /// <para>
        /// <b>An error reply becomes an exception here</b>, which is parity rather than a choice: the
        /// pipeline this executor replaces converts in <c>ResultProcessor</c> before a payload is ever
        /// produced, so a caller who catches <see cref="RedisServerException"/> today keeps catching
        /// it. Left raw, the reply would surface as RESPite's <c>RespException</c> from wherever the
        /// handler first read it.
        /// </para>
        /// <para>
        /// Worth noting that this makes the two payload producers agree, which they currently do not:
        /// a context over a raw executor throws <c>RespException</c> for the same reply. See the
        /// design notes - where error classification belongs is a real question, and the answer is
        /// "whoever turns a frame into a result", which is here.
        /// </para>
        /// </remarks>
        protected override RespPayload ParseFrame(scoped ReadOnlySpan<byte> frame, IPayloadReservationProvider? source)
        {
            var reader = new RespReader(frame);
            if (reader.TryMoveNext(checkError: false) && reader.IsError)
            {
                if (UnroutableRedirectMessage is { } unroutable)
                {
                    throw new RedisServerException(RedisErrorKind.UnknownRedirectTarget, _flags, unroutable);
                }

                throw new RedisServerException(
                    RedisErrorKindMetadata.Classify(reader),
                    _flags,
                    reader.ReadString() ?? "Unknown server error.");
            }

            // RETAIN rather than copy, when the sender owns the bytes and says so. This is the payload's
            // whole reason for existing - it is handed on, read by a handler, and sometimes cached - so
            // copying it out of the receive buffer was the one remaining copy on the read path, measured
            // at ~72 bytes per operation. The reservation counts against the connection's buffer, which
            // then cannot move or be reused until this payload is released.
            if (source is not null && source.TryReserve(frame, out var reservation)
                && reservation.Owner is RefCountedBuffer buffer)
            {
                return new RespPayload(buffer, reservation.Offset, reservation.Length);
            }

            // no owner, or the span is not a window onto it: the bytes are valid only for this call
            return RespPayload.Create(frame);
        }

        protected override RespPayload ParseFrame(in ReadOnlySequence<byte> frame)
        {
            var length = checked((int)frame.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                frame.CopyTo(buffer);
                return RespPayload.Create(new ReadOnlySpan<byte>(buffer, 0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>Await and throw away an operation's reply, so it recycles.</summary>
        /// <param name="operation">The operation to drain.</param>
        /// <remarks>
        /// Some commands have an answer that nobody wants: <c>ASKING</c>'s <c>+OK</c>, <c>MULTI</c>'s.
        /// Somebody still has to take it. Left unconsumed the operation is never reset, so it never
        /// returns to the pool and never releases the pooled buffer holding its request: a slow leak of
        /// exactly the kind pooling was added to avoid.
        /// </remarks>
        internal static void DiscardReply(RespPayloadOperation operation)
        {
            _ = DrainAsync(operation);

            static async Task DrainAsync(RespPayloadOperation operation)
            {
                try
                {
                    using var payload = await new ValueTask<RespPayload>(operation, operation.Token).ConfigureAwait(false);
                }
                catch
                {
                    // the command it was paired with will report any failure better than this can
                }
            }
        }
    }
}
