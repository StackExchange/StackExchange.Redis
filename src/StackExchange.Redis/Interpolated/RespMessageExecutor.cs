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

        public RespPayload Send(in RespRequest request)
        {
            var message = new FrameMessage(Database, request);
            return _target.ExecuteSync(message, PayloadProcessor.Instance)
                   ?? throw new RedisException("No reply.");
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            // the existing pipeline has no cancellation; the token is observed by the caller's await, which
            // is the model settled in design notes section 6.11 - the request completes by itself
            var message = new FrameMessage(Database, request);
            return new(_target.ExecuteAsync(message, PayloadProcessor.Instance, defaultValue: null!)!);
        }

        /// <summary>A message whose body is already framed: writing it is a blit.</summary>
        private sealed class FrameMessage : Message
        {
            private readonly RespRequest _request;

            internal FrameMessage(int database, in RespRequest request)
                : base(database, request.Flags & ~Message.MaskRetryCategory | request.Flags, RedisCommand.UNKNOWN)
            {
                _request = request;
            }

            // an over-estimate is allowed, and the frame knows exactly
            public override int ArgCount => _request.ArgCount;

            // the slot was folded during the write, so routing needs no second look at the keys
            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => _request.Slot;

            protected override void WriteImpl(in MessageWriter writer) => writer.WriteRaw(_request.Span);
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
