using System;
using System.Collections.Generic;
using System.Globalization;
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
    internal sealed class RespEndpointExecutor : RespExecutorBase, IAsyncDisposable, IRespOutcomeObserver
    {
        private readonly Func<CancellationToken, Task<RespConnection>> _connect;
        private readonly Func<RedisFeatures?>? _features;
        private readonly Func<RespPayloadOperation, RedisCommand, CommandFlags, int, EndPoint?, object?>? _startProfile;
        private readonly EndPoint? _endpoint;
        private readonly bool _queueWhileDisconnected;

        /// <summary>How long a connection attempt may take before it is abandoned; zero or less means no limit.</summary>
        /// <remarks>
        /// <b>Without this a stalled connect hangs the endpoint permanently.</b> One attempt is in flight at
        /// a time and everything that arrives meanwhile goes to the backlog behind it, so a connect that
        /// never completes is not one slow command - it is every command on this endpoint, for ever. The
        /// shipped core bounds the same step with <c>ConnectTimeout</c> (see <c>PhysicalConnection</c>);
        /// this had no bound at all, which is what left <c>ReconnectRetryPolicyUnitTests</c> hanging under
        /// the new core rather than failing its ping.
        /// </remarks>
        private readonly int _connectTimeoutMilliseconds;

        /// <summary>The caller's reconnect backoff, asked before every attempt after the first failure.</summary>
        /// <remarks>
        /// <b>A function, not a value</b>, because the policy is read from configuration that can be
        /// rebound underneath a long-lived executor - the same reason the features probe is a function.
        /// </remarks>
        private readonly Func<IReconnectRetryPolicy?>? _retryPolicy;

        /// <summary>How many reconnects have already been refused or failed since the last success.</summary>
        private long _connectRetryCount;

        /// <summary><see cref="Environment.TickCount"/> when the last attempt started.</summary>
        private int _lastConnectTicks = Environment.TickCount;
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
        /// <param name="connectTimeoutMilliseconds">How long a connection attempt may take before it is abandoned; zero or less means no limit.</param>
        /// <param name="retryPolicy">The reconnect backoff to consult before re-attempting a failed connect.</param>
        /// <param name="circuitBreaker">
        /// Makes the accumulator this endpoint counts outcomes into, or null when nobody is counting.
        /// </param>
        /// <param name="onCircuitBroken">Announces that this endpoint's breaker has tripped.</param>
        /// <param name="abortPendingOnConnectionFailure">
        /// Whether a failed connect abandons what was queued, or leaves it to wait for a later attempt.
        /// Null means abandon, which is the safe reading when nobody has said.
        /// </param>
        /// <param name="backlogTimeoutMilliseconds">How long a command may wait in the backlog.</param>
        /// <param name="server">
        /// This endpoint's modelled server, for the beliefs that live there rather than on a connection -
        /// principally whether a maintenance window is relaxing timeouts. A function because the executor
        /// can outlive any particular <c>ServerEndPoint</c>, and null when nobody models this endpoint.
        /// </param>
        /// <param name="noConnection">
        /// Describes "no connection was available" the way the shipped core describes it - which endpoints
        /// were tried, what each last failed with, how far connecting had got. Null falls back to a bare
        /// statement that a connection was not available.
        /// </param>
        internal RespEndpointExecutor(
            Func<CancellationToken, Task<RespConnection>> connect,
            int database = 0,
            EndPoint? endpoint = null,
            bool queueWhileDisconnected = true,
            Func<RedisFeatures?>? features = null,
            Func<RespPayloadOperation, RedisCommand, CommandFlags, int, EndPoint?, object?>? startProfile = null,
            SelectPreamble? select = null,
            int connectTimeoutMilliseconds = 0,
            Func<IReconnectRetryPolicy?>? retryPolicy = null,
            Func<Availability.CircuitBreaker.Accumulator?>? circuitBreaker = null,
            Action? onCircuitBroken = null,
            Func<RedisCommand, string?, Exception>? noConnection = null,
            Func<bool>? abortPendingOnConnectionFailure = null,
            Func<int>? backlogTimeoutMilliseconds = null,
            Func<ServerEndPoint?>? server = null)
        {
            _server = server;
            _circuitBreakerFactory = circuitBreaker;
            _onCircuitBroken = onCircuitBroken;
            _noConnection = noConnection;
            _abortPendingOnConnectionFailure = abortPendingOnConnectionFailure;
            _backlogTimeoutMilliseconds = backlogTimeoutMilliseconds;
            _circuitBreaker = circuitBreaker?.Invoke();
            _connectTimeoutMilliseconds = connectTimeoutMilliseconds;
            _retryPolicy = retryPolicy;
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

        private readonly Func<ServerEndPoint?>? _server;
        private ServerEndPoint? _serverCache;

        /// <summary>This endpoint's modelled server, or null when nobody models it.</summary>
        /// <remarks>
        /// Resolved once and kept: the lookup walks the multiplexer's endpoint table, and this is asked on
        /// the timeout path where a dictionary probe per command would be pure overhead. A miss is not
        /// cached, so an endpoint that becomes modelled later is still picked up.
        /// </remarks>
        private ServerEndPoint? Server => _serverCache ??= _server?.Invoke();

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

        /// <summary>Commands this endpoint can still be expected to finish.</summary>
        /// <remarks>
        /// <b>"Still going", not "not yet answered", and the distinction is the whole of it.</b> Written
        /// commands count only while the connection that carries them is alive - once it is gone they are
        /// about to be faulted, not completed. Queued commands count only while there is a connection or one
        /// on the way; a backlog with nothing dialling it is waiting for something that is not coming, which
        /// is precisely the state a shutdown finds after a connection failure. Counting those made closing
        /// wait out its whole timeout for commands that could never land.
        /// </remarks>
        internal int UnfinishedCount
        {
            get
            {
                lock (_sync)
                {
                    var live = _connection is { IsClosed: false };
                    var pending = live ? _connection!.PendingCount : 0;
                    var queued = live || _connecting is not null ? _backlog?.Count ?? 0 : 0;
                    return pending + queued;
                }
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
        /// <b>Deferred is the interesting one</b>: no connection, nothing in flight, and nothing has
        /// failed - this endpoint simply has not been asked for anything yet, which on a lazily-dialled
        /// core is the ordinary state of most of a cluster and is not a fault.
        /// </remarks>
        internal override RespConnectionState ConnectionStateNow(in RedisKey key, CommandFlags flags)
        {
            lock (_sync)
            {
                if (_disposed) return RespConnectionState.Unroutable;
                if (_connection is { IsClosed: false }) return RespConnectionState.Connected;
                if (_connecting is not null) return RespConnectionState.Connecting;
            }

            // outside the lock: a failed attempt has already cleared _connecting, and the count is what
            // distinguishes "tried and is not working" from "nobody has needed this yet"
            return Volatile.Read(ref _connectRetryCount) > 0
                ? RespConnectionState.Connecting
                : RespConnectionState.Deferred;
        }

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

            // FIRE-AND-FORGET DECLINES THE OUTCOME, INCLUDING THE BAD ONES. Waiting here returned the real
            // result and the real exception to a caller who had said they were not going to look - so a
            // command issued this way against a server that had just gone away threw at them. The reply is
            // drained and discarded exactly as a preamble's is: still sent, still completed, nobody told.
            //
            // Null rather than a payload, which is what Parse already expects and documents: "the caller
            // has explicitly declined it, so the pipeline never captures one and the executor hands back
            // null". The executor simply never did.
            //
            // SYNCHRONOUS ONLY so far: the same short-circuit on SendAsync still breaks the scripting
            // tests, and neither of the two obvious causes turned out to be it. See design notes 9b-vi
            // for what has been ruled out.
            if ((request.Flags & CommandFlags.FireAndForget) != 0)
            {
                RespPayloadOperation.DiscardReply(operation);
                return null!;
            }

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
            => TrySendTransaction(operations, Database, out exec);

        /// <summary>Write a transaction on behalf of a database that may not be this executor's own.</summary>
        /// <param name="operations">The queued commands.</param>
        /// <param name="database">The database the whole transaction belongs to.</param>
        /// <param name="exec">Completes with whether <c>EXEC</c> applied.</param>
        /// <remarks>
        /// The <c>SELECT</c> goes in front of <c>MULTI</c>, which is the only place it can: inside the
        /// transaction it would be queued and applied at <c>EXEC</c> like any other command.
        /// </remarks>
        internal bool TrySendTransaction(List<RespPayloadOperation> operations, int database, out ValueTask<bool> exec)
        {
            // the write itself is not endpoint-specific - it needs a connection and nothing else - so it
            // lives with the transaction, and every executor that owns a connection gets the same one
            var connection = CurrentConnection;
            return RespTransactionExecutor.TrySendOver(
                connection,
                operations,
                out exec,
                onAborted: null,
                send: connection is null ? null : (run, count) => SendRun(connection, run, count, database));
        }

        /// <summary>Write a run for a database that may not be this executor's own.</summary>
        internal bool TryWriteRun(RespConnection connection, IRespMessage[] run, int count, int database)
        {
            return SendRun(connection, run, count, database);
        }

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
                if (connection is null || connection.IsClosed) return SequentialAsync(preamble, request, gate, cancellationToken);
            }

            var target = connection as IRespPreambleTarget;

            var head = RespPayloadOperation.Rent();
            head.Attach(preamble.Span, preamble.Flags, default);
            head.Observer = this;

            var body = RespPayloadOperation.Rent();
            body.Attach(request.Span, request.Flags, cancellationToken);
            body.Slot = request.Slot;
            body.Command = request.Command;
            body.Observer = this;
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
                body.EnsureFaulted(request.Flags, NoConnection(request.Command, body.CommandAndKey));
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
        /// The gate is not <i>consulted</i>, deliberately: it asks a question about a connection, and the
        /// reason this path exists is that there is not one yet. Sending a preamble that turns out to have
        /// been unnecessary is harmless for both of today's gates - a redundant <c>SCRIPT LOAD</c> or
        /// <c>HIMPORT PREPARE</c> is idempotent - whereas skipping a needed one is not.
        /// </para>
        /// <para>
        /// <b>But it is told, which is a different question and was the bug.</b> Not recording left the
        /// belief unset forever in the one case this path always covers: the FIRST evaluation on a fresh
        /// connection. Nothing ever learned the script was loaded, so every later call re-sent the
        /// <c>SCRIPT LOAD</c> as well - correct, and permanently paying for a round trip the whole design
        /// exists to remove. It survived the suite because the result is right either way.
        /// </para>
        /// <para>
        /// Recorded against whatever connection is live once the preamble has landed, which is sound for
        /// the scope that needs it: a script is the SERVER's, and this executor is one endpoint, so any
        /// live connection of its own answers the same. A connection-local belief would not be safe to
        /// record this way - and is not, because the gate that holds one claims in <c>IsNeeded</c> and
        /// does nothing here.
        /// </para>
        /// </remarks>
        private async ValueTask<RespPayload> SequentialAsync(
            RespRequest preamble, RespRequest request, IRespPreambleGate? gate, CancellationToken cancellationToken)
        {
            // BOTH QUEUED, THEN AWAITED - not "await the preamble, then send". Awaiting first let anything
            // the caller issued in the meantime go out in between: a script whose SCRIPT LOAD was still in
            // flight had its EVALSHA written AFTER the reads that followed it at the call site, so those
            // reads saw the state from before the script ran. Ordering is per connection and Dispatch
            // already preserves it, so the await bought nothing and cost exactly that.
            //
            // The preamble still precedes the request on the wire, which is all EVALSHA needs: the server
            // processes them in order, so the script is loaded by the time the hash is used.
            var head = Dispatch(in preamble, Database, cancellationToken, profile: false);
            var body = SendAsync(request, cancellationToken);

            await Established(head, gate).ForAwait();
            return await body.ForAwait();
        }

        /// <inheritdoc/>
        internal override ValueTask SendPreambleAsync(
            RespRequest preamble, IRespPreambleGate? gate, CancellationToken cancellationToken = default)
        {
            // dispatched rather than sent, so the preamble stays out of any profiling session
            var head = Dispatch(in preamble, Database, cancellationToken, profile: false);
            return new ValueTask(Established(head, gate));
        }

        /// <summary>Await a preamble's reply and, if it succeeded, tell the gate.</summary>
        /// <param name="head">The preamble, already on its way.</param>
        /// <param name="gate">The condition it establishes; null when nobody is counting.</param>
        private async Task Established(RespPayloadOperation head, IRespPreambleGate? gate)
        {
            (await new ValueTask<RespPayload>(head, head.Token).ForAwait())?.Release();

            // told AFTER the await, so the connection recorded against is one that exists, and only on
            // success - a preamble that threw did not establish anything.
            //
            // Recorded against whatever connection is live by then, which is sound for the scope that needs
            // it: a script is the SERVER's, and this executor is one endpoint, so any live connection of its
            // own answers the same question. A connection-local belief would not be safe to record this
            // way, and is not - the gate that holds one claims in IsNeeded and does nothing here.
            if (gate is not null)
            {
                RespConnection? established;
                lock (_sync) established = _disposed ? null : _connection;
                if (established is IRespPreambleTarget target && !established.IsClosed) gate.OnEstablished(target);
            }
        }

        /// <inheritdoc/>
        internal override bool CanWriteRuns => true;

        /// <inheritdoc/>
        /// <remarks>Yes: it owns the connection a transaction holds, and the write claim on it.</remarks>
        internal override bool CanWriteTransactions => true;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Declined when there is no live connection</b>, for the same reason <c>ASKING</c> is: a
        /// backlog drains one operation at a time, which is exactly the adjacency a batch is asking for.
        /// A batch that cannot be written contiguously is not a batch, so saying no is better than
        /// quietly issuing it as a pipeline.
        /// </remarks>
        internal override bool TrySendBatch(List<RespPayloadOperation> operations)
            => TrySendBatch(operations, Database);

        /// <summary>Write a batch on behalf of a database that may not be this executor's own.</summary>
        /// <param name="operations">The queued commands.</param>
        /// <param name="database">The database the whole run belongs to.</param>
        /// <remarks>
        /// <b>One <c>SELECT</c> governs the entire run.</b> A batch is composed through a single context
        /// and so belongs to a single database, and the run is written with nothing of anybody else's in
        /// between - so the selection holds for exactly as long as the run needs it.
        /// </remarks>
        internal bool TrySendBatch(List<RespPayloadOperation> operations, int database)
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
                    // NeverConnected too: under FailFast the first command is backlogged rather than
                    // refused (see NeverConnected), and a backlog nobody dials for would simply wait
                    if (!_disposed && (_queueWhileDisconnected || NeverConnected)) EnsureConnecting(); // already holding _sync

                    return false;
                }
            }

            return SendRun(connection, operations.ToArray(), operations.Count, database);
        }

        /// <inheritdoc/>
        internal override bool TryWriteRun(RespConnection connection, IRespMessage[] run, int count)
            => SendRun(connection, run, count, Database);

        /// <summary>Write a run, preceded by a <c>SELECT</c> if this connection is on another database.</summary>
        internal bool SendRun(RespConnection connection, IRespMessage[] run, int count, int database)
        {
            // a batch's and a transaction's operations are built by the composing executor rather than by
            // Dispatch, so this is where they learn who counts their outcome. Without it a deployment whose
            // EXEC always fails was never judged unhealthy - the breaker saw only the single sends.
            for (var i = 0; i < count; i++)
            {
                if (run[i] is RespPayloadOperation operation)
                {
                    operation.Observer = this;
                    operation.Server = Server;
                }
            }

            if (_select is null || database < 0) return connection.Send(run, count);

            var sent = connection.Send(
                run, count, new Selector(connection, _select, database), static s => s.Preamble(), out var head);

            if (head is RespPayloadOperation discard) RespPayloadOperation.DiscardReply(discard);
            return sent;
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
                    if (!_queueWhileDisconnected && !_writeSlotHeld && !NeverConnected) return false;

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

        /// <summary>Where this endpoint's outcomes are counted; null when nobody is counting.</summary>
        /// <remarks>
        /// Replaced when a trip is actuated, so each connection's lifetime gets its own counters - which is
        /// what the shipped core gets structurally, by building an accumulator per <c>PhysicalConnection</c>.
        /// </remarks>
        private Availability.CircuitBreaker.Accumulator? _circuitBreaker;

        private readonly Func<Availability.CircuitBreaker.Accumulator?>? _circuitBreakerFactory;

        /// <summary>Strictly healthy -> tripped -> actuated, then healthy again with fresh counters.</summary>
        private int _circuitBreakerState;

        private const int CircuitHealthy = 0, CircuitTripped = 1, CircuitActuated = 2;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Runs per completed command, on the completion thread</b>, so it does as little as it can: the
        /// accumulator decides what counts as a failure and a null fault is a success. If it trips, the
        /// teardown is handed to the pool rather than done here - failing a backlog and building a detailed
        /// exception is not work to put on the thread that just finished somebody's GET. The shipped core
        /// makes the same split, in <c>PhysicalConnection.ObserveMessageResult</c>.
        /// </remarks>
        void IRespOutcomeObserver.ObserveOutcome(Exception? fault)
        {
            var accumulator = Volatile.Read(ref _circuitBreaker);
            if (accumulator is null || !accumulator.Trip(fault)) return;

            if (Interlocked.CompareExchange(ref _circuitBreakerState, CircuitTripped, CircuitHealthy) == CircuitHealthy)
            {
                ThreadPool.QueueUserWorkItem(s_CircuitBroken, this);
            }
        }

        private static readonly WaitCallback s_CircuitBroken = static state =>
        {
            var executor = (RespEndpointExecutor)state!;
            try
            {
                executor.ActuateTrip();
            }
            catch
            {
                // a breaker that cannot tear down is not a reason to bring the process down with it
            }
        };

        /// <summary>Act on a trip, at most once per connection lifetime.</summary>
        /// <remarks>
        /// <b>The notification matters more than the teardown here.</b> Dropping the connection stops us
        /// sending into a server the breaker has judged unhealthy, but it is the <c>ConnectionFailed</c>
        /// event that a connection group listens for, and without it a tripped member is simply a member
        /// that keeps failing.
        /// <para>
        /// Fresh counters afterwards, so the next connection starts clean rather than inheriting the
        /// judgement that condemned the last one.
        /// </para>
        /// </remarks>
        private void ActuateTrip()
        {
            if (Interlocked.CompareExchange(ref _circuitBreakerState, CircuitActuated, CircuitTripped) != CircuitTripped)
            {
                return;
            }

            // disposed from the pool rather than from a completion path: this is the same connection whose
            // read loop delivered the reply that tripped us, and tearing it down from inside itself is the
            // self-join that the MOVED work already found the hard way
            var dropped = DropConnection();

            _onCircuitBroken?.Invoke();
            _ = dropped;

            Volatile.Write(ref _circuitBreaker, _circuitBreakerFactory?.Invoke());
            Volatile.Write(ref _circuitBreakerState, CircuitHealthy);
        }

        /// <summary>Bring this endpoint up now, and wait for it.</summary>
        /// <param name="cancellationToken">Abandons the wait; the attempt itself carries on.</param>
        /// <remarks>
        /// <b>The one thing an on-demand core has no other way to say.</b> Everything else here dials
        /// because a command needed it, so "is this endpoint up?" could only be answered by sending
        /// something. Connecting is what <c>ConnectAsync</c> has to wait for, and what makes the topology
        /// probes on that first handshake land before anybody asks a question that depends on them.
        /// </remarks>
        internal Task ConnectNowAsync(CancellationToken cancellationToken = default)
        {
            Task<RespConnection>? pending;
            lock (_sync)
            {
                if (_disposed) return Task.CompletedTask;
                if (_connection is { IsClosed: false }) return Task.CompletedTask;

                EnsureConnecting(); // may decline on backoff, in which case it has armed a retry
                pending = _connecting;
            }

            return pending is null ? Task.CompletedTask : pending.WaitAsync(cancellationToken);
        }

        /// <summary>Drop whatever connection this endpoint currently holds, if any.</summary>
        /// <returns>Whether there was one to drop.</returns>
        /// <remarks>
        /// <b>Never from a completion path or a read loop</b> - disposing a connection from inside itself
        /// is a self-join, which the MOVED work already found the hard way. Callers are the trip worker and
        /// the test-only failure simulation, both of which arrive from somewhere else.
        /// <para>
        /// The backlog is deliberately left alone: a dropped connection is exactly the case it exists for,
        /// and the next command dials again and drains it.
        /// </para>
        /// </remarks>
        internal bool DropConnection(bool reconnectImmediately = false)
        {
            RespConnection? doomed;
            lock (_sync)
            {
                doomed = _connection;
                _connection = null;
            }

            if (doomed is null) return false;
            RecordFault(doomed);
            _ = doomed.DisposeAsync();

            // AND START GETTING IT BACK. Losing a connection is the event that should lead to a reconnect,
            // and under FailFast nothing else will ever ask for one: those commands are refused without
            // dialling, deliberately, so an endpoint that dropped once stayed down forever and the
            // deployment never recovered from a blip. Armed rather than dialled, so the retry policy still
            // decides when - and so this costs nothing per command, which is what the refusal is for.
            if (_disposed) return true;

            if (reconnectImmediately)
            {
                lock (_sync) EnsureConnecting(force: true);
            }
            else
            {
                ArmConnectRetry();
            }

            return true;
        }

        /// <summary>Told when this endpoint's breaker has tripped, so the failure can be announced.</summary>
        private readonly Action? _onCircuitBroken;

        /// <inheritdoc cref="RespPayloadOperation.EnsureFaulted(CommandFlags, Exception?)"/>
        private readonly Func<RedisCommand, string?, Exception>? _noConnection;

        /// <summary>Whether a failed connect abandons the backlog; see <c>BacklogPolicy</c>.</summary>
        private readonly Func<bool>? _abortPendingOnConnectionFailure;

        /// <summary>How long a command may wait in the backlog, in milliseconds.</summary>
        private readonly Func<int>? _backlogTimeoutMilliseconds;

        /// <summary>Why there is no connection: the last one's fault, or the last attempt's.</summary>
        /// <remarks>
        /// <b>A lost connection counts, not only a failed dial.</b> A command waiting in the backlog is
        /// usually waiting because the connection it would have used went away, and by the time the first
        /// retry is even due there may be no attempt to report - so reporting only attempts left the
        /// diagnosis saying that nothing was available without saying why.
        /// </remarks>
        private Exception? _lastConnectFault;

        /// <summary>Remember why a connection ended.</summary>
        /// <param name="connection">The connection that is going away.</param>
        /// <remarks>
        /// A connection that was CLOSED rather than broken has no fault of its own to report - a retirement,
        /// a circuit-breaker trip, a simulated failure - so one is written down instead. "No connection
        /// became available" followed by nothing is a diagnosis that does not diagnose, and "the connection
        /// was closed" is at least true and at least distinguishes it from never having had one.
        /// </remarks>
        private void RecordFault(RespConnection? connection)
        {
            if (connection is null) return;

            var fault = connection.Fault ?? new RedisConnectionException(
                ConnectionFailureType.SocketClosed,
                CommandFlags.None,
                $"The connection to {Format.ToString(_endpoint)} was closed.",
                null,
                CommandStatus.Unknown);

            Volatile.Write(ref _lastConnectFault, fault);
        }

        /// <summary>Periodic upkeep: time out whatever has waited too long, queued or in flight.</summary>
        /// <param name="timeoutMilliseconds">The configured command timeout.</param>
        /// <remarks>
        /// <b>Driven by the multiplexer heartbeat, which is where the shipped core does this too.</b> A
        /// command that has been WRITTEN has nothing else bounding it: the caller's wait had no deadline
        /// and the operation backstop is two minutes away, so a server that stops answering - paused,
        /// wedged, gone quiet - left commands hanging rather than timing out.
        /// <para>
        /// The RELAXED timeout where a maintenance window says so, since a window exists precisely to
        /// rescue commands that would otherwise expire while a server is moved.
        /// </para>
        /// </remarks>
        internal void OnHeartbeat(int timeoutMilliseconds)
        {
            ExpireBacklog();

            if (timeoutMilliseconds <= 0) return;

            RespConnection? connection;
            lock (_sync) connection = _connection;
            if (connection is null || connection.IsClosed) return;

            timeoutMilliseconds = EffectiveTimeout(timeoutMilliseconds);
            if (timeoutMilliseconds > 0) connection.ExpirePending(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        }

        /// <summary>The timeout that actually applies, after any maintenance window has had its say.</summary>
        /// <param name="timeoutMilliseconds">The configured timeout.</param>
        /// <remarks>
        /// A window exists precisely to rescue commands that would otherwise expire while a server is being
        /// moved, so every sweep that can end a command has to ask - the queued ones as much as the written
        /// ones, because a command waiting for a connection to a server that is mid-migration is the exact
        /// case the window is for.
        /// </remarks>
        private int EffectiveTimeout(int timeoutMilliseconds)
            => Server is { } server ? server.GetEffectiveTimeoutMilliseconds(timeoutMilliseconds) : timeoutMilliseconds;

        /// <summary>Fail anything that has waited longer than it agreed to.</summary>
        /// <remarks>
        /// <b>The other half of letting a backlog survive a failed connect.</b> Commands wait because a
        /// failure may be transient - that is what the queue is for - but waiting has to end, and it ends
        /// at the timeout the caller already has. Without this the only bound was the operation backstop,
        /// two minutes later, which is not a timeout so much as a last resort.
        /// <para>
        /// Reported as the backlog timeout it is, naming what the connection attempts have been failing
        /// with: a caller told only "timed out" has to guess whether anything was even being tried.
        /// </para>
        /// </remarks>
        private void ExpireBacklog()
        {
            var timeout = EffectiveTimeout(_backlogTimeoutMilliseconds?.Invoke() ?? 0);
            if (timeout <= 0) return;

            List<RespPayloadOperation>? expired = null;
            lock (_sync)
            {
                if (_backlog is not { Count: > 0 } backlog) return;

                var keep = new Queue<RespPayloadOperation>(backlog.Count);
                while (backlog.Count != 0)
                {
                    var operation = backlog.Dequeue();
                    if (operation.Diagnostics.Age.TotalMilliseconds >= timeout) (expired ??= new()).Add(operation);
                    else keep.Enqueue(operation);
                }

                _backlog = keep.Count == 0 ? null : keep;
            }

            if (expired is null) return;

            // built rather than borrowed: ExceptionFactory.Timeout needs a Message, which this core does
            // not have - so the wording is matched here, including the inner exception the shipped text
            // quotes, because that is what callers read and what tests assert
            var last = Volatile.Read(ref _lastConnectFault);
            var text = last is null
                ? $"The message timed out in the backlog attempting to send because no connection became available ({timeout}ms)"
                : $"The message timed out in the backlog attempting to send because no connection became available ({timeout}ms) - Last Connection Exception: {last.Message}";

            foreach (var operation in expired)
            {
                // the type follows the shipped rule, which is not cosmetic: a connection exception is
                // reported only when connecting has actually been FAILING, because then the timeout is a
                // symptom and the connection fault is the cause. A command merely waiting behind a connect
                // that is slow rather than broken timed out, and callers catch RedisTimeoutException for
                // that - see ExceptionFactory.Timeout's `logConnectionException`.
                var maintenance = operation.MaintenanceTypeForFault;
                Exception fault;
                if (last is null)
                {
                    fault = new RedisTimeoutException(operation.Flags, text, CommandStatus.WaitingInBacklog)
                    {
                        MaintenanceType = maintenance,
                    };
                }
                else
                {
                    fault = new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        operation.Flags,
                        text,
                        last,
                        CommandStatus.WaitingInBacklog)
                    {
                        MaintenanceType = maintenance,
                    };
                }

                Fail(operation, fault);
            }
        }

        /// <summary>The failure a command gets when there was nothing to send it on.</summary>
        /// <param name="command">The command, so the diagnosis can name it.</param>
        /// <param name="commandAndKey">The command and the key it named, when that can be recovered.</param>
        private Exception? NoConnection(RedisCommand command, string? commandAndKey = null)
            => _noConnection?.Invoke(command, commandAndKey);

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
            => Dispatch(in request, database, cancellationToken, profile: true);

        /// <summary>Send, optionally keeping the command out of any profiling session.</summary>
        /// <param name="request">The rendered request.</param>
        /// <param name="database">The database the command belongs to.</param>
        /// <param name="cancellationToken">Cancels the request before it is sent.</param>
        /// <param name="profile">
        /// Whether this command belongs in a profiling session. False for a PREAMBLE, which is the client's
        /// own machinery rather than a command the caller issued: the shipped core does not report a
        /// <c>SCRIPT LOAD</c> it inserted either, and a session that listed one would be reporting work
        /// nobody asked for, in a sequence the caller cannot reproduce.
        /// </param>
        private RespPayloadOperation Dispatch(
            in RespRequest request, int database, CancellationToken cancellationToken, bool profile)
        {
            var operation = RespPayloadOperation.Rent();
            operation.Attach(request.Span, request.Flags, cancellationToken);
            operation.Database = database;
            operation.Command = request.Command;
            operation.Observer = this;
            operation.Server = Server;

            // started HERE, where the endpoint is finally known: a profiled command reports which server
            // answered it, and until routing has resolved there is no honest answer to that.
            //
            // THE COMMAND'S database, not this executor's. One connection is shared by several databases
            // through per-database views, and the view's commands were being reported against the
            // connection's database instead of their own - so profiling a dedicated-database workload named
            // database 0 for every command in it.
            //
            // And -1 for a command that names no database at all: PING, ECHO, the CLIENT family. The
            // shipped core reports those as db-free, from this same predicate, and a profile that claimed
            // they ran "in database 0" would be inventing a fact about them.
            if (profile && _startProfile is { } start)
            {
                start(
                    operation,
                    request.Command,
                    request.Flags,
                    Message.RequiresDatabase(request.Command) ? database : -1,
                    _endpoint);
            }

            RespConnection? connection;
            lock (_sync)
            {
                if (_disposed)
                {
                    operation.EnsureFaulted(request.Flags, NoConnection(request.Command, operation.CommandAndKey));
                    return operation;
                }

                connection = _connection;
                if (connection is null || connection.IsClosed || _writeSlotHeld)
                {
                    // a claim is deliberately handled by the SAME branch as no-connection: both mean
                    // "not writable right now", and both are answered by the backlog, in arrival order
                    if (!_queueWhileDisconnected && !_writeSlotHeld && !NeverConnected)
                    {
                        operation.EnsureFaulted(request.Flags, NoConnection(request.Command, operation.CommandAndKey));
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
            RecordFault(connection);
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

            operation.EnsureFaulted(flags, NoConnection(operation.Command, operation.CommandAndKey));
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
                operation.EnsureFaulted(CommandFlags.None, NoConnection(operation.Command, operation.CommandAndKey));
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

            // A SERVER THAT HAS ONLY ONE DATABASE CANNOT BE ASKED FOR ANOTHER, and this said nothing: the
            // SELECT was injected regardless, so a command addressed to database 1 on a cluster - or
            // through a proxy whose command map has no SELECT - went out as though it had worked. The
            // shipped core refuses at exactly this point, when it decides whether a SELECT is needed, and
            // says which database it could not switch to.
            if (operation.Database != 0
                && connection is IRespPreambleTarget { Server: { SupportsDatabases: false } })
            {
                Fail(
                    operation,
                    new RedisConnectionException(
                        ConnectionFailureType.ProtocolFailure,
                        operation.Flags,
                        "The command could not be written.",
                        new RedisCommandException(
                            "Multiple databases are not supported on this server; cannot switch to database: "
                            + operation.Database.ToString(CultureInfo.InvariantCulture)),
                        CommandStatus.WaitingToBeSent));
                return true; // handled: completed, rather than refused back to the caller
            }

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
        private void EnsureConnecting() => EnsureConnecting(force: false);

        /// <summary>Start connecting, optionally ignoring the backoff.</summary>
        /// <param name="force">
        /// True when the connection was dropped ON PURPOSE - a <c>MOVED</c> to ourselves, whose entire
        /// remedy is "reconnect and try again". The backoff exists to space out attempts against a server
        /// that is refusing us, and nothing here is refusing: waiting out that interval just means the
        /// re-sent command sits in the backlog until it times out, which is what it did.
        /// </param>
        private void EnsureConnecting(bool force)
        {
            if (_connecting is not null) return;
            if (!force && !DueForConnectRetry())
            {
                // THE BACKOFF SAYING "not yet" USED TO MEAN "not ever". Nothing else came back: this core
                // dials on demand, so if no command happened to arrive after the delay elapsed, the
                // endpoint simply stayed down - and anything already in the backlog waited for the
                // operation backstop to time it out, two minutes later, rather than for a reconnect.
                //
                // The shipped core does not have this problem because its bridge heartbeat retries on a
                // timer whether or not anybody asks. This is that timer: a poll that defers to the policy
                // rather than a second opinion about when to retry.
                ArmConnectRetry();
                return;
            }

            _connecting = Task.Run(ConnectAsync);
        }

        /// <summary>Come back later and ask the policy again, since nobody else will.</summary>
        /// <remarks>
        /// Single-flight, and it stops of its own accord: a successful connect ends the loop, and so does
        /// disposal. While a server stays down it keeps asking, which is what the shipped bridge does too.
        /// </remarks>
        private void ArmConnectRetry()
        {
            if (_disposed || Interlocked.Exchange(ref _connectRetryArmed, 1) != 0) return;
            _ = RetryWhenDueAsync();
        }

        private async Task RetryWhenDueAsync()
        {
            try
            {
                await Task.Delay(ConnectRetryPollMilliseconds).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _connectRetryArmed, 0);
            }

            ExpireBacklog(); // whoever has waited long enough stops waiting, connected or not

            lock (_sync)
            {
                // somebody else may have connected, or this may be over entirely
                if (_disposed || _connection is { IsClosed: false }) return;
                EnsureConnecting(); // re-asks the policy, and re-arms if it is still too early
            }
        }

        private int _connectRetryArmed;

        /// <summary>How often to re-ask the retry policy while an endpoint is down.</summary>
        /// <remarks>
        /// Not itself a retry interval - the policy decides that. This only bounds how long after the
        /// policy says "now" the attempt actually happens.
        /// </remarks>
        private const int ConnectRetryPollMilliseconds = 250;

        /// <summary>Whether the configured backoff permits another attempt now.</summary>
        /// <remarks>
        /// <para>
        /// <b>The first attempt is never asked about</b>, matching the shipped core: a policy describes how
        /// to back off from a FAILURE, and there has not been one yet. So the count passed is zero for the
        /// first retry, one for the second, and it resets on a successful connect - which is the sequence
        /// <c>ReconnectRetryPolicyUnitTests</c> asserts, and which the new core did not produce at all
        /// because it reconnected immediately and unconditionally however often it was asked.
        /// </para>
        /// <para>
        /// Asked per attempt rather than from a heartbeat, because this core has no heartbeat: attempts are
        /// driven by demand, so the question is asked where the demand arrives.
        /// </para>
        /// </remarks>
        /// <summary>
        /// Whether a connection has never been ATTEMPTED here - not merely never achieved.
        /// </summary>
        /// <remarks>
        /// <b>FailFast is about not queueing behind a connection that is DOWN.</b> It was written for a
        /// core that dials every endpoint during connect, where by the time a command is issued there is
        /// either a connection or a known failure - so "no connection" could only mean the second. This
        /// core dials on demand, and "nothing has needed this endpoint yet" is neither: refusing it made
        /// the FIRST command on a FailFast multiplexer fail against a perfectly healthy server.
        /// <para>
        /// Backlogging instead does not weaken the policy, because the connect settles it either way: a
        /// server that cannot be reached fails the attempt, and <c>ConnectAsync</c> drains the backlog with
        /// that failure - which is fail-fast, arrived at honestly.
        /// </para>
        /// <para>
        /// <b>Attempted, not achieved</b>, and the difference is the whole exemption: counting only
        /// successes would leave this true for ever against a server that is down, so FailFast would never
        /// engage at all and every command would dial again. One attempt is the grace; after it, a
        /// disconnected send is refused as the policy says.
        /// </para>
        /// </remarks>
        private bool NeverConnected
            => Volatile.Read(ref _connects) == 0 && Volatile.Read(ref _connectRetryCount) == 0;

        private bool DueForConnectRetry()
        {
            if (Volatile.Read(ref _connectRetryCount) <= 0 && _connection is null && _connects == 0)
            {
                return true; // never connected and never failed: this is the first attempt
            }

            var policy = _retryPolicy?.Invoke();
            if (policy is null) return true;

            var elapsed = unchecked(Environment.TickCount - Volatile.Read(ref _lastConnectTicks));
            return policy.ShouldRetry(Volatile.Read(ref _connectRetryCount), elapsed);
        }

        /// <summary>Connect, giving up after <see cref="_connectTimeoutMilliseconds"/>.</summary>
        /// <remarks>
        /// <b>Raced rather than merely cancelled.</b> Cancelling only helps for a connect that watches the
        /// token, and the interesting stalls are the ones that do not - a tunnel hook, a handshake waiting
        /// on a server that has stopped answering. So the token is cancelled <i>and</i> the wait is
        /// abandoned, because a caller that is still waiting after the deadline has not been helped by
        /// asking nicely.
        /// <para>
        /// An abandoned attempt is still observed: if it eventually produces a connection nobody is waiting
        /// for, that connection is disposed rather than left holding a socket, and its fault is swallowed
        /// because the caller has already been told about the timeout.
        /// </para>
        /// </remarks>
        private async Task<RespConnection> ConnectWithinTimeoutAsync()
        {
            var timeoutMilliseconds = _connectTimeoutMilliseconds;
            if (timeoutMilliseconds <= 0) return await _connect(CancellationToken.None).ConfigureAwait(false);

            var cancel = new CancellationTokenSource();
            var pending = _connect(cancel.Token);

            if (pending.IsCompleted)
            {
                cancel.Dispose();
                return await pending.ConfigureAwait(false);
            }

            using (var delay = new CancellationTokenSource())
            {
                if (await Task.WhenAny(pending, Task.Delay(timeoutMilliseconds, delay.Token)).ConfigureAwait(false) == pending)
                {
                    delay.Cancel(); // stop the timer rather than leave it to fire into nothing
                    cancel.Dispose();
                    return await pending.ConfigureAwait(false);
                }
            }

            cancel.Cancel();
            Abandon(pending, cancel);
            throw new TimeoutException(
                $"The connection attempt to {_endpoint?.ToString() ?? "the endpoint"} did not complete within {timeoutMilliseconds}ms.");

            static void Abandon(Task<RespConnection> pending, CancellationTokenSource cancel)
                => _ = pending.ContinueWith(
                    static (task, state) =>
                    {
                        ((CancellationTokenSource)state!).Dispose();
                        if (task.IsCompletedSuccessfully) _ = task.Result.DisposeAsync();
                        else _ = task.Exception; // observed, so it is not an unobserved-exception event
                    },
                    cancel,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }

        private async Task<RespConnection> ConnectAsync()
        {
            try
            {
                Volatile.Write(ref _lastConnectTicks, Environment.TickCount);
                var connection = await ConnectWithinTimeoutAsync().ConfigureAwait(false);
                Interlocked.Increment(ref _connects);
                Volatile.Write(ref _connectRetryCount, 0); // a success starts the backoff over

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
                Interlocked.Increment(ref _connectRetryCount);
                var fault = AsConnectionFault(ex);
                Volatile.Write(ref _lastConnectFault, fault);

                Queue<RespPayloadOperation>? stranded;
                lock (_sync)
                {
                    _connecting = null;
                    stranded = _backlog;
                    _backlog = null;
                }

                // WHETHER a failed connect kills what was waiting for it is the caller's policy, and this
                // used to decide unconditionally that it did. That is BacklogPolicy.FailFast's behaviour
                // applied to everybody: under the default policy a command is supposed to wait, up to its
                // own timeout, for a connection to become available - the queue exists precisely so that a
                // transient failure does not become the caller's problem, and one failed dial is the most
                // transient thing there is. Killing the queue on it left the backlog unable to do the one
                // job it has.
                //
                // Bounded either way: what is NOT aborted here is still bounded by each operation's own
                // timeout, which is what makes "queue until it is answerable" safe rather than unlimited.
                if (stranded is not null)
                {
                    var abandon = _abortPendingOnConnectionFailure?.Invoke() ?? true;
                    if (abandon)
                    {
                        // each told in terms of ITS OWN command, since that is what the caller asked for
                        // and what the shipped diagnosis names; they are only in one queue by accident of
                        // timing
                        while (stranded.Count != 0)
                        {
                            var operation = stranded.Dequeue();
                            Fail(operation, NoConnection(operation.Command) ?? fault);
                        }
                    }
                    else
                    {
                        PutBack(stranded);
                    }
                }

                // A FAILED attempt has to lead to another one, which a DECLINED attempt already did. Only
                // the decline armed the timer, so after a genuine failure nothing came back: whatever was
                // put back sat there with nothing to retry it and nothing to expire it, which is the
                // two-minute backstop again by a different road.
                if (!_disposed) ArmConnectRetry();

                throw fault;
            }
        }

        /// <summary>Present a failed dial as a connection failure, whatever it arrived as.</summary>
        /// <param name="ex">What the connect attempt threw.</param>
        /// <remarks>
        /// <b>A caller should not have to know how we open sockets.</b> A dial that fails throws whatever
        /// the platform throws - a <c>SocketException</c>, an <c>AuthenticationException</c> from the TLS
        /// handshake, an <c>IOException</c> - and those were reaching callers unwrapped, so code catching
        /// <see cref="RedisConnectionException"/> (which is every caller that has ever handled this) caught
        /// nothing and code catching <see cref="RedisException"/> caught nothing either. The shipped core
        /// has always presented this as a connection failure with the platform error as the inner
        /// exception, and that is the contract being kept here.
        /// <para>
        /// A <see cref="RedisException"/> passes through untouched: it is already the vocabulary, and
        /// wrapping it would bury a perfectly good diagnosis one level deeper.
        /// </para>
        /// </remarks>
        private Exception AsConnectionFault(Exception ex)
            => ex is RedisException or ObjectDisposedException
                ? ex
                : new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    CommandFlags.CommandRetryAlways,
                    $"It was not possible to connect to the redis server(s): {Format.ToString(_endpoint)}. {ex.Message}",
                    ex,
                    CommandStatus.WaitingToBeSent);

        /// <summary>Return commands to the front of the backlog, having not sent them.</summary>
        /// <param name="waiting">What was taken off the queue, in arrival order.</param>
        /// <remarks>
        /// <b>At the FRONT</b>, which is the same rule <c>PrepareRunAsync</c> follows when a run it was
        /// holding never gets written: these were queued before anything that has arrived since, and
        /// putting them behind it would reorder the caller's commands to no purpose.
        /// </remarks>
        private void PutBack(Queue<RespPayloadOperation> waiting)
        {
            if (waiting.Count == 0) return;

            lock (_sync)
            {
                if (_backlog is { Count: > 0 })
                {
                    foreach (var later in _backlog) waiting.Enqueue(later);
                }

                _backlog = waiting;
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
