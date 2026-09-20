using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Buffers;
using RESPite.Messages;
using RESPite.Operations;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Accumulates commands and sends them inside <c>MULTI</c>/<c>EXEC</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Where a transaction stops looking like a batch.</b> A batch needed no return channel at all,
    /// because each operation's own reply <i>is</i> its result. Inside <c>MULTI</c> the server answers
    /// each command with <c>+QUEUED</c> and holds the real results until <c>EXEC</c>, which returns them
    /// as one array - so RESP itself multiplexes them, and something has to distribute them back.
    /// </para>
    /// <para>
    /// That distribution happens <b>once</b>, in the <c>EXEC</c> operation's parse, rather than as a
    /// completion source per element. The queued operations are ordinary operations that are simply not
    /// completed by their first reply; the connection's hand-off hook takes the receipt and leaves them
    /// pending.
    /// </para>
    /// <para>
    /// <b>And this is where the old core's strangest code lived.</b> <c>TransactionMessage.GetMessages</c>
    /// pauses for condition replies with <c>Monitor.Enter</c>/<c>Monitor.Exit</c> on result boxes,
    /// <i>inside the write lock</i>, because an enumerator that blocks for a reply cannot hold that lock
    /// and the reader must make progress. A core that can <c>await</c> does not need the handshake: a
    /// pause is simply the boundary between two contiguous runs. See design notes §3c, which asked for
    /// exactly this distinction, and §7f, where the handshake collapsed the same way.
    /// </para>
    /// </remarks>
    internal sealed class RespTransactionExecutor : RespExecutorBase
    {
        private readonly RespExecutorBase _inner;
        private readonly RespContext _context;
        private readonly object _sync = new();
        private List<RespPayloadOperation>? _queue;
        private List<Condition>? _conditions;
        private List<Action<bool>?>? _verdicts;
        private bool _sent;
        private bool _watchConflict;

        /// <summary>Create a transaction over an executor.</summary>
        /// <param name="inner">The executor the transaction ultimately sends through.</param>
        /// <param name="context">
        /// The context conditions render through. Only conditions need it - the queued commands arrive
        /// already rendered - so it defaults to a bare one, and a transaction with no conditions never
        /// touches it.
        /// </param>
        internal RespTransactionExecutor(RespExecutorBase inner, RespContext? context = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _context = context ?? new RespContext();
        }

        /// <inheritdoc/>
        public override int Database => _inner.Database;

        /// <summary>
        /// Whether the conditions all held and the transaction was <i>still</i> aborted, because a
        /// watched key changed between the check and the <c>EXEC</c>.
        /// </summary>
        /// <remarks>
        /// Distinct from "a condition did not hold", and the distinction is the whole reason callers
        /// retry: a failed condition means the state is not what you wanted, where a watch conflict means
        /// it was, and somebody moved first. Only the second is worth re-reading and trying again.
        /// </remarks>
        internal bool WasWatchConflict => _watchConflict;

        /// <summary>How many commands are waiting to be sent.</summary>
        internal int Count
        {
            get
            {
                lock (_sync) return _queue?.Count ?? 0;
            }
        }

        /// <inheritdoc/>
        internal override RespExecutorBase? ResolveFor(in RedisKey key, RedisCommand command, CommandFlags flags)
            => _inner.ResolveFor(in key, command, flags);

        /// <inheritdoc/>
        public override RespPayload Send(in RespRequest request)
            => throw new NotSupportedException("A queued command cannot be waited on before the transaction is executed.");

        /// <inheritdoc/>
        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            var operation = RespPayloadOperation.Rent();
            operation.Attach(request.Span, request.Flags, cancellationToken);
            operation.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
            operation.Slot = request.Slot;

            // its first reply will be the +QUEUED receipt; the real result arrives from EXEC
            operation.ExpectsQueuedReceipt = true;

            lock (_sync)
            {
                if (_sent) throw new InvalidOperationException("This transaction has already been executed.");
                (_queue ??= []).Add(operation);
            }

            return new ValueTask<RespPayload>(operation, operation.Token);
        }

        /// <summary>Add a precondition that must hold for the transaction to be sent.</summary>
        /// <param name="condition">The condition; its key is watched and its check is evaluated first.</param>
        /// <param name="onVerdict">
        /// Told this condition's own answer, whatever the transaction as a whole then does. The shipped
        /// surface hands the caller a <c>ConditionResult</c> when the condition is added and fills it in
        /// later, so "which one failed?" stays answerable; a single overall bool cannot say that.
        /// </param>
        internal void AddCondition(Condition condition, Action<bool>? onVerdict = null)
        {
            if (condition is null) throw new ArgumentNullException(nameof(condition));
            lock (_sync)
            {
                if (_sent) throw new InvalidOperationException("This transaction has already been executed.");
                (_conditions ??= []).Add(condition);
                (_verdicts ??= []).Add(onVerdict);
            }
        }

        /// <summary>Send <c>MULTI</c>, the queued commands, and <c>EXEC</c>, as one contiguous run.</summary>
        /// <returns>Whether the transaction was executed.</returns>
        /// <remarks>
        /// <para>
        /// One run, because <c>MULTI</c> is per-connection state: anything of anybody else's interleaved
        /// between the <c>MULTI</c> and the <c>EXEC</c> would join the transaction rather than run beside
        /// it.
        /// </para>
        /// <para>
        /// <b>With conditions it is two runs, and that is the whole of design notes §3c.</b> The watches
        /// and their checks go out together and are <i>awaited</i>; only then is it known whether to send
        /// the <c>MULTI</c> at all. A pause for a reply is a contiguity boundary, not a flush point - and
        /// a core that can await needs nothing more than the boundary, where the old core needed
        /// <c>Monitor</c> handshakes on result boxes inside the write lock to get the same effect.
        /// </para>
        /// </remarks>
        internal async Task<bool> ExecuteAsync()
        {
            List<RespPayloadOperation>? queue;
            List<Condition>? conditions;
            List<Action<bool>?>? verdicts;
            lock (_sync)
            {
                queue = _queue;
                conditions = _conditions;
                verdicts = _verdicts;
                _queue = null;
                _conditions = null;
                _verdicts = null;
                _sent = true;
            }

            var empty = queue is null || queue.Count == 0;
            if (empty && conditions is null) return true; // an empty transaction trivially succeeds

            var target = _inner.ResolveFor(default, RedisCommand.MULTI, CommandFlags.None);

            // The connection is NAMED, not merely used. WATCH is per-connection state, so the EXEC that
            // relies on it has to happen on the same one - and between the two runs a reconnect is
            // entirely possible. Sending MULTI/EXEC on a fresh connection would run the transaction with
            // its guard silently gone, which is the one outcome a conditional transaction must not have.
            var connection = target?.CurrentConnection;
            if (target is null || connection is null || connection.IsClosed)
            {
                Fail(queue, "This executor cannot run a transaction; it has no connection to write MULTI and EXEC to as one run.");
                return false;
            }

            if (conditions is not null && !await CheckAsync(connection, conditions, verdicts).ConfigureAwait(false))
            {
                // a condition did not hold: the transaction is NOT sent, and nothing it queued ran
                Discard(connection, queue);
                return false;
            }

            if (empty)
            {
                // conditions held but there is nothing to run; the watches must still be released
                Unwatch(connection);
                return true;
            }

            if (connection != target.CurrentConnection
                || !TrySendOver(connection, queue!, out var exec, conditions is null ? null : OnAborted))
            {
                Fail(queue, "The connection was lost before the transaction could be sent.");
                return false;
            }

            return await exec.ConfigureAwait(false);
        }

        private void OnAborted() => _watchConflict = true;

        /// <summary>Run the watches and their checks as one contiguous run, and await the verdicts.</summary>
        /// <remarks>
        /// <b>Watches and checks are interleaved, in that order, exactly as the old core emits them.</b>
        /// Each <c>WATCH</c> must precede its own check, or the check reads a value the watch is not yet
        /// guarding and the window it exists to close is open again.
        /// </remarks>
        private async Task<bool> CheckAsync(RespConnection connection, List<Condition> conditions, List<Action<bool>?>? verdicts)
        {
            var checks = new RespConditionOperation[conditions.Count];
            var run = new IRespMessage[conditions.Count * 2];
            for (var i = 0; i < conditions.Count; i++)
            {
                var watch = RespPayloadOperation.Rent();
                watch.Attach(RespConditionOperation.RenderWatch(_context, conditions[i]), CommandFlags.None, default);
                run[i * 2] = watch;

                var check = new RespConditionOperation();
                check.Attach(_context, conditions[i]);
                checks[i] = check;
                run[(i * 2) + 1] = check;
            }

            if (!connection.Send(run, run.Length))
            {
                for (var i = 0; i < run.Length; i += 2) RespPayloadOperation.DiscardReply((RespPayloadOperation)run[i]);
                foreach (var check in checks) check.TrySetCanceled(check.Token);
                return false;
            }

            for (var i = 0; i < run.Length; i += 2) RespPayloadOperation.DiscardReply((RespPayloadOperation)run[i]);

            // every check is awaited, not short-circuited on the first false: they are already in flight,
            // and abandoning one leaves an operation that never recycles. Each verdict is reported
            // individually as well as folded, because the caller is owed "which condition failed?"
            var held = true;
            for (var i = 0; i < checks.Length; i++)
            {
                bool answer;
                try
                {
                    answer = await new ValueTask<bool>(checks[i], checks[i].Token).ConfigureAwait(false);
                }
                catch
                {
                    answer = false;
                }

                verdicts?[i]?.Invoke(answer);
                held &= answer;
            }

            return held;
        }

        /// <summary>Release the watches after a condition failed, without running anything.</summary>
        private static void Discard(RespConnection connection, List<RespPayloadOperation>? queue)
        {
            Unwatch(connection);

            // NOT an exception: "a precondition did not hold" is a real outcome of a real transaction,
            // and the old surface reports it by returning false from Execute while every queued command's
            // task completes as cancelled rather than faulted
            if (queue is null) return;
            foreach (var operation in queue) operation.TrySetCanceled(operation.Token);
        }

        private static void Unwatch(RespConnection connection)
        {
            var unwatch = RespPayloadOperation.Rent();
            unwatch.Attach(UnwatchFrame, CommandFlags.None, default);
            if (!connection.Send(unwatch)) unwatch.EnsureFaulted(CommandFlags.None);
            RespPayloadOperation.DiscardReply(unwatch);
        }

        private static void Fail(List<RespPayloadOperation>? queue, string message)
        {
            if (queue is null) return;
            var fault = new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection,
                CommandFlags.CommandRetryNever,
                message,
                null,
                CommandStatus.WaitingInBacklog);
            foreach (var operation in queue) operation.TrySetException(operation.Token, fault, definite: false);
        }

        /// <summary>Write <c>MULTI</c>, the queued commands, and <c>EXEC</c> down one connection.</summary>
        /// <param name="connection">The connection to write to; may be null or closed, which declines.</param>
        /// <param name="operations">The queued commands, in order.</param>
        /// <param name="exec">Completes with whether the transaction executed.</param>
        /// <returns>Whether the run was written.</returns>
        /// <remarks>
        /// <para>
        /// <b>One write, with the receipts standing in the pending queue for <c>MULTI</c> and
        /// <c>EXEC</c>.</b> The queued commands are enqueued too - they have to be, to receive their
        /// <c>+QUEUED</c> - but the connection's hand-off hook takes that receipt and leaves them pending
        /// for <c>EXEC</c> to complete.
        /// </para>
        /// <para>
        /// <b>This has to be shared, and the bug that says so is worth keeping.</b> It began as a method
        /// on the endpoint executor, so a transaction over a plain <see cref="RespConnectionExecutor"/>
        /// hit the base <c>TrySendTransaction</c>, which declines - and a declined transaction reports
        /// "no endpoint is available", which is a lie when the connection is right there and working.
        /// Nothing about assembling this run is endpoint-specific: it needs a connection, and any
        /// executor that owns one can serve it.
        /// </para>
        /// </remarks>
        /// <param name="onAborted">
        /// Told when <c>EXEC</c> replies null - the transaction was discarded because a watched key
        /// changed. Distinct from every other way this can return false, and the only one a caller
        /// should respond to by re-reading and trying again.
        /// </param>
        internal static bool TrySendOver(RespConnection? connection, List<RespPayloadOperation> operations, out ValueTask<bool> exec, Action? onAborted = null)
        {
            exec = default;
            if (connection is null || connection.IsClosed) return false;

            var multi = RespPayloadOperation.Rent();
            multi.Attach(MultiFrame, CommandFlags.None, default);

            var execOperation = new RespExecOperation();
            execOperation.Attach(ExecFrame, operations, onAborted);

            var run = new IRespMessage[operations.Count + 2];
            run[0] = multi;
            for (var i = 0; i < operations.Count; i++) run[i + 1] = operations[i];
            run[operations.Count + 1] = execOperation;

            if (!connection.Send(run, run.Length))
            {
                RespPayloadOperation.DiscardReply(multi);
                return false;
            }

            RespPayloadOperation.DiscardReply(multi); // +OK, which nobody is waiting for
            exec = new ValueTask<bool>(execOperation, execOperation.Token);
            return true;
        }

        private static ReadOnlySpan<byte> MultiFrame => "*1\r\n$5\r\nMULTI\r\n"u8;

        private static ReadOnlySpan<byte> ExecFrame => "*1\r\n$4\r\nEXEC\r\n"u8;

        private static ReadOnlySpan<byte> UnwatchFrame => "*1\r\n$7\r\nUNWATCH\r\n"u8;

        /// <summary>Fail everything queued; for a transaction discarded rather than executed.</summary>
        internal void Abandon(Exception fault)
        {
            List<RespPayloadOperation>? queue;
            lock (_sync)
            {
                queue = _queue;
                _queue = null;
                _sent = true;
            }

            if (queue is null) return;
            foreach (var operation in queue) operation.TrySetException(operation.Token, fault, definite: false);
        }
    }

    /// <summary>
    /// The <c>EXEC</c> command, whose reply is everybody else's results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parses the array and hands element <i>i</i> to queued operation <i>i</i>. A <b>null</b> reply means
    /// the transaction was aborted - a watched key changed - and every queued command is completed as
    /// having not run, which is a distinct outcome from failing.
    /// </para>
    /// <para>
    /// <b>The elements are copied out.</b> They are slices of one frame, and that frame's buffer is
    /// reference-counted as a whole, so retaining N payloads against it would keep the entire reply alive
    /// for as long as the longest-lived element. Copying each is the cheaper end of that trade when the
    /// alternative is pinning a potentially large array.
    /// </para>
    /// </remarks>
    internal sealed class RespExecOperation : RespMessageBase<bool>
    {
        private List<RespPayloadOperation>? _queued;
        private Action? _onAborted;

        internal void Attach(ReadOnlySpan<byte> request, List<RespPayloadOperation> queued, Action? onAborted = null)
        {
            _queued = queued;
            _onAborted = onAborted;
            var pool = ArrayPool<byte>.Shared;
            var buffer = pool.Rent(request.Length);
            request.CopyTo(buffer);
            SetRequest(new ReadOnlyMemory<byte>(buffer, 0, request.Length), pool, default);
        }

        /// <inheritdoc/>
        protected override void OnReset()
        {
            _queued = null;
            _onAborted = null;
        }

        /// <inheritdoc/>
        protected override bool ParseFrame(scoped ReadOnlySpan<byte> frame, IPayloadReservationProvider? source)
        {
            var queued = _queued;
            if (queued is null) return false;

            var reader = new RespReader(frame);
            if (!reader.TryMoveNext(checkError: false)) return Abort(queued, "EXEC produced no reply.");

            if (reader.IsError)
            {
                var message = reader.ReadString() ?? "EXEC failed.";
                foreach (var operation in queued)
                {
                    operation.TrySetException(
                        operation.Token,
                        new RedisServerException(RedisErrorKindMetadata.Classify(reader), CommandFlags.None, message),
                        definite: true);
                }

                return false;
            }

            // a null reply is an ABORT, not a failure: a watched key changed, so nothing ran
            if (reader.IsNull)
            {
                _onAborted?.Invoke();
                return Abort(queued, null);
            }

            // Walk the elements by SCANNING each one's extent, exactly as the connection does for
            // top-level frames. Slicing by hand would work for the scalars a transaction usually returns
            // and quietly break on a queued LRANGE, whose element is an aggregate of its own.
            var count = reader.AggregateLength();
            var body = frame.Slice(HeaderLength(frame));

            for (var i = 0; i < queued.Count; i++)
            {
                var operation = queued[i];
                var scan = default(RespScanState);
                if (i >= count || body.IsEmpty || !scan.TryRead(body, out var length))
                {
                    operation.TrySetException(
                        operation.Token,
                        new RedisServerException(RedisErrorKind.ConnectionFault, CommandFlags.None, "EXEC returned fewer results than commands queued."),
                        definite: true);
                    continue;
                }

                // each element is a complete frame in its own right, so the operation parses it exactly
                // as it would have parsed a reply of its own. No source: these bytes belong to the EXEC
                // reply, and retaining N slices of one frame would pin the whole array for as long as its
                // longest-lived element.
                operation.TrySetResult(operation.Token, body.Slice(0, length));
                body = body.Slice(length);
            }

            return true;
        }

        /// <summary>How long the aggregate's own header is, so the elements can be walked.</summary>
        private static int HeaderLength(scoped ReadOnlySpan<byte> frame)
        {
            var terminator = frame.IndexOf((byte)'\n');
            return terminator < 0 ? frame.Length : terminator + 1;
        }

        private static bool Abort(List<RespPayloadOperation> queued, string? fault)
        {
            foreach (var operation in queued)
            {
                if (fault is null)
                {
                    // aborted: the command did not run, so it is NOT applied - which the retry layer reads
                    operation.TrySetException(
                        operation.Token,
                        new RedisServerException(RedisErrorKind.None, CommandFlags.None, "The transaction was aborted; a watched key changed."),
                        definite: true);
                }
                else
                {
                    operation.TrySetException(
                        operation.Token,
                        new RedisServerException(RedisErrorKind.ConnectionFault, CommandFlags.None, fault),
                        definite: true);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// One precondition's check: the command a <see cref="Condition"/> names, parsed into its verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The conditions themselves are reused whole.</b> <see cref="Condition.TryValidate"/> already
    /// takes a <c>ref RespReader</c> and answers a bool - it was written for
    /// <c>Condition.ConditionProcessor</c>, and it happens to be exactly the shape this core's
    /// <c>ParseFrame</c> wants. Seven condition types carried over without a line changed, which is
    /// the same story as <c>Message.GetPrimaryReplicaFlags</c> and <c>ServerSelectionStrategy</c>:
    /// the parts of the old core that were about <i>Redis</i> rather than about <c>Message</c> survive
    /// the replacement.
    /// </para>
    /// <para>
    /// Rendering goes through <see cref="Condition.CreateMessages"/> for the same reason - it is the
    /// one place that knows a condition's command and arguments, and a second renderer here would be a
    /// second thing to keep in step across seven types.
    /// </para>
    /// </remarks>
    internal sealed class RespConditionOperation : RespMessageBase<bool>
    {
        private Condition? _condition;

        internal void Attach(RespContext context, Condition condition)
        {
            _condition = condition;
            using var frame = condition.RenderCheck(context);
            Copy(frame.Span);
        }

        /// <summary>Render the <c>WATCH</c> that must precede a condition's check.</summary>
        internal static byte[] RenderWatch(RespContext context, Condition condition)
        {
            using var frame = context.Render($"{RedisCommand.WATCH}{condition.WatchKey}");
            return frame.Span.ToArray();
        }

        private void Copy(scoped ReadOnlySpan<byte> frame)
        {
            var pool = ArrayPool<byte>.Shared;
            var buffer = pool.Rent(frame.Length);
            frame.CopyTo(buffer);
            SetRequest(new ReadOnlyMemory<byte>(buffer, 0, frame.Length), pool, default);
        }

        /// <inheritdoc/>
        protected override void OnReset() => _condition = null;

        /// <inheritdoc/>
        /// <remarks>
        /// A condition whose command <i>errors</i> - <c>WRONGTYPE</c>, say - has not held, and lets the
        /// error stand rather than swallowing it: the caller gets the server's own words, and the
        /// transaction does not run.
        /// </remarks>
        protected override bool ParseFrame(scoped ReadOnlySpan<byte> frame, IPayloadReservationProvider? source)
        {
            var condition = _condition ?? throw new InvalidOperationException("No condition is attached.");
            var reader = new RespReader(frame);
            if (!reader.TryMoveNext(checkError: true)) return false;
            return condition.TryValidate(ref reader, out var held) && held;
        }
    }
}
