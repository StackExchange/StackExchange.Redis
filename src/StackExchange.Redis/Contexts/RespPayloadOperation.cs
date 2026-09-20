using System;
using System.Buffers;
using System.Threading;
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
        internal sealed class RespPayloadOperation : RespMessageBase<RespPayload>
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

        /// <summary>The cluster slot this command's keys resolved to, for batch grouping.</summary>
        /// <remarks>
        /// Carried on the operation because the request it was rendered from is gone by the time a batch
        /// is dispatched - the bytes were copied and the caller's frame released.
        /// </remarks>
        internal int Slot { get; set; } = ServerSelectionStrategy.NoSlot;

        /// <summary>The flags the request carried, which the retry and redirect layers read.</summary>
        internal CommandFlags Flags => _flags;

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
        protected override void OnSent() => Profile?.SetRequestSent();

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Finished, not consumed.</b> The record is pushed to its session here rather than when the
        /// caller reads the result, because a fire-and-forget command has no reader and would otherwise
        /// never appear in a profile at all.
        /// </remarks>
        protected override void OnFinished() => Profile?.SetCompleted();

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
    }
}
