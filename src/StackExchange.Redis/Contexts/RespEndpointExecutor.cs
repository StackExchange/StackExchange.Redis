using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RESPite.Operations;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// One endpoint: owns its connection's whole life, not just its use.
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
        /// v3 core bounded the same step with <c>ConnectTimeout</c> (in <c>PhysicalConnection</c>); this
        /// had no bound at all, which is what left <c>ReconnectRetryPolicyUnitTests</c> hanging under the
        /// new core rather than failing its ping.
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
        /// <c>MULTI</c> and must not let anything be written into it. The v3 surface got the second by
        /// holding the connection's write lock across the pause, which is why its
        /// <c>TransactionMessage</c> paused an enumerator with <c>Monitor</c> handshakes on result boxes:
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
        /// Describes "no connection was available" the way the v3 core described it - which endpoints
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
        private ServerEndPoint? Server
        {
            get
            {
                if (_serverCache is { } cached) return cached;
                try
                {
                    return _serverCache = _server?.Invoke();
                }
                catch (ObjectDisposedException)
                {
                    // the multiplexer is closing and will not create a server now; nor should the paths that
                    // ask for one here (logging, fault notes) be what stops a socket being closed
                    return null;
                }
            }
        }

        /// <summary>Count a command whose caller declined the outcome, for the multiplexer's summary.</summary>
        internal void OnFireAndForget() => Server?.Multiplexer.OnFireAndForget();

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
                    var pending = live ? _connection!.UnfinishedPendingCount : 0;
                    var queued = live || _connecting is not null ? _backlog?.Count ?? 0 : 0;
                    return pending + queued;
                }
            }
        }

        private long _operationCount;
        private long _socketCount;

        /// <summary>Commands this executor has dispatched, for the life of the executor.</summary>
        /// <remarks>
        /// <b>Counted so that the client's own counters are not wrong about it.</b> The commands are on this
        /// core's socket, and while both cores existed the v3 bridge counted nothing, so
        /// <c>GetCounters().Interactive.OperationCount</c> did not move no matter what the caller did -
        /// which is a counter reporting that the client is idle while it is busy.
        /// </remarks>
        internal long OperationCount => Volatile.Read(ref _operationCount);

        /// <summary>Whether a caller is waiting on this executor right now.</summary>
        /// <remarks>
        /// <b>What a maintenance handoff drains before replacing the connection.</b> It is deliberately
        /// coarse - anything queued or awaiting a reply counts - because the question it answers is "would
        /// recycling now lose somebody's work?", and for that, over-counting costs a short wait while
        /// under-counting costs the command.
        /// </remarks>
        internal bool HasCallerWork()
        {
            RespConnection? connection;
            int backlog;
            lock (_sync)
            {
                connection = _connection is { IsClosed: false } live ? live : null;
                backlog = _backlog?.Count ?? 0;
            }

            return backlog > 0 || connection?.PendingCount > 0;
        }

        /// <summary>The address this executor's current connection reached, if it has one.</summary>
        /// <remarks><inheritdoc cref="RespClientConnection.RemoteAddress" path="/remarks"/></remarks>
        internal System.Net.IPAddress? RemoteAddress
        {
            get
            {
                lock (_sync)
                {
                    return _connection is RespClientConnection { IsClosed: false } live ? live.RemoteAddress : null;
                }
            }
        }

        /// <summary>What the server calls this executor's current connection, if it has one.</summary>
        /// <remarks><inheritdoc cref="RespClientConnection.ConnectionId" path="/remarks"/></remarks>
        internal long? ConnectionId
        {
            get
            {
                lock (_sync)
                {
                    return _connection is RespClientConnection { IsClosed: false } live ? live.ConnectionId : null;
                }
            }
        }

        /// <summary>Connections this executor has opened, including reconnects.</summary>
        internal long SocketCount => Volatile.Read(ref _socketCount);

        /// <summary>Fold this endpoint's counters into a snapshot the public surface reports.</summary>
        /// <param name="counters">The snapshot to add to.</param>
        /// <remarks>
        /// <b>Added rather than substituted</b>, as the backlog already was - a holdover from when the v3
        /// bridges' counts shared the snapshot and every one of these was a sum.
        /// </remarks>
        internal void AddCounters(ConnectionCounters counters)
        {
            RespConnection? connection;
            int backlog;
            lock (_sync)
            {
                connection = _connection is { IsClosed: false } live ? live : null;
                backlog = _backlog?.Count ?? 0;
            }

            counters.OperationCount += OperationCount;
            counters.SocketCount += SocketCount;
            counters.PendingUnsentItems += backlog;
            counters.SentItemsAwaitingResponse += connection?.PendingCount ?? 0;
        }

        /// <summary>Whether something drives this executor's <see cref="OnHeartbeat"/>.</summary>
        /// <remarks>
        /// <b>What makes <see cref="EnforcesTimeouts"/> an honest answer rather than a hopeful one.</b>
        /// The sweep is the whole of this core's timeout enforcement, and the sweep happens because the
        /// multiplexer heartbeat calls it - so an executor built directly over a transport, as the tests
        /// do, genuinely does not time anything out except at the two-minute backstop. Claiming otherwise
        /// would make a synchronous caller stand back from its own timer and wait out that backstop.
        /// </remarks>
        internal bool HeartbeatDriven { get; init; }

        /// <inheritdoc/>
        internal override bool EnforcesTimeouts => HeartbeatDriven;

        /// <inheritdoc/>
        internal override ConnectionMultiplexer? Multiplexer => Server?.Multiplexer;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>A view over this connection, which is the whole point of <see cref="RespDatabaseExecutor"/>.</b>
        /// One endpoint has one socket whatever database a command names, and the <c>SELECT</c> that makes
        /// the naming true is written in front of the command inside the write lock.
        /// </remarks>
        internal override RespExecutorBase WithDatabase(int database)
            => database == Database ? this : new RespDatabaseExecutor(this, database);

        /// <summary>Whether this executor owns the endpoint's subscription connection.</summary>
        /// <remarks>
        /// Only used to describe a fault: the tail on a timeout names one connection's counters, and the
        /// command has to say which of an endpoint's two it was on. This core routes subscriptions by giving
        /// them their own executor rather than by flagging the command, so the executor is what knows.
        /// </remarks>
        internal bool IsSubscriptionEndpoint { get; init; }

        /// <summary>What the connect log calls this endpoint: the v3 bridge's name, <c>host:port/Type</c>.</summary>
        private string LogName => Format.ToString(_endpoint) + "/" + (IsSubscriptionEndpoint ? ConnectionType.Subscription : ConnectionType.Interactive);

        /// <summary>
        /// Hands a subscriber-mode command to the connection it belongs on, when this is not it; null when
        /// there is nowhere to hand it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A write-time question, not a compose-time one, and that is the whole point.</b> Whether a
        /// subscription shares the ordinary connection depends on the NEGOTIATED protocol, and a command
        /// can be composed while RESP3 is expected and written after a reconnect has settled on RESP2 -
        /// at which point writing it here puts this connection into subscriber mode, and it refuses every
        /// ordinary command from then on. The v3 core asked the same question in the same place, inside
        /// the write lock: <c>PhysicalBridge.WriteMessageInsideLock</c> and
        /// <c>ServerEndPoint.TryRerouteToSubscriptionBridge</c>, both added for issue #3154.
        /// </para>
        /// <para>
        /// Only the interactive executor has one: a subscription connection is already where
        /// subscriptions belong.
        /// </para>
        /// </remarks>
        internal Func<RespPayloadOperation, bool>? RerouteSubscription { get; init; }

        /// <summary>Whether this command puts a connection into, or takes it out of, subscriber mode.</summary>
        /// <param name="command">The command.</param>
        /// <remarks>
        /// All six spellings: an <c>UNSUBSCRIBE</c> is as much a subscriber-mode command as a
        /// <c>SUBSCRIBE</c>, and one written on the wrong connection unsubscribes something somewhere
        /// else as well as mis-stating this connection's mode.
        /// </remarks>
        internal static bool IsSubscriptionCommand(RedisCommand command) => command switch
        {
            RedisCommand.SUBSCRIBE or RedisCommand.UNSUBSCRIBE => true,
            RedisCommand.PSUBSCRIBE or RedisCommand.PUNSUBSCRIBE => true,
            RedisCommand.SSUBSCRIBE or RedisCommand.SUNSUBSCRIBE => true,
            _ => false,
        };

        /// <summary>This endpoint's state, in the shape the client's diagnostics already speak.</summary>
        /// <remarks>
        /// <b>Reported through <see cref="BridgeStatus"/> rather than a new shape of its
        /// own.</b> Every surface that shows connection state - <c>GetCounters</c>, <c>GetStatus</c>, the
        /// tail on a timeout exception - reads that struct, and callers have been reading those fields for
        /// years. Answering the same questions in a different vocabulary would mean teaching every one of
        /// those surfaces about a second core, which is the opposite of the direction of travel.
        /// <para>
        /// The fields this core genuinely has no answer for keep the "not applicable" sentinels rather than
        /// a plausible zero: there is no socket-level byte count and no pipe, because there is no pipe.
        /// </para>
        /// </remarks>
        /// <summary>Append the commands awaiting a reply on this endpoint's connection - the storm log's body.</summary>
        /// <param name="sb">Where to write.</param>
        /// <returns>Whether there was anything to write.</returns>
        /// <remarks>
        /// Capped at 500 lines, as v3's was: a storm log is read by a person, and the 501st pending GET says
        /// nothing the first 500 did not.
        /// </remarks>
        internal bool AppendStormLog(StringBuilder sb)
        {
            RespConnection? connection;
            lock (_sync) connection = _connection;
            if (connection is not { IsClosed: false } || connection.PendingCount == 0) return false;

            sb.Append("Sent, awaiting response from server: ").Append(connection.PendingCount).AppendLine();
            var total = 0;
            foreach (var message in connection.PendingSnapshot)
            {
                if (++total > 500) break;
                if (message is IFaultSubject subject) sb.Append(subject.CommandAndKey);
                else sb.Append(message.GetType().Name);
                sb.AppendLine();
            }

            return true;
        }

        internal BridgeStatus GetStatus()
        {
            RespConnection? connection;
            int backlog;
            bool writing;
            lock (_sync)
            {
                connection = _connection;
                backlog = _backlog?.Count ?? 0;
                writing = _writeSlotHeld;
            }

            var live = connection is { IsClosed: false } ? connection : null;
            return new BridgeStatus
            {
                IsWriterActive = writing,
                BacklogMessagesPending = backlog,
                BacklogMessagesPendingCounter = backlog,
                BacklogStatus = backlog == 0 ? BacklogStatus.Inactive : BacklogStatus.Started,
                Connection = new ConnectionStatus
                {
                    MessagesSentAwaitingResponse = live?.PendingCount ?? 0,
                    BytesAvailableOnSocket = -1,
                    BytesInReadPipe = -1,
                    BytesInWritePipe = -1,
                    BytesLastResult = live?.BytesLastResult ?? 0,
                    BytesInBuffer = live?.BytesInBuffer ?? 0,
                    ReadStatus = ReadStatus.NA,
                    WriteStatus = WriteStatus.NA,
                },
            };
        }

        /// <summary>What connecting to this endpoint last failed with, or null if it has not.</summary>
        internal RedisConnectionException? LastConnectFault => Volatile.Read(ref _lastConnectFault) as RedisConnectionException;

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
            if ((request.Flags & CommandFlags.FireAndForget) != 0)
            {
                RespPayloadOperation.DiscardReply(operation);
                OnFireAndForget();
                return null!;
            }

            return operation.Wait(operation.Token, TimeSpan.Zero);
        }

        /// <inheritdoc/>
        internal override ValueTask<TResult> SendTypedAsync<TResult>(
            RespRequest request, IRespHandler<TResult> handler, CancellationToken cancellationToken)
            => SendTypedAsync(request, handler, Database, cancellationToken);

        /// <inheritdoc/>
        /// <remarks>Dispatch attaches the request to the operation, which copies or shares it before returning.</remarks>
        internal override bool CopiesRequestOnSend => true;

        /// <summary>Send for a database that may not be this executor's own, completing with the parsed reply.</summary>
        /// <typeparam name="TResult">What the handler makes of the reply.</typeparam>
        /// <param name="request">The rendered request; consumed by this call on every path.</param>
        /// <param name="handler">Parses the reply.</param>
        /// <param name="database">The database the command belongs to.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>The parsed reply.</returns>
        /// <remarks>
        /// The typed twin of <see cref="SendAsync(RespRequest, CancellationToken)"/>: the operation is the
        /// awaitable, so no async method sits between the caller and the reply (see
        /// <see cref="RespPayloadOperation{TResult}"/>). Two things are kept exactly as the async tail had them.
        /// A synchronous failure - validation, a disposed executor - is a FAULTED task, not a throw at the call
        /// site, because callers may hold the task before awaiting it; and the request is released as soon as
        /// the operation has its own reference, where the tail held it until the reply.
        /// </remarks>
        internal ValueTask<TResult> SendTypedAsync<TResult>(
            RespRequest request, IRespHandler<TResult> handler, int database, CancellationToken cancellationToken)
        {
            if (typeof(TResult) == typeof(RespPayload)) return RespExecutor.AwaitUncached(this, request, handler, cancellationToken);

            var operation = DispatchTyped(request, handler, database, cancellationToken, out var fault, out var forgotten);
            if (fault is not null) return new ValueTask<TResult>(Task.FromException<TResult>(fault));
            if (forgotten) return new ValueTask<TResult>(default(TResult)!);
            operation!.NoteDispatched(); // lets a Task bridge register on it directly; see TryTakeDispatched
            return new ValueTask<TResult>(operation, operation.Token);
        }

        /// <inheritdoc/>
        internal override ValueTask SendVoidAsync(RespRequest request, IRespHandler<bool> handler, CancellationToken cancellationToken)
            => SendVoidAsync(request, handler, Database, cancellationToken);

        /// <summary>The void twin of <see cref="SendTypedAsync{TResult}(RespRequest, IRespHandler{TResult}, int, CancellationToken)"/>.</summary>
        /// <param name="request">The rendered request; consumed by this call on every path.</param>
        /// <param name="handler">Checks the reply.</param>
        /// <param name="database">The database the command belongs to.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>Completes when the reply has been checked.</returns>
        internal ValueTask SendVoidAsync(RespRequest request, IRespHandler<bool> handler, int database, CancellationToken cancellationToken)
        {
            var operation = DispatchTyped(request, handler, database, cancellationToken, out var fault, out var forgotten);
            if (fault is not null) return new ValueTask(Task.FromException(fault));
            if (forgotten) return default;
            operation!.NoteDispatched();
            return new ValueTask(operation, operation.Token);
        }

        /// <summary>Rent a typed operation, attach and dispatch the request, and release the caller's reference.</summary>
        /// <remarks>
        /// A synchronous failure is reported through <paramref name="fault"/> - the callers turn it into a FAULTED
        /// task, not a throw, because callers may hold the task before awaiting it; and a fire-and-forget send is
        /// reported through <paramref name="forgotten"/>, its outcome declined and its reply discarded.
        /// </remarks>
        private RespPayloadOperation<TResult>? DispatchTyped<TResult>(
            RespRequest request, IRespHandler<TResult> handler, int database, CancellationToken cancellationToken, out Exception? fault, out bool forgotten)
        {
            RespPayloadOperation<TResult> operation;
            forgotten = false;
            try
            {
                operation = RespPayloadOperation<TResult>.Rent(handler, this);
                Dispatch(in request, database, cancellationToken, profile: true, operation);
            }
            catch (Exception ex)
            {
                request.Dispose();
                fault = ex;
                return null;
            }

            fault = null;
            var flags = request.Flags;
            request.Dispose(); // the operation copied or shares the bytes; this reference is done

            if ((flags & CommandFlags.FireAndForget) != 0)
            {
                RespPayloadOperation.DiscardReply(operation);
                OnFireAndForget();
                forgotten = true;
            }

            return operation;
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            var operation = Dispatch(in request, cancellationToken);

            // <inheritdoc/> of the reasoning in Send, which this is the async twin of: fire-and-forget
            // declines the outcome, including the bad ones. The reply is drained and discarded; nobody is
            // told. What took so long to land was not this, but its consequence - a caller that does not
            // wait for its own replies no longer establishes that the server has EXECUTED them, and three
            // separate things had been quietly relying on that. See design notes 9b-vi.
            if ((request.Flags & CommandFlags.FireAndForget) != 0)
            {
                RespPayloadOperation.DiscardReply(operation);
                OnFireAndForget();
                return default;
            }

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

            // a server with one database cannot be asked for another; the sequential path dispatches each half
            // through Send, which refuses that with the shipped message
            if (_select is not null && Database > 0 && target is { Server: { SupportsDatabases: false } })
            {
                return SequentialAsync(preamble, request, gate, cancellationToken);
            }

            var head = RespPayloadOperation.Rent();
            head.Attach(preamble.Span, preamble.Flags, default);
            head.Observer = this;
            head.Database = Database;

            var body = RespPayloadOperation.Rent();
            body.Attach(request.Span, request.Flags, cancellationToken);
            body.Slot = request.Slot;
            body.Command = request.Command;
            body.Observer = this;
            body.Database = Database;
            _startProfile?.Invoke(body, request.Command, request.Flags, Database, _endpoint);

            // The gate is asked INSIDE the connection's write lock, which is the whole point of this
            // overload: deciding first and writing second lets another sender see this one's claim and then
            // win the lock ahead of it, so its command goes out in front of the preamble it depended on.
            // Harmless for SCRIPT LOAD, silent corruption for SELECT - see RespConnection.Send.
            //
            // The head operation is created speculatively, before the answer is known, because renting one
            // inside the lock is work the lock should not be holding. If the gate declines, nothing was
            // written for it and it is simply discarded.
            // ...and the pair carries its own SELECT, decided under the same lock: the connection is shared by
            // every database that reaches this endpoint, so the one it is on is whatever the last writer chose.
            // Written without it, a default-database HIMPORT PREPARE/SET ran against a concurrent caller's
            // database - succeeding there, and leaving the key missing where it was asked for.
            bool wroteHead;
            bool sent;
            if (_select is not null && Database >= 0)
            {
                sent = connection.Send(
                    head,
                    body,
                    new PairState(new Decision(gate, target), new Selector(connection, _select, Database)),
                    static p => p.Selector.Preamble(),
                    static p => p.Decision.IsNeeded(),
                    out var selected,
                    out wroteHead);
                if (selected is RespPayloadOperation discard) RespPayloadOperation.DiscardReply(discard);
            }
            else
            {
                sent = connection.Send(head, body, new Decision(gate, target), static d => d.IsNeeded(), out wroteHead);
            }

            if (!sent)
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
        /// <summary>The gate and the selector together, for the pair overload that writes a <c>SELECT</c> too.</summary>
        private readonly struct PairState(Decision decision, Selector selector)
        {
            internal readonly Decision Decision = decision;
            internal readonly Selector Selector = selector;
        }

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
                    operation.IsSubscription = IsSubscriptionEndpoint;
                    Interlocked.Increment(ref _operationCount);
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
        internal override bool TryResend(RespPayloadOperation operation)
        {
            Reprofile(operation, isMoved: true);
            return Enqueue(operation);
        }

        /// <summary>Give a redirected command its own profiling record, linked to the one it came from.</summary>
        /// <param name="operation">The command being re-sent.</param>
        /// <param name="isMoved">Whether the redirect was a <c>MOVED</c> rather than an <c>ASK</c>.</param>
        /// <remarks>
        /// <b>A retransmission is a second command as far as a profile is concerned</b>, and that is what
        /// makes a profile useful here: it went to one server, was told to go elsewhere, and went - two
        /// timings, joined by <c>RetransmissionOf</c>, and each naming the endpoint it actually reached.
        /// Reusing one record collapses that into a single entry against whichever server happened to be
        /// last, which is the version of events least like what happened.
        /// <para>
        /// The old record is finished first, because it IS finished: the reply that ended it was the
        /// redirect. Its timings are complete and nothing more will be added to it. <c>ProfiledCommand</c>
        /// has carried <c>NewAttachedToSameContext</c> for exactly this since the v3 core started
        /// re-issuing, so the shape is borrowed rather than invented.
        /// </para>
        /// </remarks>
        private void Reprofile(RespPayloadOperation operation, bool isMoved)
        {
            if (operation.Profile is not { } previous || Server is not { } server) return;

            previous.SetCompleted();
            var profile = Profiling.ProfiledCommand.NewAttachedToSameContext(previous, server, isMoved);
            profile.SetOperation(
                operation.Command,
                operation.Flags,
                operation.Database,
                operation.Diagnostics.CreatedDateTime,
                operation.Diagnostics.CreatedTimestamp);
            profile.SetEnqueued(null);
            operation.Profile = profile;
        }

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

            Reprofile(operation, isMoved: false);

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
        /// what the v3 core got structurally, by building an accumulator per <c>PhysicalConnection</c>.
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
        /// exception is not work to put on the thread that just finished somebody's GET. The v3 core
        /// made the same split, in <c>PhysicalConnection.ObserveMessageResult</c>.
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
        internal bool DropConnection(bool reconnectImmediately = false, bool wasRequested = false)
            => DropConnection(expected: null, reconnectImmediately, wasRequested);

        /// <summary>A connection closed under us - by the server, the network, anything we did not ask for.</summary>
        /// <remarks>
        /// <para>
        /// <b>The same as dropping it, which is what had been missing.</b> A close we did not initiate failed the
        /// pending commands and nothing else: no log, no <c>ConnectionFailed</c>, no reconnect until some later send
        /// happened to find the corpse (<c>OnSendRefused</c>). Anything that watches for failures - a multi-group's
        /// failover among them - never heard about it.
        /// </para>
        /// <para>
        /// Only for the CURRENT connection. Our own drops dispose the connection after unpublishing it, so their close
        /// arrives here for a connection that is no longer current and is ignored - which is also what keeps a
        /// requested close from being announced as a failure.
        /// </para>
        /// </remarks>
        private void OnConnectionClosed(RespConnection connection)
        {
            if (Volatile.Read(ref _disposed)) return;
            DropConnection(expected: connection);
        }

        /// <param name="expected">Drop only this connection, if it is still the current one; null for whichever is.</param>
        /// <param name="reconnectImmediately">Dial now, rather than leaving it to the retry policy.</param>
        /// <param name="wasRequested">Whether the drop was asked for, in which case it is not announced as a failure.</param>
        private bool DropConnection(RespConnection? expected, bool reconnectImmediately = false, bool wasRequested = false)
        {
            RespConnection? doomed;
            lock (_sync)
            {
                doomed = _connection;
                if (expected is not null && !ReferenceEquals(doomed, expected)) return false; // already replaced
                _connection = null;
            }

            if (doomed is null) return false;
            RecordFault(doomed, wasRequested);
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
        /// <param name="wasRequested">
        /// Whether we asked for this close - a retirement, a maintenance recycle, a dispose - in which case
        /// it is recorded but not announced as a failure.
        /// </param>
        /// <remarks>
        /// A connection that was CLOSED rather than broken has no fault of its own to report - a retirement,
        /// a circuit-breaker trip, a simulated failure - so one is written down instead. "No connection
        /// became available" followed by nothing is a diagnosis that does not diagnose, and "the connection
        /// was closed" is at least true and at least distinguishes it from never having had one.
        /// </remarks>
        private void RecordFault(RespConnection? connection, bool wasRequested = false)
        {
            if (connection is null) return;

            var fault = connection.Fault ?? new RedisConnectionException(
                ConnectionFailureType.SocketClosed,
                CommandFlags.None,
                $"The connection to {Format.ToString(_endpoint)} was closed.",
                null,
                CommandStatus.Unknown);

            Volatile.Write(ref _lastConnectFault, fault);

            // ...in the connect log too, not only as an event. A connection going away is the single most
            // useful line in a support log, and this core was closing sockets without writing one.
            // under the v3 ids, so a filter written against the v3 bridge still sees this
            if (Server?.Multiplexer.Logger is { } logger)
            {
                if (wasRequested)
                {
                    logger.LogInformationConnectionFailureRequested(fault, fault.Message);
                }
                else
                {
                    logger.LogErrorConnectionIssue(fault, fault.Message);
                }
            }

            // ...and SAY so, which this core did not. ConnectionFailed is a documented public event and the
            // thing callers wire up to notice a deployment moving underneath them; the v3 bridge raised
            // it from RecordConnectionFailed whenever a socket died, while this core raised it only for a
            // tripped circuit breaker and a maintenance handoff. So on this core an ordinary dead
            // connection was silent - measured by MaintenanceRelaxationTests, which breaks a
            // connection during a relaxed window and waits for somebody to notice.
            //
            // reconfigure: false, because the caller decides. A connection going away is not by itself a
            // reason to re-read the topology - OnRepeatedConnectFailure is what judges that, with the
            // restraint that question needs - and asking for one per dropped socket is the stampede the
            // rate limit exists to prevent.
            //
            // ...and NOT when the close was asked for, which is the same distinction the v3 bridge
            // made with `wasRequested`. A retirement, a maintenance recycle or a dispose is not a failure,
            // and announcing one is actively misleading: MaintenanceNotificationTests watches a graceful
            // recycle and asserts that no failure is reported for it.
            if (!wasRequested && _endpoint is { } reportAt && Server is { } server && !server.Multiplexer.IsDisposed)
            {
                server.Multiplexer.OnConnectionFailed(
                    reportAt,
                    IsSubscriptionEndpoint ? ConnectionType.Subscription : ConnectionType.Interactive,
                    fault is RedisConnectionException rce ? rce.FailureType : ConnectionFailureType.SocketFailure,
                    fault,
                    reconfigure: false,
                    physicalName: null);
            }
        }

        private const int ProfileLogSamples = 10;
        private const double ProfileLogSeconds = (1000 /* ms */ * ProfileLogSamples) / 1000.0;
        private readonly long[] _profileLog = new long[ProfileLogSamples];
        private int _profileLogIndex;

        /// <summary>
        /// Append the circular op-count snapshot: the operation count at each of the last ten heartbeats, and the
        /// rate across them.
        /// </summary>
        /// <param name="sb">Where to write.</param>
        /// <remarks>
        /// v3's format exactly (" base+delta+delta=total (rate ops/s; spans 10s)"), since it appears in logged
        /// endpoint summaries that people read side by side across versions. Like v3, the span assumes the
        /// default one-second heartbeat.
        /// </remarks>
        internal void AppendProfile(StringBuilder sb)
        {
            var clone = new long[ProfileLogSamples + 1];
            for (var i = 0; i < ProfileLogSamples; i++)
            {
                clone[i] = Volatile.Read(ref _profileLog[i]);
            }

            clone[ProfileLogSamples] = OperationCount;
            Array.Sort(clone);
            sb.Append(' ').Append(clone[0]);
            for (var i = 1; i < clone.Length; i++)
            {
                if (clone[i] != clone[i - 1]) sb.Append('+').Append(clone[i] - clone[i - 1]);
            }

            if (clone[0] != clone[ProfileLogSamples]) sb.Append('=').Append(clone[ProfileLogSamples]);
            var rate = (clone[ProfileLogSamples] - clone[0]) / ProfileLogSeconds;
            sb.Append(" (").Append(rate.ToString("N2", CultureInfo.InvariantCulture)).Append(" ops/s; spans ").Append(ProfileLogSeconds).Append("s)");
        }

        /// <summary>Periodic upkeep: time out whatever has waited too long, queued or in flight.</summary>
        /// <param name="timeoutMilliseconds">The configured command timeout.</param>
        /// <remarks>
        /// <b>Driven by the multiplexer heartbeat, which is where the v3 core did this too.</b> A
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
            var index = (uint)Interlocked.Increment(ref _profileLogIndex);
            Volatile.Write(ref _profileLog[index % ProfileLogSamples], OperationCount);

            ExpireBacklog();

            if (timeoutMilliseconds <= 0) return;

            RespConnection? connection;
            lock (_sync) connection = _connection;
            if (connection is null) return;
            if (connection.IsClosed)
            {
                // the close notice should already have dropped it; this is the net under that, as the heartbeat
                // is for the timeouts - a closed connection left published is one nothing would ever retry
                OnConnectionClosed(connection);
                return;
            }

            timeoutMilliseconds = EffectiveTimeout(timeoutMilliseconds);
            var expired = timeoutMilliseconds > 0 ? connection.ExpirePending(TimeSpan.FromMilliseconds(timeoutMilliseconds)) : 0;

            if (IsDeadSocket(connection, expired, timeoutMilliseconds, out var silentMilliseconds))
            {
                // Event 90 and the recovery it reports, as v3's heartbeat had them: commands are timing out AND
                // nothing at all has arrived for four timeouts' worth of time. That is a half-open connection -
                // writes still "succeed" into the socket buffer, and on Linux TCP retransmission keeps the socket
                // open for ~15 minutes before anything fails. TCP keep-alive does not help: it only probes an IDLE
                // connection, and this one has requests outstanding. So the connection is declared dead here,
                // which fails it like any other loss and starts the reconnect.
                Server?.Multiplexer.Logger?.LogWarningDeadSocketDetected(silentMilliseconds / 1000, expired);
                DropConnection();
                return;
            }

            KeepAlive();
        }

        // what the dead-socket check last saw: which connection, how many bytes it had received, and when that
        // count last moved. Heartbeat-only state, so no lock: OnHeartbeat is not re-entered for one executor.
        private RespConnection? _readWatchConnection;
        private long _readWatchBytes;
        private int _readWatchTick;

        /// <summary>
        /// Whether this heartbeat timed commands out on a connection that has received nothing for four
        /// timeouts' worth of time - v3's rule, and its multiplier.
        /// </summary>
        /// <remarks>
        /// Measured on the EFFECTIVE timeout, as v3 did: a maintenance window that relaxes the timeout must also
        /// relax this, or it would tear down the very connection the window exists to keep. "Received" is any
        /// inbound byte, push or reply, so a subscription connection that is merely quiet never qualifies: it
        /// has to be timing commands out as well.
        /// </remarks>
        private bool IsDeadSocket(RespConnection connection, int expired, int timeoutMilliseconds, out long silentMilliseconds)
        {
            var now = Environment.TickCount;
            var received = connection.BytesReceived;
            if (!ReferenceEquals(connection, _readWatchConnection) || received != _readWatchBytes)
            {
                _readWatchConnection = connection;
                _readWatchBytes = received;
                _readWatchTick = now;
            }

            silentMilliseconds = unchecked(now - _readWatchTick);
            return expired > 0 && timeoutMilliseconds > 0 && silentMilliseconds > (long)timeoutMilliseconds * 4;
        }

        private int _lastWriteTickCount = Environment.TickCount;

        /// <summary>Say something on an idle connection, so it is not closed underneath us.</summary>
        /// <remarks>
        /// <para>
        /// <b>An idle connection is a connection being timed out.</b> Servers close one after their own
        /// <c>timeout</c> - the very setting this core reads during discovery - and the network between
        /// will do it sooner. The v3 bridge sent a keep-alive on this schedule for as long as it existed;
        /// this core sent nothing at all, so a connection carrying no traffic was simply
        /// waiting to be dropped. The tests that notice are the ones that measure the heartbeat doing
        /// something: <c>ConfigTests.TestManualHeartbeat</c> and
        /// <c>ConnectCustomConfigTests.HeartbeatConsistencyCheckPingsAsync</c>, both as an operation count
        /// that never moves.
        /// </para>
        /// <para>
        /// Two schedules, as the v3 bridge had: every heartbeat when consistency checks are on -
        /// their whole purpose is to notice a dropped stream promptly, so skipping one because the
        /// connection is busy would defeat them - and otherwise only once the connection has been quiet
        /// for <c>WriteEverySeconds</c>.
        /// </para>
        /// <para>
        /// Fire-and-forget on the pool, and never awaited: this runs on the heartbeat, and a heartbeat
        /// that waits for a reply from a server that has stopped answering is a heartbeat that stops
        /// beating for everything else.
        /// </para>
        /// </remarks>
        private void KeepAlive()
        {
            if (Server is not { } server) return;

            var always = server.Multiplexer.RawConfig.HeartbeatConsistencyChecks;
            if (!always)
            {
                var writeEverySeconds = server.WriteEverySeconds;
                if (writeEverySeconds <= 0) return;

                var idleMilliseconds = unchecked(Environment.TickCount - Volatile.Read(ref _lastWriteTickCount));
                if (idleMilliseconds < writeEverySeconds * 1000) return;
            }

            if (!server.Multiplexer.CommandMap.IsAvailable(RedisCommand.PING)) return;

            Volatile.Write(ref _lastWriteTickCount, Environment.TickCount);
            ThreadPool.QueueUserWorkItem(static state => _ = ((RespEndpointExecutor)state!).PingAsync(), this);
        }

        private async Task PingAsync()
        {
            try
            {
                var started = DateTime.UtcNow;
                await new RespDatabaseContext(
                        new RespContext(Server!.Multiplexer.RawConfig.CommandMap, database: -1).WithExecutor(this))
                    .PingAsync(CommandFlags.NoRedirect)
                    .ConfigureAwait(false);

                // ...and keeps the latency sample current, as the v3 heartbeat's tracer did
                Server.SetLatency(started);
            }
            catch (Exception ex)
            {
                // a keep-alive that fails has told us something, and the connection's own failure
                // handling is what acts on it; there is nobody here to report to
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
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

            // built rather than borrowed: v3's ExceptionFactory.Timeout needed a Message, which this core
            // does not have - so the wording is matched here, including the inner exception the v3 text
            // quoted, because that is what callers read and what tests assert
            var last = Volatile.Read(ref _lastConnectFault);
            var text = last is null
                ? $"The message timed out in the backlog attempting to send because no connection became available ({timeout}ms)"
                : $"The message timed out in the backlog attempting to send because no connection became available ({timeout}ms) - Last Connection Exception: {last.Message}";

            foreach (var operation in expired)
            {
                // the type follows the v3 rule, which is not cosmetic: a connection exception is
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

        /// <summary>Refuse, before sending, what the caller is not permitted to send HERE.</summary>
        /// <param name="request">The rendered request.</param>
        /// <param name="database">The database the command will run against, or -1 for none.</param>
        /// <remarks>
        /// <b>The checks the v3 pipeline made once a server had been named</b>, which is what this
        /// executor is: <c>ConnectionMultiplexer.ExecuteAsyncImpl</c> refused an admin command with
        /// <c>AllowAdmin</c> off, and a primary-only command aimed at a replica, in the branch where the
        /// caller chose the server. Both have to be answered by whoever holds that choice, and here that is
        /// this type.
        /// <para>
        /// Both refusals are about what would happen if the command DID go, so both belong before the send:
        /// "Command cannot be issued to a replica" is a statement the client can make on its own, and a
        /// server error saying READONLY instead is a worse answer arrived at more slowly. The admin gate is
        /// stronger still - the point of it is that the command is not sent.
        /// </para>
        /// <para>
        /// Costs a switch on the command, and the sub-command is recovered from the frame only for the one
        /// command whose answer depends on it. Nothing is paid at all while <c>AllowAdmin</c> is on and the
        /// endpoint is a primary, which is the overwhelmingly common case.
        /// </para>
        /// </remarks>
        private void Validate(in RespRequest request, int database)
        {
            // "no database" is a real answer for a server-scoped context, and a command that needs one has
            // to say so rather than quietly run against whatever the connection last selected. Message's
            // constructor made the same refusal for the v3 pipeline; a core without Message has to make
            // it somewhere, and this is where the database is finally known.
            if (database < 0
                && request.Command != RedisCommand.NONE
                && request.Command != RedisCommand.UNKNOWN
                && CommandFlagsInternal.RequiresDatabase(request.Command))
            {
                throw ExceptionFactory.DatabaseRequired(
                    Server?.Multiplexer.RawConfig.IncludeDetailInExceptions ?? false, request.Command);
            }

            if (Server is not { } server) return; // nobody models this endpoint; nothing to judge it against

            var config = server.Multiplexer.RawConfig;

            // A command the map has disabled is refused BEFORE anything else judges it, because "you have
            // turned this off" outranks every other reason it might not run. The v3 pipeline made this
            // refusal while rendering - MessageWriter threw when the mapped name was empty - and a
            // core that renders its own frames has to make it somewhere; found by porting SUBSCRIBE, which
            // is what ConfigTests.ConnectWithSubscribeDisabled asks about.
            if (request.Command is not (RedisCommand.NONE or RedisCommand.UNKNOWN)
                && !config.CommandMap.IsAvailable(request.Command))
            {
                throw ExceptionFactory.CommandDisabled(request.Command);
            }

            if (!config.AllowAdmin
                && AdminCommands.IsAdminCommand(
                    request.Command,
                    AdminCommands.TryGetSubCommand(in request, out var subCommand) ? subCommand : null))
            {
                throw ExceptionFactory.AdminModeNotEnabled(config.IncludeDetailInExceptions, request.Command, null, server);
            }

            if (server.IsReplica && !server.AllowReplicaWrites && request.Command.IsPrimaryOnly())
            {
                throw ExceptionFactory.PrimaryOnly(config.IncludeDetailInExceptions, request.Command, null, server);
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
        /// own machinery rather than a command the caller issued: the v3 core did not report a
        /// <c>SCRIPT LOAD</c> it inserted either, and a session that listed one would be reporting work
        /// nobody asked for, in a sequence the caller cannot reproduce.
        /// </param>
        /// <param name="rented">An operation already rented by the caller - a typed one - or null to rent here.</param>
        private RespPayloadOperation Dispatch(
            in RespRequest request, int database, CancellationToken cancellationToken, bool profile, RespPayloadOperation? rented = null)
        {
            Validate(in request, database);

            var operation = rented ?? RespPayloadOperation.Rent();
            operation.Attach(in request, cancellationToken);
            operation.Database = database;
            operation.Command = request.Command;
            operation.Observer = this;
            operation.Server = Server;
            operation.IsSubscription = IsSubscriptionEndpoint;
            Interlocked.Increment(ref _operationCount);

            // started HERE, where the endpoint is finally known: a profiled command reports which server
            // answered it, and until routing has resolved there is no honest answer to that.
            //
            // THE COMMAND'S database, not this executor's. One connection is shared by several databases
            // through per-database views, and the view's commands were being reported against the
            // connection's database instead of their own - so profiling a dedicated-database workload named
            // database 0 for every command in it.
            //
            // And -1 for a command that names no database at all: PING, ECHO, the CLIENT family. The
            // v3 core reported those as db-free, from this same predicate, and a profile that claimed
            // they ran "in database 0" would be inventing a fact about them.
            if (profile && _startProfile is { } start)
            {
                start(
                    operation,
                    request.Command,
                    request.Flags,
                    CommandFlagsInternal.RequiresDatabase(request.Command) ? database : -1,
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

                        if (!held)
                        {
                            if (_writeSlotHeld)
                            {
                                slotWait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                (_writeWaiters ??= new()).Enqueue(slotWait);

                                // CAPTURED WHEN WE ASK, not when we win, and this is the correction that
                                // makes waiting an ordering guarantee too. "Earlier" has to mean earlier
                                // than the moment this run was issued; anything that arrives while we wait
                                // was issued after it and must be written after it. Capturing on winning
                                // instead put every such command in front of the run - and, worse, the
                                // outgoing holder drained them itself, so they were written before this run
                                // even existed on the wire. BatchTests.TestBatchSent found that as a
                                // WRONGTYPE: a SMEMBERS issued after a batch ran before the batch's DEL.
                                if (!captured)
                                {
                                    earlier = _backlog;
                                    _backlog = null;
                                    captured = true;
                                }
                            }
                            else
                            {
                                _writeSlotHeld = true;
                                held = true;
                                if (!captured)
                                {
                                    earlier = _backlog;
                                    _backlog = null;
                                    captured = true;
                                }
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
                // never written, so they go back at the FRONT: they were queued before anything that has
                // arrived since, and losing that is losing the ordering this method exists for. Restored
                // whether or not the slot was ever held, because it is claimed when we ASK - so a wait that
                // ends in failure is also a wait that is holding somebody else's commands.
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

                if (held) ReleaseWrites();
            }
        }

        /// <inheritdoc/>
        internal override void ReleaseWrites()
        {
            while (true)
            {
                lock (_sync)
                {
                    // HANDED ON BEFORE DRAINING when somebody is waiting, which is the other half of
                    // capturing "earlier" at ask time. A waiter already owns everything that was queued
                    // when it asked; what is in the backlog now arrived after that, so draining it here
                    // would write it in front of a run that was issued before it. The waiter drains it
                    // itself, after its own run, when it releases.
                    if (_writeWaiters is { Count: > 0 })
                    {
                        _writeWaiters.Dequeue().TrySetResult(true);
                        return;
                    }
                }

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
            // when this connection last had anything to say, for the keep-alive below. A tick count
            // rather than a timestamp: it is only ever compared against itself.
            Volatile.Write(ref _lastWriteTickCount, Environment.TickCount);

            // Asked HERE, at the write, because the answer can have changed since this was composed: see
            // RerouteSubscription. A subscribe queued while RESP3 was expected and written after the
            // handshake settled on RESP2 would otherwise put this connection into subscriber mode.
            if (!IsSubscriptionEndpoint
                && IsSubscriptionCommand(operation.Command)
                && RerouteSubscription is { } reroute
                && reroute(operation))
            {
                return true; // somebody else's responsibility now
            }

            if (_select is null || operation.Database < 0) return connection.Send(operation);

            // A SERVER THAT HAS ONLY ONE DATABASE CANNOT BE ASKED FOR ANOTHER, and this said nothing: the
            // SELECT was injected regardless, so a command addressed to database 1 on a cluster - or
            // through a proxy whose command map has no SELECT - went out as though it had worked. The
            // v3 core refused at exactly this point, when it decided whether a SELECT was needed, and
            // said which database it could not switch to.
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
                // The v3 core did not have this problem because its bridge heartbeat retried on a
                // timer whether or not anybody asked. This is that timer: a poll that defers to the policy
                // rather than a second opinion about when to retry.
                ArmConnectRetry();
                return;
            }

            if (Volatile.Read(ref _connectRetryCount) is var retries and > 0)
            {
                Server?.Multiplexer.Logger?.LogInformationResurrecting(
                    (IsSubscriptionEndpoint ? ConnectionType.Subscription : ConnectionType.Interactive) + "/" + Format.ToString(_endpoint),
                    retries);
            }

            _connecting = Task.Run(ConnectAsync);
        }

        /// <summary>Come back later and ask the policy again, since nobody else will.</summary>
        /// <remarks>
        /// Single-flight, and it stops of its own accord: a successful connect ends the loop, and so does
        /// disposal. While a server stays down it keeps asking, which is what the v3 bridge did too.
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
        /// <b>The first attempt is never asked about</b>, matching the v3 core: a policy describes how
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
            // Nothing has failed since the last success - the first connect, or the first reconnect after
            // a healthy connection was lost - so there is nothing to back off from. The v3 bridge
            // reconnected on demand here without asking the policy, and a policy that delays its first
            // answer (exponential backoff does) would otherwise hold up a reconnect that needed no retry.
            var failures = Volatile.Read(ref _connectRetryCount);
            if (failures <= 0) return true;

            var policy = _retryPolicy?.Invoke();
            if (policy is null) return true;

            // the policy is asked about retries already MADE, as the v3 bridge asked it: after the first
            // failure, none has been - so the count it sees starts at zero, not at the failure count
            var elapsed = unchecked(Environment.TickCount - Volatile.Read(ref _lastConnectTicks));
            return policy.ShouldRetry(failures - 1, elapsed);
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

                // ...and clears the fault, as the v3 bridge did on establishing: LastException answers
                // "why is this endpoint down", so a recovered endpoint must stop reporting the old reason
                Volatile.Write(ref _lastConnectFault, null);

                bool drain;
                lock (_sync)
                {
                    _connecting = null;
                    if (_disposed)
                    {
                        _ = connection.DisposeAsync();
                        throw new ObjectDisposedException(nameof(RespEndpointExecutor));
                    }

                    // WEAKLY, for the reason RespClientConnection.Server is weak: the connection is kept alive by its own
                    // pending read, so a strong delegate here held this executor - and through it the multiplexer -
                    // for as long as the socket lived (GarbageCollectionTests.MuxerIsCollected). Set before the
                    // connection is published; see the check below.
                    var self = new WeakReference<RespEndpointExecutor>(this);
                    connection.Closed = closed =>
                    {
                        if (self.TryGetTarget(out var executor)) executor.OnConnectionClosed(closed);
                    };
                    _connection = connection;
                    Interlocked.Increment(ref _socketCount);

                    // if somebody owns the write slot they are waiting on this very task, and THEY will
                    // drain - draining here as well would write the backlog out from under them
                    drain = !_writeSlotHeld;
                    if (drain) _writeSlotHeld = true;
                }

                // a close before Closed was set had nobody to tell; now that it is published, catch that here
                if (connection.IsClosed) OnConnectionClosed(connection);

                // event 70: v3 flushed what the handshake and the backlog had queued as its last handshake step, and
                // this is where the same thing happens here - the connection is published and what waited for it goes
                Server?.Multiplexer.Logger?.LogInformationFlushingOutboundBuffer(new(Server));
                if (drain) ReleaseWrites(); // drains in arrival order, then frees or hands on the slot

                // "Connected" is announced HERE, after the connection is published, and never from inside
                // the handshake. Announced earlier, it completed the waiters of ServerEndPoint.OnConnectedAsync
                // before IsConnected could say yes - so a waiter that registered in between missed the signal,
                // re-checked, still heard no, and waited out the whole ConnectTimeout. That is a cluster
                // connect taking exactly 20,015ms, seen at the start of a loaded net481 run, followed by
                // GetServer answering from whichever node HAD connected.
                if (!IsSubscriptionEndpoint && connection is RespClientConnection { Server: { } established })
                {
                    established.OnConnected($"{_endpoint} connected on the new core");
                }

                return connection;
            }
            catch (Exception ex)
            {
                var failures = Interlocked.Increment(ref _connectRetryCount);
                var fault = AsConnectionFault(ex);
                Volatile.Write(ref _lastConnectFault, fault);
                Server?.Multiplexer.Logger?.LogErrorConnectFailed(fault, LogName, fault.Message);

                // An endpoint that only ever refuses has nobody to tell the client it has moved: every
                // other path that re-reads the topology needs somebody ELSE to notice first - a
                // notification, a MOVED from a reachable node, a peer's broadcast. The v3 bridge closed
                // that gap from its own retry loop; this core had no equivalent, so a dead address was
                // dialled indefinitely. ServerEndPoint owns the restraint
                // (ConfigCheckSeconds, and only above a threshold), so this just reports the count.
                if (!IsSubscriptionEndpoint && Server is { } repeatedly)
                {
                    repeatedly.OnRepeatedConnectFailure((int)Math.Min(failures, int.MaxValue));
                }

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
                        // and what the v3 diagnosis named; they are only in one queue by accident of
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
        /// nothing and code catching <see cref="RedisException"/> caught nothing either. The v3 core
        /// always presented this as a connection failure with the platform error as the inner
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

            if (connection is not null)
            {
                // a requested close is still a line in the connect log: the v3 bridge wrote one as the
                // multiplexer is disposed, and its absence reads as a connection that simply vanished.
                //
                // The CACHED server only, never `Server`: resolving it here asks the multiplexer, which by
                // now has cleared its servers and throws ObjectDisposedException rather than create one -
                // and that throw skipped the dispose below. An executor that had never needed its server
                // (a RESP2 subscription socket, typically) therefore leaked its socket on every multiplexer
                // disposal, until the server's client limit refused new connections.
                if (!connection.IsClosed && Volatile.Read(ref _serverCache)?.Multiplexer.Logger is { } logger)
                {
                    var closing = new RedisConnectionException(ConnectionFailureType.ConnectionDisposed, CommandFlags.None, LogName + ": closed by the client");
                    logger.LogInformationConnectionFailureRequested(closing, closing.Message);
                }

                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
