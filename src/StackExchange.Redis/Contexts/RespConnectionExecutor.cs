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
        /// </remarks>
        private readonly PayloadMessage?[] _pool = new PayloadMessage[PoolSize];

        private const int PoolSize = 64;

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

        private PayloadMessage Dispatch(in RespRequest request, CancellationToken cancellationToken)
        {
            var operation = Rent();

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

        private PayloadMessage Rent()
        {
            var pool = _pool;
            for (var i = 0; i < pool.Length; i++)
            {
                if (Interlocked.Exchange(ref pool[i], null) is { } reused) return reused;
            }

            return new PayloadMessage(this);
        }

        private void Return(PayloadMessage operation)
        {
            var pool = _pool;
            for (var i = 0; i < pool.Length; i++)
            {
                if (Interlocked.CompareExchange(ref pool[i], operation, null) is null) return;
            }

            // pool full; drop it, which is why this is lossy by design rather than by accident
        }

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
        private sealed class PayloadMessage(RespConnectionExecutor owner) : RespMessageBase<RespPayload>
        {
            private CommandFlags _flags;

            /// <inheritdoc/>
            /// <remarks>
            /// Called only after a definite outcome has been consumed, with the instance already reset and
            /// its version moved on - so anything still holding the old token is locked out before this
            /// hands the instance to the next caller.
            /// </remarks>
            protected override void OnRecyclable() => owner.Return(this);

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
            internal void EnsureFaulted(CommandFlags flags)
                => TrySetException(
                    Token,
                    new RedisConnectionException(
                        ConnectionFailureType.SocketClosed,
                        flags,
                        "The connection is not available.",
                        null,
                        (CommandStatus)Diagnostics.Status),
                    definite: false);

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
            protected override RespPayload ParseFrame(scoped ReadOnlySpan<byte> frame)
            {
                var reader = new RespReader(frame);
                if (reader.TryMoveNext(checkError: false) && reader.IsError)
                {
                    throw new RedisServerException(
                        RedisErrorKindMetadata.Classify(reader),
                        _flags,
                        reader.ReadString() ?? "Unknown server error.");
                }

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
        }
    }
}
