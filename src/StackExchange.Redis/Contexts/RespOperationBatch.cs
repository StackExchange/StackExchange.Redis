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
    /// EXPERIMENTAL SPIKE. An executor that accumulates commands and sends them as one run.
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
    internal sealed class RespOperationBatchExecutor : RespExecutorBase
    {
        private readonly RespExecutorBase _inner;
        private readonly object _sync = new();
        private List<RespPayloadOperation>? _queue;
        private bool _sent;

        internal RespOperationBatchExecutor(RespExecutorBase inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc/>
        /// <remarks>Yes: nothing leaves until the run does.</remarks>
        internal override bool Accumulates => true;

        /// <inheritdoc/>
        public override int Database => _inner.Database;

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
            operation.Attach(request.Span, request.Flags, cancellationToken);
            operation.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
            operation.Slot = request.Slot;
            operation.Database = Database; // read if it is ever re-sent on its own, after a redirect

            lock (_sync)
            {
                if (_sent) throw new InvalidOperationException("This batch has already been executed.");
                (_queue ??= []).Add(operation);
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
                return default;
            }

            return new ValueTask<RespPayload>(operation, operation.Token);
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
                queue.Add(head);
                queue.Add(body);
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
            List<RespPayloadOperation>? queue;
            lock (_sync)
            {
                queue = _queue;
                _queue = null;
                _sent = true;
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
            List<RespPayloadOperation>? queue;
            lock (_sync)
            {
                queue = _queue;
                _queue = null;
                _sent = true;
            }

            if (queue is null) return;
            foreach (var operation in queue)
            {
                // never written, so definitely not applied - the retry layer above is free to re-issue
                operation.TrySetException(operation.Token, fault, definite: false);
            }
        }

        /// <remarks>
        /// Outside cluster every request is <c>NoSlot</c>, so this is one group and the cost is a walk
        /// comparing ints. The common case does not pay for the uncommon one.
        /// </remarks>
        private static IEnumerable<List<RespPayloadOperation>> GroupBySlot(List<RespPayloadOperation> queue)
        {
            var slot = queue[0].Slot;
            var single = true;
            for (var i = 1; i < queue.Count && single; i++) single = queue[i].Slot == slot;
            if (single)
            {
                yield return queue;
                yield break;
            }

            var bySlot = new Dictionary<int, List<RespPayloadOperation>>();
            foreach (var operation in queue)
            {
                if (!bySlot.TryGetValue(operation.Slot, out var group)) bySlot.Add(operation.Slot, group = []);
                group.Add(operation);
            }

            foreach (var group in bySlot.Values) yield return group;
        }

        private async Task DispatchAsync(List<RespPayloadOperation> group)
        {
            // BY SLOT, not by "anywhere": the group was formed by slot precisely so it could be routed to
            // the node that owns it. Resolving with no key sent every group to whichever node answered
            // first, which in a cluster is a MOVED for any group whose keys live elsewhere.
            var target = _inner.ResolveForSlot(group[0].Slot, RedisCommand.NONE, CommandFlags.None);

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
                foreach (var operation in group) operation.TrySetException(operation.Token, fault, definite: false);
            }
        }
    }
}
