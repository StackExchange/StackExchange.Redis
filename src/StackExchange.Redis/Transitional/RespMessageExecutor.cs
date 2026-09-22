using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
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
    internal sealed class RespMessageExecutor : RespExecutorBase, IRespRunExecutor
    {
        private readonly RedisBase _target;

        internal RespMessageExecutor(RedisBase target, int database)
        {
            _target = target;
            Database = database;
        }

        public override int Database { get; }

        /// <summary>The same target, sending to a different database.</summary>
        /// <param name="database">The database index.</param>
        /// <remarks>
        /// The target is the thing that owns the connection and the routing; the database is one field of
        /// the message it builds. So re-pointing is a new executor over the same target, not a new target -
        /// which is why <see cref="RespContext.WithDatabase"/> can offer it at all.
        /// </remarks>
        internal RespMessageExecutor WithDatabase(int database)
            => database == Database ? this : new RespMessageExecutor(_target, database);

        /// <summary>Issue the request and return the reply; null if the caller declined one.</summary>
        /// <param name="request">The rendered request.</param>
        /// <remarks>
        /// <b>No reply is an error, except when it was asked for.</b> Fire-and-forget returns the default
        /// from the pipeline - which is null here - and that is the answer, not a fault; the asynchronous
        /// twin below has always passed it straight back. Without the distinction this threw
        /// <c>"No reply."</c> at every synchronous fire-and-forget command on this surface.
        /// </remarks>
        public override RespPayload Send(in RespRequest request)
        {
            var message = new FrameMessage(Database, request);
            var reply = _target.ExecuteSync(message, PayloadProcessor.Instance);
            if (reply is null && (request.Flags & CommandFlags.FireAndForget) == 0)
            {
                throw new RedisException("No reply.");
            }

            return reply!; // null only for fire-and-forget, which every consumer already tests for
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            // the existing pipeline has no cancellation; the token is observed by the caller's await, which
            // is the model settled in design notes section 6.11 - the request completes by itself
            var message = new FrameMessage(Database, request);
            return new(_target.ExecuteAsync(message, PayloadProcessor.Instance, defaultValue: null!)!);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// <b>The same mechanism as the preamble pair, at a larger N.</b> <c>FrameRunMessage</c> is an
        /// <see cref="IMultiMessage"/>, and <c>PhysicalBridge</c> expands one <i>inside the write lock</i>,
        /// writing every yielded sub-command consecutively - which is the whole of the contiguity
        /// guarantee, and what <c>TransactionMessage</c> has always used.
        /// </para>
        /// <para>
        /// <b>The last request is the run message itself</b>, and the rest are yielded ahead of it. That is
        /// not a trick for its own sake: only the messages a multi-message yields are enqueued for replies,
        /// so a wrapper that yielded none of itself would never be written and never complete - which is
        /// the bug <c>FramePairMessage</c> documents having hit. Yielding itself last means the outer task
        /// is the last reply, and the last reply landing is the run being done.
        /// </para>
        /// <para>
        /// <b>Each of the others carries its own result box</b>, which is how a single write produces a
        /// task per request. The boxes are the shim's own plumbing - <c>IResultBox</c> belongs to the
        /// <c>Message</c> world that is being replaced - and deliberately do not reach the context surface,
        /// which sees only the <see cref="ValueTask{TResult}"/>s.
        /// </para>
        /// </remarks>
        public ValueTask SendAsync(
            scoped ReadOnlySpan<RespRequest> run,
            scoped Span<ValueTask<RespPayload>> replies,
            CancellationToken cancellationToken = default)
        {
            if (run.Length != replies.Length)
            {
                throw new ArgumentException("One reply slot is needed per request.", nameof(replies));
            }

            switch (run.Length)
            {
                case 0:
                    return default;
                case 1:
                    // a run of one is a send; the multi-message would buy nothing and cost an expansion
                    replies[0] = SendAsync(run[0], cancellationToken);
                    return Awaited(replies[0]);
            }

            var tail = run.Length - 1;
            var heads = new Message[tail];
            for (var i = 0; i < tail; i++)
            {
                var box = TaskResultBox<RespPayload>.Create(out var source, null);
                var message = new FrameMessage(Database, run[i]);
                message.SetSource(box, PayloadProcessor.Instance);
                heads[i] = message;
                replies[i] = new ValueTask<RespPayload>(source.Task);
            }

            var last = _target.ExecuteAsync(
                new FrameRunMessage(Database, heads, run[tail]),
                PayloadProcessor.Instance,
                defaultValue: null!)!;

            replies[tail] = new ValueTask<RespPayload>(last);
            return new ValueTask(last);

            static async ValueTask Awaited(ValueTask<RespPayload> pending) => await pending.ForAwait();
        }

        /// <summary>A run of frames written as one unit; the last of them is this message.</summary>
        /// <remarks><inheritdoc cref="SendAsync(ReadOnlySpan{RespRequest}, Span{ValueTask{RespPayload}}, CancellationToken)" path="/remarks"/></remarks>
        private sealed class FrameRunMessage : Message, IMultiMessage
        {
            private readonly Message[] _heads;
            private readonly RespRequest _tail;

            internal FrameRunMessage(int database, Message[] heads, in RespRequest tail)
                : base(database, tail.Flags & ~MaskRetryCategory | tail.Flags, tail.Command)
            {
                _heads = heads;
                _tail = tail;
            }

            /// <remarks><inheritdoc cref="FramePairMessage.CanWriteWithoutExpansion" path="/remarks"/></remarks>
            public bool CanWriteWithoutExpansion => false;

            public override int ArgCount => _tail.ArgCount - 1;

            /// <summary>
            /// The whole run's slot, which is what makes "one connection" checkable rather than hoped for.
            /// </summary>
            /// <remarks>
            /// Combining is what refuses a run that spans two slots: <c>CombineSlot</c> answers
            /// <c>MultipleSlots</c>, and routing fails the send rather than writing half of it to one
            /// server. A caller that wants a batch across servers has to split it first, which is exactly
            /// what <c>RedisBatch.Execute</c> does.
            /// </remarks>
            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                var slot = _tail.Slot;
                foreach (var head in _heads)
                {
                    slot = ServerSelectionStrategy.CombineSlot(slot, head.GetHashSlot(serverSelectionStrategy));
                }

                return slot;
            }

            public IEnumerable<Message>? GetMessages(PhysicalConnection connection) => Expand();

            private IEnumerable<Message> Expand()
            {
                foreach (var head in _heads) yield return head;
                yield return this;
            }

            protected override void WriteImpl(in MessageWriter writer) => writer.WriteRaw(_tail.Span);
        }

        /// <summary>
        /// Write a preamble and a request as one unit, so nothing interleaves and both reach one connection.
        /// </summary>
        /// <remarks>
        /// An <see cref="IMultiMessage"/>, which is how the pipeline has always expressed "these go
        /// together" - it is what <c>ScriptEvalMessage</c> uses for exactly this pairing today. Going
        /// through it rather than around it means the pair inherits ordering, the backlog, retry and the
        /// reconnect handshake, none of which a second write path could have shared.
        /// </remarks>
        /// <inheritdoc/>
        /// <remarks>This is the executor that actually reaches a connection, so it is the one that can.</remarks>
        public override bool CanWritePreamble => true;

        public override ValueTask<RespPayload> SendAsync(RespRequest preamble, RespRequest request, IRespPreambleGate? gate, CancellationToken cancellationToken = default)
        {
            var message = new FramePairMessage(Database, preamble, request, gate);
            return new(_target.ExecuteAsync(message, PayloadProcessor.Instance, defaultValue: null!)!);
        }

        /// <summary>A preamble and a request, expanded into two messages that are written together.</summary>
        /// <remarks>
        /// The preamble's reply is consumed and thrown away - it exists for its effect on the connection,
        /// not for its value - so it carries a processor that demands nothing of it. The pair routes by the
        /// <b>request</b>, because the preamble is typically keyless and would otherwise route anywhere.
        /// </remarks>
        private sealed class FramePairMessage : Message, IMultiMessage
        {
            /// <remarks>
            /// No: the request half alone would be an <c>EVALSHA</c> with no <c>SCRIPT LOAD</c> behind it.
            /// Unlike the classic script messages there is no body-carrying spelling to fall back to, so a
            /// dropped expansion is a <c>NOSCRIPT</c> waiting inside someone's <c>EXEC</c> array.
            /// </remarks>
            public bool CanWriteWithoutExpansion => false;

            private readonly int _database;
            private readonly RespRequest _preamble;
            private readonly RespRequest _request;
            private readonly IRespPreambleGate? _gate;

            internal FramePairMessage(int database, in RespRequest preamble, in RespRequest request, IRespPreambleGate? gate = null)
                : base(database, request.Flags & ~Message.MaskRetryCategory | request.Flags, request.Command)
            {
                _database = database;
                _preamble = preamble;
                _request = request;
                _gate = gate;
            }

            public override int ArgCount => _request.ArgCount - 1;

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => _request.Slot;

            /// <remarks>
            /// <b>The tail is <c>this</c>, not a second message.</b> The caller's result box is on this
            /// message - it is what <c>ExecuteAsync</c> was handed - and only the messages yielded here are
            /// enqueued for a reply. Yielding a fresh <c>FrameMessage</c> for the request instead left this
            /// one holding the caller's task, never enqueued, and therefore never completed. Same shape as
            /// <c>ScriptEvalMessage</c>, which yields itself for the same reason.
            /// </remarks>
            public IEnumerable<Message>? GetMessages(PhysicalConnection connection)
                // the write-time half: the connection - and so the endpoint whose script cache is in
                // question - is not known until here, which is why this cannot be decided when rendering
                => _gate is { } gate && !gate.IsNeeded(connection) ? null : Expand();

            private IEnumerable<Message> Expand()
            {
                var head = new FrameMessage(_database, _preamble, _gate);
                head.SetInternalCall();
                head.SetSource(PreambleProcessor.Instance, null);
                yield return head;
                yield return this;
            }

            /// <remarks>
            /// Writing the pair means writing its <i>request</i>: the preamble is a separate message, and
            /// this one must still be writable on its own for the path where the expansion is declined.
            /// </remarks>
            protected override void WriteImpl(in MessageWriter writer) => writer.WriteRaw(_request.Span);
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

            /// <summary>Set only on a preamble, and only when it establishes something skippable.</summary>
            internal IRespPreambleGate? Gate { get; }

            internal FrameMessage(int database, in RespRequest request, IRespPreambleGate? gate = null)
                // the command's identity, not just its bytes: without it the pipeline cannot tell a write
                // from a read, so IsPrimaryOnly lets a write be routed to a replica, and a profiler
                // reports every command in the library as UNKNOWN
                : base(DatabaseFor(database, request.Command), request.Flags & ~Message.MaskRetryCategory | request.Flags, request.Command)
            {
                _request = request;
                Gate = gate;
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

            /// <summary>Drop a database the command does not take, rather than asserting on it.</summary>
            /// <remarks>
            /// A context carries a database because most commands need one, but a global command -
            /// <c>SCRIPT</c>, <c>CLIENT</c>, <c>INFO</c> - rejects it outright: "A target database is not
            /// required for SCRIPT", thrown at write time, which fails the connection rather than the call.
            /// The same normalisation the ad-hoc <c>Execute</c> path already does, and for the same reason -
            /// the caller did not ask for a database, the context simply had one.
            /// </remarks>
            private static int DatabaseFor(int database, RedisCommand command)
                => database >= 0 && !RequiresDatabase(command) ? -1 : database;

            protected override void WriteImpl(in MessageWriter writer)
                => writer.WriteRaw(_copy ?? _request.Span);
        }

        /// <summary>Consumes a preamble's reply without judging it.</summary>
        /// <remarks>
        /// A preamble is sent for its effect on the connection, not its value, and different preambles
        /// answer differently - <c>SCRIPT LOAD</c> replies with a 40-byte hash, not <c>+OK</c>. This used to
        /// be <c>DemandOK</c>, which rejects that hash as an unexpected response and takes the connection
        /// down with it; nothing noticed because every test of this path replied "+OK" from a fake.
        /// Errors still fault the message - the base handles those before this is reached - so accepting
        /// anything here means accepting any <i>successful</i> reply.
        /// </remarks>
        private sealed class PreambleProcessor : ResultProcessor<bool>
        {
            internal static readonly PreambleProcessor Instance = new();

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // reached only on success - the base handles errors before this - so this is the point at
                // which the effect is known to hold, matching where ResultProcessor.ScriptLoad records the
                // classic path's belief. Recording on send would claim an effect the server never confirmed.
                if (message is FrameMessage { Gate: { } gate }) gate.OnEstablished(connection);

                SetResult(message, true);
                return true;
            }
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

            /// <summary>
            /// Notice a <c>NOSCRIPT</c>, which this path could previously only fail on - for ever.
            /// </summary>
            /// <remarks>
            /// The belief that an endpoint holds a script is what lets the write-time gate skip
            /// <c>SCRIPT LOAD</c>, and <c>NOSCRIPT</c> is the only evidence that belief has gone stale -
            /// a <c>SCRIPT FLUSH</c>, a restart, a failover to a node that never had it. Without this the
            /// belief survived the very reply that disproved it, so the next call skipped the load again
            /// and failed the same way: a permanent failure rather than a transient one.
            /// </remarks>
            protected override ReplyVerdict Inspect(PhysicalConnection connection, Message message, in RespReader reader)
            {
                var probe = reader;
                probe.MovePastBof();
                return probe.IsError ? NoScriptVerdict(connection, message, in probe) : ReplyVerdict.Complete;
            }

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
