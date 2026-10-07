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
    /// One database's view of a <see cref="RespEndpointExecutor"/>, so several databases can share a
    /// single connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A view rather than another executor with its own socket.</b> The endpoint executor owns the
    /// connection, the backlog and the reconnect; this adds a database index and nothing else. The
    /// <c>SELECT</c> that makes the index true is written by the connection, immediately in front of the
    /// command and inside the write lock - see <c>RespConnection</c>'s preamble overload.
    /// </para>
    /// <para>
    /// <b>Why a view at all, rather than a per-database context.</b> <see cref="RespContext.Database"/> is
    /// resolved from its executor whenever it has one - deliberately, because the executor is what routes -
    /// so a context cannot ask an executor for a database the executor is not on. The database has to be an
    /// executor-level fact, and this is the cheapest thing that can carry one.
    /// </para>
    /// <para>
    /// Everything that is about the <i>connection</i> forwards to the inner executor, and deliberately so:
    /// two views of one socket must agree about whether it is connected, what its server can do, and who
    /// holds the write slot, because there is only one of each.
    /// </para>
    /// </remarks>
    internal sealed class RespDatabaseExecutor : RespExecutorBase
    {
        private readonly RespEndpointExecutor _inner;

        internal RespDatabaseExecutor(RespEndpointExecutor inner, int database)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Database = database;
        }

        /// <inheritdoc/>
        public override int Database { get; }

        /// <inheritdoc/>
        public override bool CanCancel => _inner.CanCancel;

        /// <inheritdoc/>
        internal override ConnectionMultiplexer? Multiplexer => _inner.Multiplexer;

        /// <inheritdoc/>
        /// <remarks>Re-pointed by asking the connection's owner for another view, not by stacking one.</remarks>
        internal override RespExecutorBase WithDatabase(int database)
            => database == Database ? this : _inner.WithDatabase(database);

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Forwarded, like the other capabilities.</b> Not forwarding it meant a view over a batch or a
        /// transaction answered "no, I send each command as it arrives" while queueing every one of them -
        /// and the answer is consulted for correctness, not tuning: <c>Scripts</c> asks it to decide
        /// whether to inline a script body, so over a wrapper it would pair SCRIPT LOAD with EVALSHA
        /// inside MULTI/EXEC and shift every result in the EXEC array.
        /// </remarks>
        internal override bool Accumulates => _inner.Accumulates;

        /// <inheritdoc/>
        internal override bool Transactional => _inner.Transactional;

        /// <inheritdoc/>
        internal override bool EnforcesTimeouts => _inner.EnforcesTimeouts;

        /// <inheritdoc/>
        internal override RespExecutorBase? ResolveForSlot(int slot, RedisCommand command, CommandFlags flags)
            => _inner.ResolveForSlot(slot, command, flags);

        /// <inheritdoc/>
        internal override bool IsReachable(in RedisKey key, CommandFlags flags) => _inner.IsReachable(in key, flags);

        /// <inheritdoc/>
        internal override RespConnectionState ConnectionStateNow(in RedisKey key, CommandFlags flags)
            => _inner.ConnectionStateNow(in key, flags);

        /// <inheritdoc/>
        internal override bool TryGetLocalFeatures(out RedisFeatures features) => _inner.TryGetLocalFeatures(out features);

        /// <inheritdoc/>
        internal override Func<CancellationToken>? GetFailoverSource() => _inner.GetFailoverSource();

        /// <inheritdoc/>
        public override ValueTask<EndPoint?> IdentifyEndpointAsync(
            RedisKey key, CommandFlags flags, CancellationToken cancellationToken = default)
            => _inner.IdentifyEndpointAsync(key, flags, cancellationToken);

        /// <inheritdoc/>
        /// <remarks>
        /// Forwarded: the preamble belongs to the connection this view sits over, and a <c>SCRIPT LOAD</c>
        /// names no database, so there is nothing of this view's own to apply to it.
        /// </remarks>
        internal override ValueTask SendPreambleAsync(
            RespRequest preamble, IRespPreambleGate? gate, CancellationToken cancellationToken = default)
            => _inner.SendPreambleAsync(preamble, gate, cancellationToken);

        /// <inheritdoc/>
        /// <remarks>Named with THIS view's database, as the untyped send is.</remarks>
        internal override ValueTask<TResult> SendTypedAsync<TResult>(
            RespRequest request, IRespHandler<TResult> handler, CancellationToken cancellationToken)
            => _inner.SendTypedAsync(request, handler, Database, cancellationToken);

        /// <inheritdoc/>
        internal override bool CopiesRequestOnSend => true; // a view over an endpoint executor, which does

        /// <inheritdoc/>
        internal override ValueTask SendVoidAsync(RespRequest request, IRespHandler<bool> handler, CancellationToken cancellationToken)
            => _inner.SendVoidAsync(request, handler, Database, cancellationToken);

        /// <inheritdoc/>
        /// <remarks>Named with THIS view's database, which is the only thing this type adds.</remarks>
        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            var operation = _inner.Dispatch(in request, Database, cancellationToken);

            // Fire-and-forget declines the outcome here for the same reason it does on the executor this is
            // a view over - and it has to, or the behaviour depends on which database the caller happened to
            // be on: `GetDatabase(3)` would throw at a caller who said they were not looking, where
            // `GetDatabase(0)` does not.
            if ((request.Flags & CommandFlags.FireAndForget) != 0)
            {
                RespPayloadOperation.DiscardReply(operation);
                _inner.OnFireAndForget();
                return default;
            }

            return new ValueTask<RespPayload>(operation, operation.Token);
        }

        /// <inheritdoc/>
        public override RespPayload Send(in RespRequest request)
        {
            var operation = _inner.Dispatch(in request, Database, default);
            return operation.Wait(operation.Token, TimeSpan.Zero);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Yes, and the database is no obstacle.</b> A run is composed through one context and so
        /// belongs to one database, and it is written with nothing of anybody else's in between - so a
        /// single <c>SELECT</c> at the front governs all of it. An earlier version declined whenever the
        /// view's database differed from the connection's, which was needlessly strict: what matters is
        /// that the operations agree with each other, not that they agree with the socket's last SELECT.
        /// </remarks>
        internal override bool CanWriteRuns => _inner.CanWriteRuns;

        /// <inheritdoc/>
        internal override bool CanWriteTransactions => _inner.CanWriteTransactions;

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="CanWriteRuns" path="/remarks"/></remarks>
        internal override bool TrySendBatch(List<RespPayloadOperation> operations)
            => _inner.TrySendBatch(operations, Database);

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="CanWriteRuns" path="/remarks"/></remarks>
        internal override bool TrySendTransaction(List<RespPayloadOperation> operations, out ValueTask<bool> exec)
            => _inner.TrySendTransaction(operations, Database, out exec);

        /// <inheritdoc/>
        /// <remarks>Named with THIS view's database, so a transaction's run selects before its MULTI.</remarks>
        internal override bool TryWriteRun(RespConnection connection, IRespMessage[] run, int count)
            => _inner.TryWriteRun(connection, run, count, Database);

        /// <inheritdoc/>
        internal override RespConnection? CurrentConnection => _inner.CurrentConnection;

        /// <inheritdoc/>
        internal override bool TryResend(RespPayloadOperation operation) => _inner.TryResend(operation);

        /// <inheritdoc/>
        internal override bool TryResendAsking(RespPayloadOperation operation) => _inner.TryResendAsking(operation);

        /// <inheritdoc/>
        internal override ValueTask<bool> PrepareRunAsync(CancellationToken cancellationToken = default)
            => _inner.PrepareRunAsync(cancellationToken);

        /// <inheritdoc/>
        internal override void ReleaseWrites() => _inner.ReleaseWrites();

        /// <inheritdoc/>
        public override string ToString() => $"{_inner} (db {Database})";
    }
}
