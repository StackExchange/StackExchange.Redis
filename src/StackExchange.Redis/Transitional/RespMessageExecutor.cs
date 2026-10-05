using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
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

        /// <summary>The one server this executor must send to, or null to route normally.</summary>
        /// <remarks>
        /// Only ever set by <see cref="ResolveForChannel"/>. The shipped path carries the same thing as an
        /// argument to <c>ExecuteAsync</c>; here it is a field, because the new surface routes by choosing
        /// an executor rather than by passing a server down each call.
        /// </remarks>
        private readonly ServerEndPoint? _server;

        internal RespMessageExecutor(RedisBase target, int database, ServerEndPoint? server = null)
        {
            _target = target;
            Database = database;
            _server = server;
        }

        public override int Database { get; }

        /// <summary>The same target, sending to a different database.</summary>
        /// <param name="database">The database index.</param>
        /// <remarks>
        /// The target is the thing that owns the connection and the routing; the database is one field of
        /// the message it builds. So re-pointing is a new executor over the same target, not a new target -
        /// which is why <see cref="RespContext.WithDatabase"/> can offer it at all.
        /// </remarks>
        internal override RespExecutorBase WithDatabase(int database)
            => database == Database ? this : new RespMessageExecutor(_target, database, _server);

        /// <inheritdoc/>
        /// <remarks>
        /// The subscription registry is the multiplexer's, and this executor has one - so the preference
        /// is answered here rather than invented at the call site. An unsubscribed channel answers null and
        /// the publish routes normally, which is what the shipped path does too.
        /// </remarks>
        internal override RespExecutorBase? ResolveForChannel(in RedisChannel channel)
        {
            var server = _target.multiplexer.GetSubscribedServer(channel);
            return server is null ? null : new RespMessageExecutor(_target, Database, server);
        }

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
            var reply = _target.ExecuteSync(message, PayloadProcessor.Instance, server: _server);
            if (reply is null && (request.Flags & CommandFlags.FireAndForget) == 0)
            {
                throw new RedisException("No reply.");
            }

            return reply!; // null only for fire-and-forget, which every consumer already tests for
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <c>PING</c> rather than the real command, matching the shipped implementation: the question is
        /// whether the connection that <i>would</i> take this key is up, and PING routes the same way
        /// without implying a command that might be disabled in the map.
        /// </remarks>
        internal override bool IsReachable(in RedisKey key, CommandFlags flags)
            => _target.multiplexer.SelectServer(RedisCommand.PING, flags, key)?.IsConnected == true;

        /// <inheritdoc/>
        /// <remarks>
        /// <c>PING</c> for a null key and <c>EXISTS</c> otherwise, matching the shipped implementation
        /// exactly: <c>EXISTS</c> because it is a read that routes by the key and changes nothing, and
        /// <c>PING</c> because with no key there is nothing to route by. The reply itself is discarded -
        /// <c>ConnectionIdentity</c> reads the <i>connection</i> that answered, not the payload, which is
        /// precisely what the new surface cannot yet express and why this still goes through a Message.
        /// </remarks>
        public override ValueTask<EndPoint?> IdentifyEndpointAsync(
            RedisKey key,
            CommandFlags flags,
            CancellationToken cancellationToken = default)
        {
            var message = key.IsNull
                ? Message.Create(-1, flags, RedisCommand.PING)
                : Message.Create(Database, flags, RedisCommand.EXISTS, key);
            return new(_target.ExecuteAsync(message, ResultProcessor.ConnectionIdentity));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            // the existing pipeline has no cancellation; the token is observed by the caller's await, which
            // is the model settled in design notes section 6.11 - the request completes by itself
            var message = new FrameMessage(Database, request);
            return new(_target.ExecuteAsync(message, PayloadProcessor.Instance, defaultValue: null!, server: _server)!);
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
        /// <summary>The sub-command a rendered frame carries, if it is one we recognise.</summary>
        /// <param name="request">The rendered frame.</param>
        /// <param name="subCommand">The sub-command.</param>
        /// <remarks>
        /// <b>Recovered from the frame, because the admin gate is sub-command aware.</b> <c>CLIENT</c> as a
        /// whole is admin, but <c>CLIENT ID</c>, <c>GETNAME</c>, <c>SETNAME</c>, <c>INFO</c> and
        /// <c>SETINFO</c> are not - so a message reporting only its command has every one of those refused
        /// when <c>AllowAdmin</c> is off. The shipped ad-hoc message exposes its first argument for exactly
        /// this reason; once the command is rendered, the frame is the only copy of it left.
        /// </remarks>
        internal static bool TryGetSubCommand(in RespRequest request, out Message.SubCommand subCommand)
        {
            // the command token is not an argument here, so index 0 IS the sub-command; and the span must
            // be big enough for every argument, because resolving refuses a short one rather than filling
            // what it can - so it is sized from the whole frame, which is an upper bound
            var wanted = request.ArgCount;
            Span<KeyRange> ranges = wanted <= 16 ? stackalloc KeyRange[16] : new KeyRange[wanted];
            if (request.TryGetAllArguments(ranges) > 0
                && Message.SubCommandMetadata.TryParse(request.GetKey(ranges[0]), out subCommand))
            {
                return true;
            }

            subCommand = Message.SubCommand.Unknown;
            return false;
        }

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

        /// <inheritdoc/>
        /// <remarks>
        /// Yes: everything here becomes a <c>Message</c> on the existing pipeline, which has carried the
        /// timeout sweep - and the diagnostics that go with it - since long before this type existed.
        /// </remarks>
        internal override bool EnforcesTimeouts => true;

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
                : base(database, request.Flags & ~CommandFlagsInternal.MaskRetryCategory | request.Flags, request.Command)
            {
                _database = database;
                _preamble = preamble;
                _request = request;
                _gate = gate;
            }

            public override int ArgCount => _request.ArgCount - 1;

            /// <inheritdoc/>
            /// <remarks><inheritdoc cref="RespMessageExecutor.TryGetSubCommand" path="/remarks"/></remarks>
            protected override bool TryGetSubCommand(out SubCommand subCommand)
                => RespMessageExecutor.TryGetSubCommand(in _request, out subCommand);

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
        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// <b>Not yet, and the reason is a redirect.</b> The operation form below is written and works -
        /// <c>IMultiMessage</c> gives the contiguity a run needs, since <c>PhysicalBridge</c> expands one
        /// inside its write lock - but turning this on made
        /// <c>MovedToSameEndpoint_BatchCommands_QueuedDuringReconnect</c> hang: a <c>-MOVED</c> re-issues
        /// the message, and re-issuing a multi-message re-expands a wrapper whose heads were already
        /// enqueued. Batches over this shim therefore still fall back to the shipped implementation.
        /// </para>
        /// <para>
        /// Left as false rather than reverted, because the work it gates is not wasted: the batch bugs it
        /// exposed - a preamble deadlocking inside an accumulating executor, fire-and-forget never
        /// completing early, a batch not reporting itself as one - were real and are fixed, and they
        /// applied to the new core too.
        /// </para>
        /// </remarks>
        internal override bool CanWriteRuns => false;

        /// <inheritdoc/>
        /// <remarks>
        /// The operations already own the completions their callers are awaiting, so each is written by an
        /// <see cref="OperationMessage"/> that pushes the reply straight back into it. The last of them
        /// carries the run, for the reason the request form documents: only the messages a multi-message
        /// yields are enqueued for replies, so a wrapper yielding none of itself would never be written.
        /// </remarks>
        internal override bool TrySendBatch(List<RespPayloadOperation> operations)
        {
            if (operations.Count == 0) return true;

            // all but the last are yielded ahead of the run; the LAST IS the run message itself, exactly
            // as the request form does it. Only the messages a multi-message yields are enqueued for
            // replies, so a wrapper that yielded none of itself would never be written and never complete.
            var tail = operations.Count - 1;
            var heads = new Message[tail];
            for (var i = 0; i < tail; i++)
            {
                var head = new OperationMessage(Database, operations[i]);
                Forward(head, operations[i]);
                heads[i] = head;
            }

            var run = new OperationRunMessage(Database, heads, operations[tail]);
            Forward(_target.ExecuteAsync(run, PayloadProcessor.Instance, defaultValue: null!)!, operations[tail]);
            return true;
        }

        /// <summary>The operations of a batch, written as one unit.</summary>
        /// <remarks><inheritdoc cref="TrySendBatch" path="/remarks"/></remarks>
        private sealed class OperationRunMessage : Message, IMultiMessage
        {
            private readonly Message[] _heads;

            internal OperationRunMessage(int database, Message[] heads, RespPayloadOperation tail)
                : base(database, tail.Flags, RedisCommand.UNKNOWN)
            {
                _heads = heads;
                Operation = tail;
            }

            /// <summary>The last operation of the run, which this message writes and completes.</summary>
            internal RespPayloadOperation Operation { get; }

            public bool CanWriteWithoutExpansion => false;

            public override int ArgCount => 0;

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                var slot = Operation.Slot;
                foreach (var message in _heads)
                {
                    slot = ServerSelectionStrategy.CombineSlot(slot, message.GetHashSlot(serverSelectionStrategy));
                }

                return slot;
            }

            public IEnumerable<Message>? GetMessages(PhysicalConnection connection) => Expand();

            private IEnumerable<Message> Expand()
            {
                foreach (var head in _heads) yield return head;
                yield return this;
            }

            protected override void WriteImpl(in MessageWriter writer)
            {
                if (!Operation.TryReserveRequest(Operation.Token, out var payload)) return;
                try
                {
                    writer.WriteRaw(payload.Span);
                }
                finally
                {
                    Operation.ReleaseRequest();
                }
            }
        }

        /// <summary>
        /// Writes an operation's own already-rendered bytes, and completes <b>that operation</b> with the
        /// reply.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The bridge the batch and transaction paths need.</b> Those paths deal in
        /// <see cref="RespPayloadOperation"/>s that already exist and already own the completion a caller
        /// is awaiting - unlike the run path, which is handed requests and creates its own. So this
        /// message carries the operation rather than a request: it writes what the operation holds, and
        /// pushes the reply back into it.
        /// </para>
        /// <para>
        /// It is deliberately thin. Everything about ordering, contiguity and slots is the caller's; all
        /// this does is get bytes out through the old pipeline and the answer back to the right waiter.
        /// </para>
        /// </remarks>
        private sealed class OperationMessage : Message
        {
            internal readonly RespPayloadOperation Operation;

            internal OperationMessage(int database, RespPayloadOperation operation)
                : base(database, operation.Flags, RedisCommand.UNKNOWN)
            {
                Operation = operation;
            }

            public override int ArgCount => 0;

            /// <inheritdoc/>
            /// <remarks>Already folded when the operation's frame was rendered; nothing here can improve on it.</remarks>
            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => Operation.Slot;

            protected override void WriteImpl(in MessageWriter writer)
            {
                if (!Operation.TryReserveRequest(Operation.Token, out var payload)) return;
                try
                {
                    writer.WriteRaw(payload.Span);
                }
                finally
                {
                    Operation.ReleaseRequest();
                }
            }
        }

        /// <summary>
        /// Attaches an existing operation to a message, so the message's reply completes <b>that
        /// operation</b>.
        /// </summary>
        /// <remarks>
        /// <b>Reuses <c>PayloadProcessor</c> rather than parsing replies itself</b>, and the first attempt
        /// here did not - which is exactly what went wrong. A bespoke processor that copied the reply
        /// straight into the operation also bypassed everything the old read path does around a reply: a
        /// <c>-MOVED</c> was handed to the caller as a server error instead of being followed, so a
        /// batch during a reconfiguration failed where it should have been re-issued. Going through the
        /// ordinary processor inherits redirects, NOSCRIPT repair and error handling for free.
        /// </remarks>
        private static void Forward(Message message, RespPayloadOperation operation)
        {
            var box = TaskResultBox<RespPayload>.Create(out var source, null);
            message.SetSource(box, PayloadProcessor.Instance);
            Forward(source.Task, operation);
        }

        /// <summary>
        /// The same hand-off for a message whose completion somebody else owns.
        /// </summary>
        /// <remarks>
        /// <b>The tail of a run is executed rather than merely enqueued</b>, and <c>ExecuteAsync</c> sets
        /// the message's source itself - overwriting any box attached beforehand, which left the tail's
        /// operation waiting for a completion that had been replaced. Forwarding from the task it hands
        /// back is the same wiring from the other end.
        /// </remarks>
        private static void Forward(Task<RespPayload> pending, RespPayloadOperation operation)
        {
            pending.ContinueWith(
                static (completed, state) =>
                {
                    var target = (RespPayloadOperation)state!;
                    if (completed.IsFaulted)
                    {
                        target.TrySetException(target.Token, completed.Exception!.InnerException ?? completed.Exception);
                        return;
                    }

                    using var payload = completed.Result;
                    target.TrySetResult(target.Token, payload.Span);
                },
                operation,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        private sealed class FrameMessage : Message
        {
            private readonly RespRequest _request;
            private readonly byte[]? _copy;

            /// <summary>Set only on a preamble, and only when it establishes something skippable.</summary>
            internal IRespPreambleGate? Gate { get; }

            /// <summary>The command and its first key, for diagnostics.</summary>
            /// <remarks>
            /// <b>This is what users read when something goes wrong.</b> The base implementation reports
            /// the command alone, so "no connection is active/available to service this operation: SADD"
            /// named the command but not the key - and a message naming which key you were writing is
            /// most of the value when you are staring at a connection failure in production.
            /// <para>
            /// Recovered from the frame's own key marks, which the writer already recorded for routing, so
            /// this costs nothing until somebody asks. Only the first key: that is what the shipped
            /// <c>CommandKeyBase</c> reports too, and a variadic command's whole key list would bury the
            /// diagnostic it is meant to support.
            /// </para>
            /// </remarks>
            public override string CommandAndKey
            {
                get
                {
                    if (_request.KeyCount > 0)
                    {
                        Span<KeyRange> ranges = stackalloc KeyRange[1];
                        if (_request.TryGetKeys(ranges) > 0)
                        {
                            var key = _request.GetKey(in ranges[0]);
                            if (!key.IsEmpty)
                            {
#if NETCOREAPP3_1_OR_GREATER
                                return $"{Command} {System.Text.Encoding.UTF8.GetString(key)}";
#else
                                return $"{Command} {System.Text.Encoding.UTF8.GetString(key.ToArray())}";
#endif
                            }
                        }
                    }

                    return base.CommandAndKey;
                }
            }

            internal FrameMessage(int database, in RespRequest request, IRespPreambleGate? gate = null)
                // the command's identity, not just its bytes: without it the pipeline cannot tell a write
                // from a read, so IsPrimaryOnly lets a write be routed to a replica, and a profiler
                // reports every command in the library as UNKNOWN
                : base(DatabaseFor(database, request.Command), request.Flags & ~CommandFlagsInternal.MaskRetryCategory | request.Flags, request.Command)
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

            /// <inheritdoc/>
            /// <remarks><inheritdoc cref="RespMessageExecutor.TryGetSubCommand" path="/remarks"/></remarks>
            protected override bool TryGetSubCommand(out SubCommand subCommand)
                => RespMessageExecutor.TryGetSubCommand(in _request, out subCommand);

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
