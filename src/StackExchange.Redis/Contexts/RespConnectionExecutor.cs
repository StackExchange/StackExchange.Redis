using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using RESPite.Operations;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. An executor that talks to a <see cref="RespConnection"/> directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The first path with no <c>Message</c> in it.</b> Everything else on this surface still reaches
    /// the server through <c>RespMessageExecutor</c>, which wraps a rendered frame in a <c>Message</c> so
    /// the existing pipeline can carry it. This one hands the frame to an operation, and the operation IS
    /// the completion - no message, no result box, no <c>Task</c>.
    /// </para>
    /// <para>
    /// This is the <b>server-endpoint</b> executor of the three in design notes section 3b, in its
    /// simplest form: one connection, no routing decision to make. The multiplexer and group executors
    /// resolve a key to one of these; they do not re-implement it.
    /// </para>
    /// <para>
    /// Deliberately missing, and why it is still useful without them: handshake, <c>SELECT</c>, reconnect
    /// and the backlog. Those belong to whoever owns the connection's lifecycle, and leaving them out is
    /// what lets this be driven end to end by a transport that is two byte arrays.
    /// </para>
    /// </remarks>
    internal sealed class RespConnectionExecutor : RespExecutorBase
    {
        private readonly RespConnection _connection;

        internal RespConnectionExecutor(RespConnection connection, int database)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Database = database;
        }

        /// <inheritdoc/>
        public override int Database { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// The synchronous path is the same object, waited on rather than awaited - which is the whole
        /// point of the operation carrying its own completion. There is no second mechanism here, and no
        /// result box to allocate.
        /// </remarks>
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

        private RespPayloadOperation Dispatch(in RespRequest request, CancellationToken cancellationToken)
        {
            var operation = RespPayloadOperation.Rent();

            // the request's bytes are the caller's until this call completes, so the operation takes its
            // own copy rather than a reference: the connection may still be writing after we return, and
            // fire-and-forget returns before the write at all
            operation.Attach(request.Span, request.Flags, cancellationToken);

            if (!_connection.Send(operation))
            {
                // Send only refuses when the connection is closed or the operation was already
                // completed; either way it has been faulted, and awaiting it reports why
                operation.EnsureFaulted(request.Flags);
            }

            return operation;
        }
    }
}
