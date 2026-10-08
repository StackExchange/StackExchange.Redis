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
    /// An executor that accumulates commands and sends them as one run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what design notes section 3c predicted, and it is worth noting how much is missing.</b>
    /// The earlier attempt needed a <c>TaskCompletionSource</c> per element, a
    /// <c>Span&lt;ValueTask&lt;RespPayload&gt;&gt;</c> out-parameter, and a scatter-back into caller
    /// positions - all to answer "how does element 3 of 5 get its result out?". It does not arise here:
    /// element 3 <i>is</i> an operation, whose completion is itself. There is nothing to hand back, so
    /// there is no return channel to design.
    /// </para>
    /// <para>
    /// <b>Grouped by slot, not by server.</b> Grouping by server races a reshard - the map can change
    /// between grouping and writing, and a group would then be split across nodes with no way to tell.
    /// A slot is a property of the keys themselves and cannot move underneath the grouping; where that
    /// slot currently lives is resolved once, at dispatch.
    /// </para>
    /// </remarks>
    /// <summary>A queued operation, and the token of the life that was queued.</summary>
    /// <remarks>
    /// A batch or transaction acts on its queue long after composing it - writing it, failing it, cancelling it,
    /// handing <c>EXEC</c>'s results out - and an element can complete in between (cancelled through its token),
    /// be consumed, and have its pooled instance rented for somebody else's command. Acting through the instance's
    /// CURRENT token would act on that stranger; acting through the token captured here makes a stale element a
    /// no-op. The connection's pending queue keeps its tokens for the same reason (see <c>RespRunEntry</c>).
    /// </remarks>
    internal readonly struct QueuedOperation(RespPayloadOperation operation, short token)
    {
        public readonly RespPayloadOperation Operation = operation;
        public readonly short Token = token;

        /// <summary>Queue an operation as it stands now: its current life.</summary>
        public static QueuedOperation Of(RespPayloadOperation operation) => new(operation, operation.Token);

        /// <summary>The element as a connection writes it.</summary>
        public RespRunEntry Entry => new(Operation, Token);

        public void Deconstruct(out RespPayloadOperation operation, out short token)
        {
            operation = Operation;
            token = Token;
        }

        /// <summary>A run of these, as a connection writes them.</summary>
        internal static RespRunEntry[] ToRun(List<QueuedOperation> operations)
        {
            var run = new RespRunEntry[operations.Count];
            for (var i = 0; i < run.Length; i++) run[i] = operations[i].Entry;
            return run;
        }
    }

    internal sealed class RespOperationBatchExecutor : RespExecutorBase
    {
        private readonly RespExecutorBase _inner;
        private readonly object _sync = new();
        private List<QueuedOperation>? _queue;
        private bool _sent;
        private readonly bool _reusable;

        /// <summary>Create a batch over an executor.</summary>
        /// <param name="inner">The executor the accumulated run is sent through.</param>
        /// <param name="reusable">
        /// Whether executing leaves the batch open for more. The shipped <see cref="IBatch"/> always was:
        /// <c>Execute</c> took what was pending and anything queued afterwards went into the next run, so
        /// code that holds one batch and executes it repeatedly is legitimate and must keep working. The
        /// SER014 <see cref="RespBatch"/> is execute-or-discard, and copies of it share this queue, so it
        /// stays one-shot.
        /// </param>
        internal RespOperationBatchExecutor(RespExecutorBase inner, bool reusable = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _reusable = reusable;
        }

        /// <inheritdoc/>
        /// <remarks>Yes: nothing leaves until the run does.</remarks>
        internal override bool Accumulates => true;

        /// <inheritdoc/>
        public override int Database => _inner.Database;

        /// <summary>Whether this batch has been executed or discarded; either way, nothing more will be sent.</summary>
        internal bool IsSent
        {
            get
            {
                lock (_sync) return _sent;
            }
        }

        /// <summary>How many commands are waiting to be sent.</summary>
        internal int Count
        {
            get
            {
                lock (_sync) return _queue?.Count ?? 0;
            }
        }

        /// <inheritdoc/>
        /// <remarks>Routing is the inner executor's; accumulating does not change where a key lives.</remarks>
        internal override RespExecutorBase? ResolveFor(in RedisKey key, RedisCommand command, CommandFlags flags)
            => _inner.ResolveFor(in key, command, flags);

        /// <inheritdoc/>
        internal override RespExecutorBase? ResolveForSlot(int slot, RedisCommand command, CommandFlags flags)
            => _inner.ResolveForSlot(slot, command, flags);

        /// <inheritdoc/>
        public override ValueTask<EndPoint?> IdentifyEndpointAsync(RedisKey key, CommandFlags flags, CancellationToken cancellationToken = default)
            => _inner.IdentifyEndpointAsync(key, flags, cancellationToken);

        /// <inheritdoc/>
        public override RespPayload Send(in RespRequest request)
            => throw new NotSupportedException("A batched command cannot be waited on before the batch is executed.");

        /// <inheritdoc/>
        /// <remarks>
        /// The returned <see cref="ValueTask{TResult}"/> is real and awaitable <i>after</i>
        /// <see cref="ExecuteAsync"/> - awaiting it before then would wait for something that has not been
        /// asked for yet, which the operation reports rather than hanging.
        /// </remarks>
        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            var operation = RespPayloadOperation.Rent();
            return Enqueue(operation, in request, cancellationToken) ? new ValueTask<RespPayload>(operation, operation.Token) : default;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// A typed operation, not the default's async parse-after-await, which boxed a state machine per command once
        /// the batch was deep (see <see cref="RespPayloadOperation{TResult}"/>) - but handed out as a task:
        /// </para>
        /// <para>
        /// <b>So its reply is parsed when it arrives, in parallel, rather than when it is awaited, in series.</b> Over
        /// the operation, a <c>ValueTask</c> parses in <c>GetResult</c> - which is what makes a single send one object.
        /// But a batch's results are awaited after <c>Execute</c>, one after another, so all of a run's parsing and
        /// recycling landed on the caller's one thread, after the last reply: measured, the context batch carried
        /// half the CPU per command of <c>IBatch</c> and still ran 10-15% slower, because <c>IBatch</c>'s tasks were
        /// completed on the pool as each reply landed. Here every command's task is completed that way (see
        /// <c>RespPayloadOperation&lt;T&gt;.AsTask</c>), for one task per command.
        /// </para>
        /// <para>
        /// <b>Not double-wrapped for <c>IBatch</c>:</b> the task is noted for the thread, and the <c>IDatabase</c> bridge
        /// hands that same task back rather than bridging the <c>ValueTask</c> over it.
        /// </para>
        /// </remarks>
        internal override ValueTask<TResult> SendTypedAsync<TResult>(
            RespRequest request, IRespHandler<TResult> handler, CancellationToken cancellationToken)
        {
            if (typeof(TResult) == typeof(RespPayload)) return RespExecutor.AwaitUncached(this, request, handler, cancellationToken);
            var operation = RespPayloadOperation<TResult>.Rent(handler, this);
            try
            {
                if (!Enqueue(operation, in request, cancellationToken)) return new ValueTask<TResult>(default(TResult)!);
            }
            catch (Exception ex)
            {
                return new ValueTask<TResult>(Task.FromException<TResult>(ex)); // as the async default reported it
            }
            finally
            {
                request.Dispose(); // copied; this reference is done
            }

            // a task, not the operation: see the remarks
            var task = operation.AsTask(operation.Token);
            RespPayloadOperation<TResult>.NoteDispatchedTask(task);
            return new ValueTask<TResult>(task);
        }

        /// <inheritdoc/>
        internal override ValueTask SendVoidAsync(RespRequest request, IRespHandler<bool> handler, CancellationToken cancellationToken)
        {
            var operation = RespPayloadOperation<bool>.Rent(handler, this);
            try
            {
                if (!Enqueue(operation, in request, cancellationToken)) return default;
            }
            catch (Exception ex)
            {
                return new ValueTask(Task.FromException(ex));
            }
            finally
            {
                request.Dispose();
            }

            Task task = operation.AsTask(operation.Token); // a task, as SendTypedAsync
            RespPayloadOperation<bool>.NoteDispatchedTask(task);
            return new ValueTask(task);
        }

        /// <inheritdoc/>
        /// <remarks>Yes: <see cref="Enqueue"/> copies the request into the operation before returning.</remarks>
        internal override bool CopiesRequestOnSend => true;

        /// <summary>Attach the request to the operation and queue it for the run.</summary>
        /// <returns>False if the command is fire-and-forget, and so already answered.</returns>
        private bool Enqueue(RespPayloadOperation operation, in RespRequest request, CancellationToken cancellationToken)
        {
            operation.Attach(request.Span, request.Flags, cancellationToken);
            operation.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
            operation.Slot = request.Slot;
            operation.Database = Database; // read if it is ever re-sent on its own, after a redirect

            lock (_sync)
            {
                if (_sent) throw new InvalidOperationException("This batch has already been executed.");
                (_queue ??= []).Add(QueuedOperation.Of(operation));
            }

            // Fire-and-forget is answered NOW, with a null payload that Parse turns into default(T). The
            // caller has said they do not want the reply, and a batch makes that visible: the task is
            // complete before Execute is even called, which is what the shipped batch does and what
            // BatchQueuedFireAndForgetCompletesImmediatelyWithDefault pins. The operation is still queued
            // and still written - it just has nobody waiting - and DiscardReply is what lets it recycle
            // when its reply lands.
            if ((request.Flags & CommandFlags.FireAndForget) != 0)
            {
                RespPayloadOperation.DiscardReply(operation);
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Yes - a batch is a run, and a run can hold two adjacent entries as easily as one.</b> Both
        /// go into the queue together, so nothing of anybody else's can land between them when the run is
        /// written, which is the whole of what a preamble asks for.
        /// </remarks>
        public override bool CanWritePreamble => true;

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// <b>The gate is not consulted, and that is the point.</b> A gate answers "is this preamble still
        /// needed on THIS connection, right now" - and right now is composition time, which for a batch is
        /// long before anything is written and possibly before a connection exists. So the preamble always
        /// goes. Both of today's gates are idempotent - a redundant <c>SCRIPT LOAD</c> or
        /// <c>HIMPORT PREPARE</c> costs a round trip in the run, nothing more - where a preamble wrongly
        /// skipped is a command that cannot work.
        /// </para>
        /// <para>
        /// Without this the pair fell back to sending the preamble and AWAITING it, which inside an
        /// accumulating executor waits for a send the same call is holding up: the await completed only at
        /// Execute, and the request then arrived after it, reporting that the batch had already been
        /// executed. See <c>Accumulates</c>.
        /// </para>
        /// </remarks>
        public override ValueTask<RespPayload> SendAsync(
            RespRequest preamble,
            RespRequest request,
            IRespPreambleGate? gate,
            CancellationToken cancellationToken = default)
        {
            var head = RespPayloadOperation.Rent();
            head.Attach(preamble.Span, preamble.Flags, default);
            head.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
            head.Slot = preamble.Slot;
            head.Database = Database;

            var body = RespPayloadOperation.Rent();
            body.Attach(request.Span, request.Flags, cancellationToken);
            body.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
            body.Slot = request.Slot;
            body.Database = Database;

            lock (_sync)
            {
                if (_sent) throw new InvalidOperationException("This batch has already been executed.");
                var queue = _queue ??= [];
                queue.Add(QueuedOperation.Of(head));
                queue.Add(QueuedOperation.Of(body));
            }

            // nobody is waiting on the preamble's own reply; it is +OK or the run has failed, and the
            // command behind it reports that better
            RespPayloadOperation.DiscardReply(head);
            return new ValueTask<RespPayload>(body, body.Token);
        }

        /// <summary>Send everything accumulated so far.</summary>
        /// <remarks>
        /// Outside the lock once the queue is taken: dispatching can block on a write, and holding the
        /// accumulation lock across it would serialise a second batch behind this one for no reason.
        /// </remarks>
        internal async Task ExecuteAsync()
        {
            List<QueuedOperation>? queue;
            lock (_sync)
            {
                queue = _queue;
                _queue = null;
                _sent = !_reusable; // reusable: what is queued from here on is the next run
            }

            if (queue is null || queue.Count == 0) return;

            foreach (var group in GroupBySlot(queue))
            {
                await DispatchAsync(group).ConfigureAwait(false);
            }
        }

        /// <summary>Fail everything accumulated; for a batch that is discarded rather than executed.</summary>
        internal void Abandon(Exception fault)
        {
            List<QueuedOperation>? queue;
            lock (_sync)
            {
                queue = _queue;
                _queue = null;
                _sent = true;
            }

            if (queue is null) return;
            foreach (var (operation, token) in queue)
            {
                // never written, so definitely not applied - the retry layer above is free to re-issue
                operation.TrySetException(token, fault, definite: false);
            }
        }

        /// <remarks>
        /// Outside cluster every request is <c>NoSlot</c>, so this is one group and the cost is a walk
        /// comparing ints. The common case does not pay for the uncommon one.
        /// </remarks>
        private static IEnumerable<List<QueuedOperation>> GroupBySlot(List<QueuedOperation> queue)
        {
            var slot = queue[0].Operation.Slot;
            var single = true;
            for (var i = 1; i < queue.Count && single; i++) single = queue[i].Operation.Slot == slot;
            if (single)
            {
                yield return queue;
                yield break;
            }

            var bySlot = new Dictionary<int, List<QueuedOperation>>();
            foreach (var entry in queue)
            {
                var entrySlot = entry.Operation.Slot;
                if (!bySlot.TryGetValue(entrySlot, out var group)) bySlot.Add(entrySlot, group = []);
                group.Add(entry);
            }

            foreach (var group in bySlot.Values) yield return group;
        }

        private async Task DispatchAsync(List<QueuedOperation> group)
        {
            // BY SLOT, not by "anywhere": the group was formed by slot precisely so it could be routed to
            // the node that owns it. Resolving with no key sent every group to whichever node answered
            // first, which in a cluster is a MOVED for any group whose keys live elsewhere.
            var target = _inner.ResolveForSlot(group[0].Operation.Slot, RedisCommand.NONE, CommandFlags.None);

            // waits for a connection, and for any write claim to clear, rather than declining. A batch
            // that arrived before the first connect completed is EARLY, not unservable, and failing it
            // was a real defect: the existing BatchTests caught it the moment they ran on this core.
            if (target is not null && !await target.PrepareRunAsync().ConfigureAwait(false)) target = null;

            // the slot is ours from here; anything issued while this was waiting queued behind it
            var sent = target is not null && target.TrySendBatch(group);
            target?.ReleaseWrites();

            if (!sent)
            {
                // nothing took it; every element is owed an answer, and "never sent" is the honest one
                var fault = new RedisConnectionException(
                    ConnectionFailureType.UnableToResolvePhysicalConnection,
                    CommandFlags.CommandRetryNever,
                    "This executor cannot write a batch as one contiguous run; it has no connection to write it to.",
                    null,
                    CommandStatus.WaitingInBacklog);
                foreach (var (operation, token) in group) operation.TrySetException(token, fault, definite: false);
            }
        }
    }
}
