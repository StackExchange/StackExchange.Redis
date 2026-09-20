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
        private readonly EndPoint? _endpoint;
        private readonly bool _queueWhileDisconnected;
        private readonly object _sync = new();

        private RespConnection? _connection;
        private Task<RespConnection>? _connecting;
        private Queue<RespPayloadOperation>? _backlog;
        private bool _disposed;

        /// <summary>Create an executor that owns a connection to one endpoint.</summary>
        /// <param name="connect">Establishes and hands back a ready-to-use connection.</param>
        /// <param name="database">The database this executor sends to.</param>
        /// <param name="endpoint">The endpoint, for identity; may be null when unknown.</param>
        /// <param name="queueWhileDisconnected">
        /// Whether a command arriving with no connection waits for one, or fails immediately.
        /// </param>
        internal RespEndpointExecutor(
            Func<CancellationToken, Task<RespConnection>> connect,
            int database = 0,
            EndPoint? endpoint = null,
            bool queueWhileDisconnected = true)
        {
            _connect = connect ?? throw new ArgumentNullException(nameof(connect));
            Database = database;
            _endpoint = endpoint;
            _queueWhileDisconnected = queueWhileDisconnected;
        }

        /// <inheritdoc/>
        public override int Database { get; }

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
        public override bool IsConnected(in RedisKey key, CommandFlags flags) => IsConnectedNow;

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
                Discard(asking);
                return false;
            }

            Discard(asking);
            return true;
        }

        /// <summary>Consume a reply nobody is waiting for, so its operation and buffer come back.</summary>
        /// <param name="operation">The operation to drain.</param>
        /// <remarks>
        /// <c>ASKING</c> has an answer - <c>+OK</c> - and somebody has to take it. Left unconsumed the
        /// operation is never reset, so it never returns to the pool and never releases the pooled buffer
        /// holding its request: a slow leak of exactly the kind pooling was added to avoid.
        /// </remarks>
        private static void Discard(RespPayloadOperation operation)
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
                    // ASKING failing tells us nothing the redirected command will not tell us better
                }
            }
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
                if (connection is null || connection.IsClosed)
                {
                    if (!_queueWhileDisconnected) return false;

                    operation.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
                    (_backlog ??= new()).Enqueue(operation);
                    EnsureConnecting();
                    return true;
                }
            }

            return connection.Send(operation);
        }

        private RespPayloadOperation Dispatch(in RespRequest request, CancellationToken cancellationToken)
        {
            var operation = RespPayloadOperation.Rent();
            operation.Attach(request.Span, request.Flags, cancellationToken);

            RespConnection? connection;
            lock (_sync)
            {
                if (_disposed)
                {
                    operation.EnsureFaulted(request.Flags);
                    return operation;
                }

                connection = _connection;
                if (connection is null || connection.IsClosed)
                {
                    if (!_queueWhileDisconnected)
                    {
                        operation.EnsureFaulted(request.Flags);
                        return operation;
                    }

                    // WaitingInBacklog, not WaitingToBeSent, and it matters beyond the report: a command
                    // still in the backlog provably never reached a socket, which is what lets
                    // FaultContext.NotApplied bypass retry's side-effect cap if this ends badly
                    operation.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
                    (_backlog ??= new()).Enqueue(operation);
                    EnsureConnecting();
                    return operation;
                }
            }

            // OUTSIDE the lock: writing is the slow part, and holding a lock across it would serialise
            // every sender behind one - the exact thing the new core exists to avoid
            if (!connection.Send(operation)) OnSendRefused(connection, operation, request.Flags);
            return operation;
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
                    operation.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
                    (_backlog ??= new()).Enqueue(operation);
                    EnsureConnecting();
                    return;
                }
            }

            operation.EnsureFaulted(flags);
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

                Queue<RespPayloadOperation>? waiting;
                lock (_sync)
                {
                    _connecting = null;
                    if (_disposed)
                    {
                        _ = connection.DisposeAsync();
                        throw new ObjectDisposedException(nameof(RespEndpointExecutor));
                    }

                    _connection = connection;
                    waiting = _backlog;
                    _backlog = null;
                }

                // drained OUTSIDE the lock, in arrival order: these were queued before anything newer
                // could reach a connection, so replaying them first is what preserves ordering
                if (waiting is not null)
                {
                    while (waiting.Count != 0)
                    {
                        var operation = waiting.Dequeue();
                        if (!connection.Send(operation)) operation.EnsureFaulted(CommandFlags.None);
                    }
                }

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
