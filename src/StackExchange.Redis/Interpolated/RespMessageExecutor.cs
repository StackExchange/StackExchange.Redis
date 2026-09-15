using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Sends a pre-rendered frame through the existing message pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transition bridge, in the direction that matters for actually talking to a server: the frame is
    /// already framed, so the <c>Message</c> wrapping it only has to blit bytes, and the
    /// <c>ResultProcessor</c> only has to hand the raw reply back. Everything between - connection
    /// selection, the backlog, multiplexing, failover - is the existing pipeline, untouched.
    /// </para>
    /// <para>
    /// This exists so the new surface can be validated end to end against a real server before anything is
    /// rewritten. It is <b>scaffolding, not a destination</b>: long term the <c>Message</c> machinery goes
    /// away entirely, replaced by state representing an execution life-cycle, and a rendered frame reaches
    /// the connection with nothing in between.
    /// </para>
    /// <para>
    /// Worth noting what the wrapping costs, because it is almost nothing: <b>one</b> message type covers
    /// every pre-formatted command. The library currently has 75 <c>WriteImpl</c> overrides across 20
    /// files, and they exist only because each command shape writes itself differently. Once the bytes
    /// arrive already framed, there is one shape.
    /// </para>
    /// </remarks>
    internal sealed class RespMessageExecutor : IRespExecutor
    {
        private readonly RedisBase _target;

        internal RespMessageExecutor(RedisBase target, int database)
        {
            _target = target;
            Database = database;
        }

        public int Database { get; }

        /// <summary>Issue the request and return the reply; null if the caller declined one.</summary>
        /// <param name="request">The rendered request.</param>
        /// <remarks>
        /// <b>No reply is an error, except when it was asked for.</b> Fire-and-forget returns the default
        /// from the pipeline - which is null here - and that is the answer, not a fault; the asynchronous
        /// twin below has always passed it straight back. Without the distinction this threw
        /// <c>"No reply."</c> at every synchronous fire-and-forget command on this surface.
        /// </remarks>
        public RespPayload Send(in RespRequest request)
        {
            var message = new FrameMessage(Database, request);
            var reply = _target.ExecuteSync(message, PayloadProcessor.Instance);
            if (reply is null && (request.Flags & CommandFlags.FireAndForget) == 0)
            {
                throw new RedisException("No reply.");
            }

            return reply!; // null only for fire-and-forget, which every consumer already tests for
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            // the existing pipeline has no cancellation; the token is observed by the caller's await, which
            // is the model settled in design notes section 6.11 - the request completes by itself
            var message = new FrameMessage(Database, request);
            return new(_target.ExecuteAsync(message, PayloadProcessor.Instance, defaultValue: null!)!);
        }

        /// <summary>A message whose body is already framed: writing it is a blit.</summary>
        /// <remarks>
        /// <para>
        /// <b>Ownership, and the one case where a blit is not enough.</b> The pipeline writes a message
        /// some time after the caller regains control, and the caller's <c>RespRequest</c> owns a pooled,
        /// reference-counted buffer that it disposes when its own call is done. For an ordinary command
        /// those two orderings cannot cross: "the call is done" means the reply arrived, which is strictly
        /// after the write.
        /// </para>
        /// <para>
        /// <b>Fire-and-forget breaks that.</b> The caller has declined the reply, so its call completes the
        /// instant the message is queued - and its dispose then hands the buffer back to the pool while it
        /// is still sitting in the write queue. The symptom is not subtle but it is far away: an
        /// <see cref="ObjectDisposedException"/> from inside <c>WriteMessageToServerInsideWriteLock</c>,
        /// which kills the connection and fails every other command in flight on it. Found by running the
        /// existing test suite against this path, which is exactly what that exercise is for.
        /// </para>
        /// <para>
        /// So a fire-and-forget message takes a plain copy of the bytes. Not a pooled one: a rented array
        /// would need returning, and "when is it safe to return this?" is the question that just went
        /// wrong. Fire-and-forget is the path that has already chosen throughput over bookkeeping, and one
        /// short-lived array is a cheaper answer than a lifetime protocol.
        /// </para>
        /// </remarks>
        private sealed class FrameMessage : Message
        {
            private readonly RespRequest _request;
            private readonly byte[]? _copy;

            internal FrameMessage(int database, in RespRequest request)
                // the command's identity, not just its bytes: without it the pipeline cannot tell a write
                // from a read, so IsPrimaryOnly lets a write be routed to a replica, and a profiler
                // reports every command in the library as UNKNOWN
                : base(database, request.Flags & ~Message.MaskRetryCategory | request.Flags, request.Command)
            {
                _request = request;
                if ((request.Flags & CommandFlags.FireAndForget) != 0)
                {
                    _copy = request.Span.ToArray();
                }
            }

            // an over-estimate is allowed, and the frame knows exactly

            /// <remarks>
            /// <b>Minus the command.</b> Every other <see cref="Message"/> reports the count the writer
            /// then adds one to for the <c>*N</c> header, whereas a frame's own count already includes the
            /// command - it counted while writing. Reporting the frame's number directly would make
            /// <c>CheckMessage</c> reject a frame one argument earlier than the identical classic message.
            /// </remarks>
            public override int ArgCount => _request.ArgCount - 1;

            // the slot was folded during the write, so routing needs no second look at the keys
            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => _request.Slot;

            protected override void WriteImpl(in MessageWriter writer)
                => writer.WriteRaw(_copy ?? _request.Span);
        }

        /// <summary>Captures the raw reply, undecoded, for the handler (or the cache) to read.</summary>
        /// <remarks>
        /// Same shape as <c>ResultProcessor.RespResult</c>, and for the same reason: <c>SetResult</c> is
        /// overridden rather than <c>SetResultCore</c>, so this runs <b>before</b> the base implementation's
        /// <c>MovePastBof()</c> consumes the prefix and length bytes that the capture needs.
        /// </remarks>
        private sealed class PayloadProcessor : ResultProcessor<RespPayload>
        {
            internal static readonly PayloadProcessor Instance = new();

            public override bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                var totalBytes = checked((int)reader.ProtocolBytesRemaining);

                var probe = reader;
                probe.MovePastBof();
                if (probe.IsError) return base.SetResult(connection, message, ref reader);

                var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, totalBytes));
                reader.CopyRawTo(buffer.AsSpan(0, totalBytes));
                SetResult(message, new RespPayload(RESPite.Buffers.RefCountedBuffer.Adopt(buffer, buffer.Length), 0, totalBytes));
                return true;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader) =>
                throw new NotSupportedException(); // SetResult is fully overridden above
        }
    }
}
