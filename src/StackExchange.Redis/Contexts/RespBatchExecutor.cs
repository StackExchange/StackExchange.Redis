using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// Batching, as a decorator on the <b>executor</b>: <c>SendAsync</c> accumulates instead of sending,
    /// and everything queued goes out when the batch is executed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No generics, and that is the whole shape of it.</b> The obvious design is an untyped base plus a
    /// <c>Pending&lt;T&gt; : TaskCompletionSource&lt;T&gt;</c> that proxies the typed result out - which
    /// C# would refuse anyway, since a class cannot derive from both. It is not needed:
    /// <see cref="IRespExecutor"/> has <b>already</b> erased the type. It deals in
    /// <see cref="RespPayload"/>, and the <see cref="IRespHandler{TResult}"/> that turns a payload into a
    /// <c>T</c> is applied by <c>RespExecutor.AwaitUncached</c>, one layer up and after this has handed the
    /// payload back. So a queued command needs a <see cref="TaskCompletionSource{TResult}"/> of exactly one
    /// type, whatever the caller asked for.
    /// </para>
    /// <para>
    /// <b>One allocation per queued command</b>, because the pending entry <i>is</i> the completion source
    /// rather than holding one - which is the good half of the sketch above, kept.
    /// </para>
    /// <para>
    /// <b>The frame needs no retain</b>, for the reason that makes this whole layer cheap: an executor may
    /// use a request until the task it returned completes, and the caller holds its reference until then.
    /// A batched send completes at execute, which is after the bytes have been written. Fire-and-forget is
    /// the documented exception - it completes before the write - which is why <c>FrameMessage</c> copies
    /// for that case and only that case.
    /// </para>
    /// <para>
    /// <b>This pipelines; it does not yet batch.</b> Executing issues every queued send before awaiting any
    /// of them, so they travel without waiting for each other - but they are separate messages, and
    /// another caller's command may land between two of them. The shipped <c>RedisBatch.Execute</c> gets
    /// contiguity by grouping <c>Message</c>s per bridge and clearing the flush flag on all but the last,
    /// which needs the messages rather than the frames. Noted in the queue.
    /// </para>
    /// </remarks>
    internal sealed class RespBatchExecutor : IRespExecutor
    {
        private readonly IRespExecutor _inner;
        private readonly object _sync = new();
        private List<PendingSend>? _pending = [];

        internal RespBatchExecutor(IRespExecutor inner)
            => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public int Database => _inner.Database;

        /// <summary>How many commands are waiting; for tests and diagnostics.</summary>
        internal int Count
        {
            get
            {
                lock (_sync) return _pending?.Count ?? 0;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Refused rather than forwarded.</b> A synchronous send must hand back a reply, and a batched
        /// command has not been sent yet - so there is no honest answer, and forwarding would quietly take
        /// the command out of the batch. The shipped <see cref="IBatch"/> has the same shape from the other
        /// direction: it inherits <see cref="IDatabaseAsync"/> and offers no synchronous commands at all.
        /// </remarks>
        public RespPayload Send(in RespRequest request) => throw new InvalidOperationException(
            "A batch has no synchronous send: nothing is sent until the batch is executed. "
            + "Use the asynchronous surface, or compose the command from a context without batching.");

        /// <inheritdoc/>
        /// <remarks>
        /// The returned task completes when the batch is executed and this command's reply arrives - so the
        /// caller's <c>await</c> is what makes a batched command look ordinary from the call site.
        /// </remarks>
        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            var pending = new PendingSend(request);
            lock (_sync)
            {
                var queue = _pending ?? throw AlreadyExecuted();
                queue.Add(pending);
            }

            return new ValueTask<RespPayload>(pending.Task);
        }

        /// <summary>Send everything queued, and complete each caller's task with its own reply.</summary>
        /// <remarks>
        /// <b>Every send is issued before any is awaited</b>, which is what makes the queue travel as a run
        /// rather than as a series of round trips. Completing them in order afterwards costs nothing - they
        /// are already in flight - and means a caller awaiting the third command cannot observe it finish
        /// before the second.
        /// </remarks>
        internal async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            List<PendingSend> queue;
            lock (_sync)
            {
                queue = _pending ?? throw AlreadyExecuted();
                _pending = null; // one shot: a second execute is a bug, not an empty flush
            }

            if (queue.Count == 0) return;

            var sends = new ValueTask<RespPayload>[queue.Count];
            for (var i = 0; i < queue.Count; i++)
            {
                var pending = queue[i];
                try
                {
                    sends[i] = _inner.SendAsync(pending.Request, cancellationToken);
                }
                catch (Exception ex)
                {
                    // a send that threw synchronously never produced a task to await; fault this one and
                    // carry on issuing the rest, so one bad command does not strand the others
                    pending.TrySetException(ex);
                    sends[i] = default;
                }
            }

            for (var i = 0; i < queue.Count; i++)
            {
                var pending = queue[i];
                if (pending.Task.IsCompleted) continue; // faulted above

                try
                {
                    pending.TrySetResult(await sends[i].ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    pending.TrySetException(ex);
                }
            }
        }

        /// <summary>Fault everything still queued, for a batch that is abandoned rather than executed.</summary>
        /// <remarks>
        /// <b>Not tidiness.</b> A caller awaiting a queued command holds the only reference to its rendered
        /// frame and releases it when that task completes; a batch that is simply dropped would leave every
        /// one of those awaits outstanding for ever, and the pooled buffers behind them with it.
        /// </remarks>
        internal void Abandon()
        {
            List<PendingSend>? queue;
            lock (_sync)
            {
                queue = _pending;
                _pending = null;
            }

            if (queue is null) return;
            foreach (var pending in queue)
            {
                pending.TrySetException(new InvalidOperationException(
                    "The batch was discarded without being executed, so this command was never sent."));
            }
        }

        private static InvalidOperationException AlreadyExecuted() => new(
            "This batch has already been executed; create another to send more commands.");

        /// <summary>One queued command: the frame to send, and the caller waiting for its reply.</summary>
        /// <remarks>
        /// <b>It <i>is</i> the completion source</b> rather than holding one, so a queued command costs a
        /// single allocation. <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> is
        /// load-bearing: without it, executing a batch of a thousand commands would run all thousand
        /// callers' continuations inline, one after another, on whichever thread happened to flush.
        /// </remarks>
        private sealed class PendingSend(RespRequest request) : TaskCompletionSource<RespPayload>(TaskCreationOptions.RunContinuationsAsynchronously)
        {
            internal RespRequest Request { get; } = request;
        }
    }
}
