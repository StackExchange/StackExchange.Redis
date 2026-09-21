using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. One endpoint: owns its connection's whole life, not just its use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RespConnectionExecutor"/> sends over a connection somebody else established and does
    /// not care what happens to it. This one <b>owns</b> the connection: it connects, hands the new
    /// connection to the handshake, notices when it dies, reconnects, and decides what happens to
    /// commands that arrive while there is nothing to send them on.
    /// </para>
    /// <para>
    /// This is the <b>server-endpoint</b> executor of the three in design notes section 3b. It makes no
    /// routing decision - there is one endpoint and that is where everything goes. The multiplexer and
    /// group executors resolve a key to one of these; they do not re-implement what it does.
    /// </para>
    /// <para>
    /// <b>Reconnection is lazy, and deliberately so.</b> There is no background loop trying to reconnect
    /// to an endpoint nobody is using; a send finds no connection and starts one, and everybody who
    /// arrives during that attempt waits on the same attempt rather than starting their own. An idle
    /// application that has lost a server discovers it when it next needs it, which is also when it can
    /// do anything about it.
    /// </para>
    /// </remarks>
    internal sealed class RespEndpointExecutor : RespExecutorBase, IAsyncDisposable
    {
        private readonly Func<CancellationToken, Task<RespConnection>> _connect;
        private readonly Func<RedisFeatures?>? _features;
        private readonly Func<RespPayloadOperation, RedisCommand, CommandFlags, int, EndPoint?, object?>? _startProfile;
        private readonly EndPoint? _endpoint;
        private readonly bool _queueWhileDisconnected;
        private readonly object _sync = new();

        private RespConnection? _connection;
        private Task<RespConnection>? _connecting;
        private Queue<RespPayloadOperation>? _backlog;
        private bool _disposed;

        /// <summary>Whether somebody owns the write side of this connection right now.</summary>
        /// <remarks>
        /// <para>
        /// <b>An ordered write slot, and every part of that matters.</b> Two callers need the write side
        /// to themselves for longer than a single <c>Send</c>: a contiguous run, which must not be
        /// interleaved, and a conditional transaction, which has a real pause between its watches and its
        /// <c>MULTI</c> and must not let anything be written into it. The shipped surface gets the second
        /// by holding the connection's write lock across the pause, which is why
        /// <c>TransactionMessage</c> pauses an enumerator with <c>Monitor</c> handshakes on result boxes:
        /// the reader must still make progress to deliver the replies being waited for.
        /// </para>
        /// <para>
        /// Here the slot blocks <b>writers</b> and nothing else. Arrivals go to the backlog - the same
        /// queue a disconnected endpoint uses, drained in arrival order - so per-connection FIFO comes
        /// from the mechanism that already guarantees it rather than a second one. No thread blocks, the
        /// reader is never impeded, and the replies that release the slot cannot be starved by it.
        /// </para>
        /// <para>
        /// <b>Acquired rather than merely waited on</b>, which is the correction that made it work. A
        /// version that only waited left a window: a run that had to wait for the first connect released
        /// its wait, and commands issued in the meantime were already in the backlog and drained ahead of
        /// it. Whoever is going to write a run has to own the slot from before the wait, so that anything
        /// arriving during the wait queues behind it. The existing BatchTests found this as a batch
        /// overtaking the StringSet issued before it, and then failing WRONGTYPE.
        /// </para>
        /// </remarks>
        private bool _writeSlotHeld;

        /// <summary>Whoever is waiting for the write slot, in the order they asked.</summary>
        private Queue<TaskCompletionSource<bool>>? _writeWaiters;

        /// <summary>Create an executor that owns a connection to one endpoint.</summary>
        /// <param name="connect">Establishes and hands back a ready-to-use connection.</param>
        /// <param name="database">The database this executor sends to.</param>
        /// <param name="endpoint">The endpoint, for identity; may be null when unknown.</param>
        /// <param name="queueWhileDisconnected">
        /// Whether a command arriving with no connection waits for one, or fails immediately.
        /// </param>
        /// <param name="features">
        /// What this endpoint's own handshake observed, if anything. Supplied rather than discovered
        /// because the connect delegate owns the handshake - this only has to be told the answer.
        /// </param>
        /// <param name="startProfile">Begins a profiling record for a command, when anyone is profiling.</param>
        /// <param name="select">Supplies <c>SELECT</c> frames when this connection serves several databases.</param>
        internal RespEndpointExecutor(
            Func<CancellationToken, Task<RespConnection>> connect,
            int database = 0,
            EndPoint? endpoint = null,
            bool queueWhileDisconnected = true,
            Func<RedisFeatures?>? features = null,
            Func<RespPayloadOperation, RedisCommand, CommandFlags, int, EndPoint?, object?>? startProfile = null,
            SelectPreamble? select = null)
        {
            _features = features;
            _startProfile = startProfile;
            _connect = connect ?? throw new ArgumentNullException(nameof(connect));
            Database = database;
            _endpoint = endpoint;
            _queueWhileDisconnected = queueWhileDisconnected;
            _select = select;
        }

        /// <summary>Where <c>SELECT</c> frames come from, or null if this connection serves one database.</summary>
        /// <remarks>
        /// <b>Null is the old shape and still the default</b>, so an executor that was never told about
        /// several databases behaves exactly as before - no injection, no per-command question. It is
        /// supplied only by whoever decided to share one connection across databases, which is the same
        /// decision that makes the injection necessary.
        /// </remarks>
        private readonly SelectPreamble? _select;

        /// <inheritdoc/>
        public override int Database { get; }

        /// <inheritdoc/>
        /// <remarks>Yes; see <see cref="RespConnectionExecutor.CanCancel"/> - it is the operation that honours it.</remarks>
        public override bool CanCancel => true;

        /// <summary>Whether there is a live connection right now.</summary>
        public bool IsConnectedNow
        {
            get
            {
                lock (_sync) return _connection is { IsClosed: false };
            }
        }

        /// <summary>How many commands are waiting for a connection.</summary>
        public int BacklogCount
        {
            get
            {
                lock (_sync) return _backlog?.Count ?? 0;
            }
        }

        /// <summary>How many times a connection has been established, including the first.</summary>
        public int Connects => Volatile.Read(ref _connects);

        private int _connects;

        /// <inheritdoc/>
        /// <remarks>
        /// Answered without sending anything, as the base class describes: there is one endpoint, so the
        /// question reduces to whether we can currently reach it.
        /// </remarks>
        internal override bool IsReachable(in RedisKey key, CommandFlags flags) => IsConnectedNow;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>An observation, not a borrowed guess.</b> The handshake read this server's version out of
        /// its own <c>HELLO</c> reply on this very connection, so the answer describes the node that will
        /// actually run the command - which is the whole question.
        /// </remarks>
        internal override bool TryGetLocalFeatures(out RedisFeatures features)
        {
            if (_features?.Invoke() is { } observed)
            {
                features = observed;
                return true;
            }

            features = default;
            return false;
        }

        /// <inheritdoc/>
        public override ValueTask<EndPoint?> IdentifyEndpointAsync(
            RedisKey key,
            CommandFlags flags,
            CancellationToken cancellationToken = default)
            => new(_endpoint);

        /// <inheritdoc/>
        public override RespPayload Send(in RespRequest request)
        {
            var operation = Dispatch(in request, default);
            return operation.Wait(operation.Token, TimeSpan.Zero);
        }

        /// <inheritdoc/>
        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            var operation = Dispatch(in request, cancellationToken);
            return new ValueTask<RespPayload>(operation, operation.Token);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>MULTI, the commands, and EXEC as one write</b>, with the receipts standing in the pending
        /// queue for MULTI and EXEC. The queued commands are enqueued too - they have to be, to receive
        /// their <c>+QUEUED</c> - but the connection's hand-off hook takes that receipt and leaves them
        /// pending for <c>EXEC</c> to complete.
        /// </remarks>
        internal override bool TrySendTransaction(List<RespPayloadOperation> operations, out ValueTask<bool> exec)
            // the write itself is not endpoint-specific - it needs a connection and nothing else - so it
            // lives with the transaction, and every executor that owns a connection gets the same one
            => RespTransactionExecutor.TrySendOver(CurrentConnection, operations, out exec);

        /// <inheritdoc/>
        /// <remarks>
        /// Deliberately <b>not</b> a connect trigger. A caller asking which connection this is holds it
        /// to compare against later; answering "none yet, but soon" would be answering a different
        /// question, and starting a connect as a side effect of being asked would be a surprise.
        /// </remarks>
        internal override RespConnection? CurrentConnection
        {
            get
            {
                lock (_sync) return _disposed ? null : _connection;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Yes: it owns a connection, and <c>RespConnection.Send(first, second)</c> already writes two
        /// operations with nothing between them - the same primitive <c>ASKING</c> uses.
        /// </remarks>
        public override bool CanWritePreamble => true;

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// <b>The gate is consulted at WRITE time, holding the connection</b>, which is the entire reason
        /// this cannot be decided when the frames are rendered: whether a <c>SCRIPT LOAD</c> is needed
        /// depends on the endpoint the write lands on, and that is not chosen until here.
        /// </para>
        /// <para>
        /// <b>Declined when there is no live connection</b>, as batches and <c>ASKING</c> are: the pair
        /// must be adjacent, and a backlog drains one at a time. Declining lets the caller fall back to
        /// sending them in sequence, which is a round trip worse and semantically identical.
        /// </para>
        /// </remarks>
        public override ValueTask<RespPayload> SendAsync(
            RespRequest preamble,
            RespRequest request,
            IRespPreambleGate? gate,
            CancellationToken cancellationToken = default)
        {
            RespConnection? connection;
            lock (_sync)
            {
                connection = _disposed || _writeSlotHeld ? null : _connection;

                // No connection yet (or somebody holds the write slot), so there is nothing to pair ON.
                // Do what an executor WITHOUT the capability does - send them in sequence - rather than
                // calling the base, which throws: this executor can pair in general, just not this instant,
                // and the first EVALSHA on a cold connection is exactly that instant.
                if (connection is null || connection.IsClosed) return SequentialAsync(preamble, request, cancellationToken);
            }

            var target = connection as IRespPreambleTarget;

            var head = RespPayloadOperation.Rent();
            head.Attach(preamble.Span, preamble.Flags, default);

            var body = RespPayloadOperation.Rent();
            body.Attach(request.Span, request.Flags, cancellationToken);
            body.Slot = request.Slot;
            _startProfile?.Invoke(body, request.Command, request.Flags, Database, _endpoint);

            // The gate is asked INSIDE the connection's write lock, which is the whole point of this
            // overload: deciding first and writing second lets another sender see this one's claim and then
            // win the lock ahead of it, so its command goes out in front of the preamble it depended on.
            // Harmless for SCRIPT LOAD, silent corruption for SELECT - see RespConnection.Send.
            //
            // The head operation is created speculatively, before the answer is known, because renting one
            // inside the lock is work the lock should not be holding. If the gate declines, nothing was
            // written for it and it is simply discarded.
            bool wroteHead;
            if (!connection.Send(head, body, new Decision(gate, target), static d => d.IsNeeded(), out wroteHead))
            {
                RespPayloadOperation.DiscardReply(head);
                body.EnsureFaulted(request.Flags);
                return new ValueTask<RespPayload>(body, body.Token);
            }

            if (!wroteHead)
            {
                RespPayloadOperation.DiscardReply(head);
                return new ValueTask<RespPayload>(body, body.Token);
            }

            // recorded when the reply lands rather than when the write happens: claiming an effect the
            // server has not confirmed is how a NOSCRIPT gets cached as "loaded"
            if (gate is not null && target is not null) Established(head, gate, target);
            else RespPayloadOperation.DiscardReply(head);

            return new ValueTask<RespPayload>(body, body.Token);
        }

        /// <summary>Consume the preamble's reply, and tell the gate only if it succeeded.</summary>
        private static void Established(RespPayloadOperation head, IRespPreambleGate gate, IRespPreambleTarget target)
        {
            _ = AwaitAsync(head, gate, target);

            static async Task AwaitAsync(RespPayloadOperation head, IRespPreambleGate gate, IRespPreambleTarget target)
            {
                try
                {
                    using var payload = await new ValueTask<RespPayload>(head, head.Token).ConfigureAwait(false);
                    gate.OnEstablished(target);
                }
                catch
                {
                    // the preamble failed, so its effect did not happen and the belief must not be set;
                    // the request that followed will report whatever that costs it
                }
            }
        }

        /// <summary>
        /// The gate and what it is being asked about, as one struct so the predicate can be a static
        /// lambda and allocate nothing on a path that runs per command.
        /// </summary>
        private readonly struct Decision(IRespPreambleGate? gate, IRespPreambleTarget? target)
        {
            /// <remarks>
            /// No gate, or nothing to ask it about, means "write it" - the same answer the pair gave before
            /// gates existed, and the safe one: a preamble sent needlessly costs a round trip, one skipped
            /// wrongly costs correctness.
            /// </remarks>
            internal bool IsNeeded() => gate is null || target is null || gate.IsNeeded(target);
        }

        /// <summary>
        /// The preamble and the request as two sends, the second issued only once the first has landed.
        /// </summary>
        /// <remarks>
        /// <b>Ordering is what the caller actually needs; adjacency is an optimisation on top of it.</b>
        /// Awaiting the preamble gives the ordering at the cost of the round trip a pair would have saved,
        /// which is the same trade <c>AwaitPair</c> makes for an executor that cannot pair at all.
        /// <para>
        /// The gate is not consulted, deliberately: it asks a question about a connection, and the reason
        /// this path exists is that there is not one yet. Sending a preamble that turns out to have been
        /// unnecessary is harmless for both of today's gates - a redundant <c>SCRIPT LOAD</c> or
        /// <c>HIMPORT PREPARE</c> is idempotent - whereas skipping a needed one is not.
        /// </para>
        /// </remarks>
        private async ValueTask<RespPayload> SequentialAsync(
            RespRequest preamble, RespRequest request, CancellationToken cancellationToken)
        {
            (await SendAsync(preamble, cancellationToken).ForAwait())?.Release();
            return await SendAsync(request, cancellationToken).ForAwait();
        }

        /// <inheritdoc/>
        internal override bool CanWriteRuns => true;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Declined when there is no live connection</b>, for the same reason <c>ASKING</c> is: a
        /// backlog drains one operation at a time, which is exactly the adjacency a batch is asking for.
        /// A batch that cannot be written contiguously is not a batch, so saying no is better than
        /// quietly issuing it as a pipeline.
        /// </remarks>
        internal override bool TrySendBatch(List<RespPayloadOperation> operations)
        {
            RespConnection? connection;
            lock (_sync)
            {
                connection = _disposed ? null : _connection;
                if (connection is null || connection.IsClosed)
                {
                    // start connecting anyway: the caller will fail this batch, but the next one should
                    // not have to wait for a connection nobody has asked for yet. Callers that CAN wait
                    // call PrepareRunAsync first and so do not reach this at all.
                    if (!_disposed && _queueWhileDisconnected) EnsureConnecting(); // already holding _sync

                    return false;
                }
            }

            return connection.Send(operations.ToArray(), operations.Count);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The same path an ordinary send takes once its operation exists - connect if needed, backlog if
        /// not yet connected - minus creating one, because a redirected command already has its own and
        /// somebody is already awaiting it.
        /// </remarks>
        internal override bool TryResend(RespPayloadOperation operation) => Enqueue(operation);

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Declined when there is no live connection, rather than backlogged.</b> A backlog drains one
        /// operation at a time, so the pair would lose the adjacency that is the entire point - another
        /// sender can take the write lock between them, and the server would apply the <c>ASKING</c> to
        /// that command instead. Declining lets the redirect stand as the error it arrived as, which is
        /// also the more honest answer: the node we were asked to try is not reachable.
        /// </remarks>
        internal override bool TryResendAsking(RespPayloadOperation operation)
        {
            RespConnection? connection;
            lock (_sync)
            {
                connection = _disposed ? null : _connection;
                if (connection is null || connection.IsClosed) return false;
            }

            var asking = RespPayloadOperation.Rent();
            asking.Attach(AskingFrame, CommandFlags.None, default);
            if (!connection.Send(asking, operation))
            {
                RespPayloadOperation.DiscardReply(asking);
                return false;
            }

            RespPayloadOperation.DiscardReply(asking);
            return true;
        }

        /// <summary>The rendered <c>ASKING</c> command; constant, so it is rendered once.</summary>
        private static ReadOnlySpan<byte> AskingFrame => "*1\r\n$6\r\nASKING\r\n"u8;

        /// <summary>Hand an existing operation to the connection, or to the backlog if there is none.</summary>
        /// <param name="operation">The operation to write.</param>
        private bool Enqueue(RespPayloadOperation operation)
        {
            RespConnection? connection;
            lock (_sync)
            {
                if (_disposed) return false;

                connection = _connection;
                if (connection is null || connection.IsClosed || _writeSlotHeld)
                {
                    if (!_queueWhileDisconnected && !_writeSlotHeld) return false;

                    operation.MarkQueued();
                    (_backlog ??= new()).Enqueue(operation);

                    // ONLY when there is nothing to send on. This branch is shared with "the write slot
                    // is held", where the connection is perfectly good and merely busy - and dialling a
                    // new socket there replaces _connection underneath the slot holder, who then sees its
                    // named connection change and fails the transaction it was in the middle of. That is
                    // exactly how it presented: a conditional transaction reporting the connection lost,
                    // with neither the old nor the new connection closed.
                    if (connection is null || connection.IsClosed) EnsureConnecting();
                    return true;
                }
            }

            return connection.Send(operation);
        }

        private RespPayloadOperation Dispatch(in RespRequest request, CancellationToken cancellationToken)
            => Dispatch(in request, Database, cancellationToken);

        /// <summary>Send, on behalf of a database that may not be this executor's own.</summary>
        /// <param name="request">The rendered request.</param>
        /// <param name="database">The database the command belongs to.</param>
        /// <param name="cancellationToken">Cancels the request before it is sent.</param>
        /// <remarks>
        /// The seam for sharing one connection: a per-database view over this executor sends through here,
        /// naming its own database, and the <c>SELECT</c> that makes that true is written in front of the
        /// command inside the connection's write lock.
        /// </remarks>
        internal RespPayloadOperation Dispatch(in RespRequest request, int database, CancellationToken cancellationToken)
        {
            var operation = RespPayloadOperation.Rent();
            operation.Attach(request.Span, request.Flags, cancellationToken);
            operation.Database = database;

            // started HERE, where the endpoint is finally known: a profiled command reports which server
            // answered it, and until routing has resolved there is no honest answer to that
            _startProfile?.Invoke(operation, request.Command, request.Flags, Database, _endpoint);

            RespConnection? connection;
            lock (_sync)
            {
                if (_disposed)
                {
                    operation.EnsureFaulted(request.Flags);
                    return operation;
                }

                connection = _connection;
                if (connection is null || connection.IsClosed || _writeSlotHeld)
                {
                    // a claim is deliberately handled by the SAME branch as no-connection: both mean
                    // "not writable right now", and both are answered by the backlog, in arrival order
                    if (!_queueWhileDisconnected && !_writeSlotHeld)
                    {
                        operation.EnsureFaulted(request.Flags);
                        return operation;
                    }

                    // WaitingInBacklog, not WaitingToBeSent, and it matters beyond the report: a command
                    // still in the backlog provably never reached a socket, which is what lets
                    // FaultContext.NotApplied bypass retry's side-effect cap if this ends badly
                    operation.MarkQueued();
                    (_backlog ??= new()).Enqueue(operation);

                    // ONLY when there is nothing to send on. This branch is shared with "the write slot
                    // is held", where the connection is perfectly good and merely busy - and dialling a
                    // new socket there replaces _connection underneath the slot holder, who then sees its
                    // named connection change and fails the transaction it was in the middle of. That is
                    // exactly how it presented: a conditional transaction reporting the connection lost,
                    // with neither the old nor the new connection closed.
                    if (connection is null || connection.IsClosed) EnsureConnecting();
                    return operation;
                }
            }

            // OUTSIDE the lock: writing is the slow part, and holding a lock across it would serialise
            // every sender behind one - the exact thing the new core exists to avoid
            if (!Send(connection, operation)) OnSendRefused(connection, operation, request.Flags);
            return operation;
        }

        /// <summary>
        /// Answers "does a <c>SELECT</c> have to go out in front of this command?", from inside the write
        /// lock, and builds one only when it does.
        /// </summary>
        private readonly struct Selector(RespConnection connection, SelectPreamble select, int database)
        {
            internal IRespMessage? Preamble()
            {
                // TrySelectDatabase both asks and claims; see IRespPreambleTarget for why those cannot be
                // two steps. A connection that manages its own database answers false and nothing is added.
                if (connection is not IRespPreambleTarget target || !target.TrySelectDatabase(database)) return null;

                var frame = select.For(database);
                var head = RespPayloadOperation.Rent();
                head.Attach(frame.Span, frame.Flags, default);
                return head;
            }
        }

        private void OnSendRefused(RespConnection connection, RespPayloadOperation operation, CommandFlags flags)
        {
            // the connection died between our reading it and our writing to it. Backlog rather than fail:
            // this command never reached a socket, so it is exactly the case the backlog exists for
            lock (_sync)
            {
                if (ReferenceEquals(_connection, connection)) _connection = null;

                if (_queueWhileDisconnected && !_disposed)
                {
                    operation.MarkQueued();
                    (_backlog ??= new()).Enqueue(operation);

                    // ONLY when there is nothing to send on. This branch is shared with "the write slot
                    // is held", where the connection is perfectly good and merely busy - and dialling a
                    // new socket there replaces _connection underneath the slot holder, who then sees its
                    // named connection change and fails the transaction it was in the middle of. That is
                    // exactly how it presented: a conditional transaction reporting the connection lost,
                    // with neither the old nor the new connection closed.
                    if (connection is null || connection.IsClosed) EnsureConnecting();
                    return;
                }
            }

            operation.EnsureFaulted(flags);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// On success the <b>caller owns the write slot</b> and must release it; the backlog has been
        /// drained, so writing a run now cannot overtake anything that arrived earlier.
        /// </remarks>
        internal override async ValueTask<bool> PrepareRunAsync(CancellationToken cancellationToken = default)
        {
            var held = false;
            var captured = false;

            // What was already queued when the slot was taken, and ONLY that. Draining the live backlog
            // instead was the subtle version of this bug: by the time the slot holder was ready, commands
            // issued AFTER it took the slot had joined the queue, and draining wholesale wrote them out
            // in front of the run. Arrival order at the connection is not the rule - the rule is that
            // whoever holds the slot goes ahead of everything issued after they took it.
            Queue<RespPayloadOperation>? earlier = null;

            try
            {
                while (true)
                {
                    TaskCompletionSource<bool>? slotWait = null;
                    Task? connectWait = null;
                    var ready = false;

                    lock (_sync)
                    {
                        if (_disposed) return false;

                        if (held && !captured)
                        {
                            // taken under the lock the moment the slot is ours, so nothing can slip in
                            // between owning it and deciding what counts as "earlier"
                            earlier = _backlog;
                            _backlog = null;
                            captured = true;
                        }

                        if (!held)
                        {
                            if (_writeSlotHeld)
                            {
                                slotWait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                (_writeWaiters ??= new()).Enqueue(slotWait);
                            }
                            else
                            {
                                _writeSlotHeld = true;
                                held = true;
                                earlier = _backlog;
                                _backlog = null;
                                captured = true;
                            }
                        }

                        if (held)
                        {
                            if (_connection is { IsClosed: false })
                            {
                                ready = true;
                            }
                            else if (!_queueWhileDisconnected)
                            {
                                return false;
                            }
                            else
                            {
                                EnsureConnecting(); // already holding _sync
                                connectWait = _connecting;
                            }
                        }
                    }

                    if (ready)
                    {
                        // anything that queued earlier goes first; only then does the caller's run get
                        // written, which is what makes owning the slot an ordering guarantee rather than
                        // merely an exclusion one
                        Write(earlier);
                        earlier = null;
                        held = false; // ownership passes to the caller, who releases it
                        return true;
                    }

                    try
                    {
                        if (slotWait is not null)
                        {
                            held = await slotWait.Task.ConfigureAwait(false);
                        }
                        else if (connectWait is not null)
                        {
                            await connectWait.ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        return false; // the connect failed; the caller fails the run, and says why
                    }
                }
            }
            finally
            {
                if (held)
                {
                    // never written, so they go back at the FRONT: they were queued before anything that
                    // has arrived since, and losing that is losing the ordering this method exists for
                    if (earlier is { Count: > 0 })
                    {
                        lock (_sync)
                        {
                            if (_backlog is { Count: > 0 })
                            {
                                foreach (var later in _backlog) earlier.Enqueue(later);
                            }

                            _backlog = earlier;
                        }
                    }

                    ReleaseWrites();
                }
            }
        }

        /// <inheritdoc/>
        internal override void ReleaseWrites()
        {
            while (true)
            {
                DrainBacklog();

                lock (_sync)
                {
                    // re-checked under the lock rather than assumed: freeing the slot with anything still
                    // queued would reopen the overtaking window the slot exists to close, one level down
                    if (_connection is { IsClosed: false } && _backlog is { Count: > 0 }) continue;

                    if (_writeWaiters is { Count: > 0 })
                    {
                        // handed over directly, so the slot is never momentarily free for a newcomer to
                        // take ahead of somebody who has been waiting
                        _writeWaiters.Dequeue().TrySetResult(true);
                        return;
                    }

                    _writeSlotHeld = false;
                    return;
                }
            }
        }

        /// <summary>Write everything backlogged, in arrival order, while the slot is held.</summary>
        private void DrainBacklog()
        {
            while (true)
            {
                Queue<RespPayloadOperation>? waiting;
                lock (_sync)
                {
                    // only drain what a live connection can take; otherwise leave it for the connect,
                    // which is the path that already knows how
                    if (_connection is not { IsClosed: false } || _backlog is not { Count: > 0 }) return;

                    waiting = _backlog;
                    _backlog = null;
                }

                Write(waiting);
            }
        }

        /// <summary>Write a captured queue, in order.</summary>
        /// <remarks>
        /// <b>Selects, like any other write.</b> This is not a rare path that can be excused: a command
        /// issued before the connection came up is backlogged and drained here, so on a connection serving
        /// several databases the FIRST command after connecting always arrives this way. Writing it without
        /// its <c>SELECT</c> would run it against whatever the handshake selected - and only the first one,
        /// which is the kind of bug that looks like a fluke.
        /// </remarks>
        private void Write(Queue<RespPayloadOperation>? waiting)
        {
            if (waiting is null) return;
            while (waiting.Count != 0)
            {
                var operation = waiting.Dequeue();
                if (_connection is { IsClosed: false } connection && Send(connection, operation)) continue;
                operation.EnsureFaulted(CommandFlags.None);
            }
        }

        /// <summary>Write one operation, putting a <c>SELECT</c> in front of it if this connection needs one.</summary>
        /// <remarks>
        /// The database comes from the OPERATION, not from this executor: one connection can be shared by
        /// several databases, and the backlog can hold commands for more than one of them at once.
        /// </remarks>
        private bool Send(RespConnection connection, RespPayloadOperation operation)
        {
            if (_select is null || operation.Database < 0) return connection.Send(operation);

            var sent = connection.Send(
                operation, new Selector(connection, _select, operation.Database), static s => s.Preamble(), out var head);

            // the SELECT's own reply is nobody's business: it is +OK or the connection is broken, and the
            // command behind it reports that far better than a reply nobody asked for
            if (head is RespPayloadOperation discard) RespPayloadOperation.DiscardReply(discard);
            return sent;
        }

        /// <summary>Start a connection attempt, unless one is already running.</summary>
        /// <remarks>
        /// Single-flight: everybody who arrives while a connect is in progress waits on that one rather
        /// than starting a competing attempt. Called with the lock held.
        /// </remarks>
        private void EnsureConnecting()
        {
            if (_connecting is not null) return;
            _connecting = Task.Run(ConnectAsync);
        }

        private async Task<RespConnection> ConnectAsync()
        {
            try
            {
                var connection = await _connect(CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref _connects);

                bool drain;
                lock (_sync)
                {
                    _connecting = null;
                    if (_disposed)
                    {
                        _ = connection.DisposeAsync();
                        throw new ObjectDisposedException(nameof(RespEndpointExecutor));
                    }

                    _connection = connection;

                    // if somebody owns the write slot they are waiting on this very task, and THEY will
                    // drain - draining here as well would write the backlog out from under them
                    drain = !_writeSlotHeld;
                    if (drain) _writeSlotHeld = true;
                }

                if (drain) ReleaseWrites(); // drains in arrival order, then frees or hands on the slot

                return connection;
            }
            catch (Exception ex)
            {
                Queue<RespPayloadOperation>? stranded;
                lock (_sync)
                {
                    _connecting = null;
                    stranded = _backlog;
                    _backlog = null;
                }

                // a failed connect fails what was waiting for it; holding them for a later attempt would
                // be a queue that grows without bound while a server is down
                if (stranded is not null)
                {
                    while (stranded.Count != 0) Fail(stranded.Dequeue(), ex);
                }

                throw;
            }
        }

        private static void Fail(RespPayloadOperation operation, Exception exception)
            => operation.TrySetException(operation.Token, exception, definite: false);

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            RespConnection? connection;
            Queue<RespPayloadOperation>? stranded;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                connection = _connection;
                _connection = null;
                stranded = _backlog;
                _backlog = null;
            }

            if (stranded is not null)
            {
                var ex = new ObjectDisposedException(nameof(RespEndpointExecutor));
                while (stranded.Count != 0) Fail(stranded.Dequeue(), ex);
            }

            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
