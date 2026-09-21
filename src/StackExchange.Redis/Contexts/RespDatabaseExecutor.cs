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
        internal override bool EnforcesTimeouts => _inner.EnforcesTimeouts;

        /// <inheritdoc/>
        internal override bool IsReachable(in RedisKey key, CommandFlags flags) => _inner.IsReachable(in key, flags);

        /// <inheritdoc/>
        internal override bool TryGetLocalFeatures(out RedisFeatures features) => _inner.TryGetLocalFeatures(out features);

        /// <inheritdoc/>
        internal override Func<CancellationToken>? GetFailoverSource() => _inner.GetFailoverSource();

        /// <inheritdoc/>
        public override ValueTask<EndPoint?> IdentifyEndpointAsync(
            RedisKey key, CommandFlags flags, CancellationToken cancellationToken = default)
            => _inner.IdentifyEndpointAsync(key, flags, cancellationToken);

        /// <inheritdoc/>
        /// <remarks>Named with THIS view's database, which is the only thing this type adds.</remarks>
        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            var operation = _inner.Dispatch(in request, Database, cancellationToken);
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
        /// <b>Deliberately not forwarded.</b> A run is written as one contiguous block, and every command
        /// in it would need to be preceded by this view's <c>SELECT</c> - which the run form cannot express
        /// today. Declining sends a batch or transaction down the path that handles an executor without the
        /// capability, rather than writing one against the wrong database.
        /// </remarks>
        internal override bool CanWriteRuns => Database == _inner.Database && _inner.CanWriteRuns;

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="CanWriteRuns" path="/remarks"/></remarks>
        internal override bool TrySendBatch(List<RespPayloadOperation> operations)
            => Database == _inner.Database && _inner.TrySendBatch(operations);

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="CanWriteRuns" path="/remarks"/></remarks>
        internal override bool TrySendTransaction(List<RespPayloadOperation> operations, out ValueTask<bool> exec)
        {
            if (Database != _inner.Database)
            {
                exec = default;
                return false;
            }

            return _inner.TrySendTransaction(operations, out exec);
        }

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
