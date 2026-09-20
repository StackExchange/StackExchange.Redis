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

            lock (_sync)
            {
                if (_sent) throw new InvalidOperationException("This batch has already been executed.");
                (_queue ??= []).Add(operation);
            }

            return new ValueTask<RespPayload>(operation, operation.Token);
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
            var target = _inner.ResolveFor(default, RedisCommand.NONE, CommandFlags.None);
            if (target is null || !target.TrySendBatch(group))
            {
                // nothing took it; every element is owed an answer, and "never sent" is the honest one
                var fault = new RedisConnectionException(
                    ConnectionFailureType.UnableToResolvePhysicalConnection,
                    CommandFlags.CommandRetryNever,
                    "No endpoint is available to serve this batch.",
                    null,
                    CommandStatus.WaitingInBacklog);
                foreach (var operation in group) operation.TrySetException(operation.Token, fault, definite: false);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
