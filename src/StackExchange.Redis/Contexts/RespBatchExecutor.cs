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
    /// <b>A command nobody is awaiting costs neither.</b> Fire-and-forget answers a default
    /// <see cref="ValueTask{TResult}"/> at once - already completed, carrying a null payload, which is what
    /// <c>RespExecutor.Parse</c> turns into <c>default(T)</c> - so there is no promise to make and no task
    /// to allocate. <see cref="PendingForget"/> exists only to hold the frame until the run goes out.
    /// </para>
    /// <para>
    /// <b>Which is also the one place this has to take a reference of its own.</b> The rule everything else
    /// here relies on is that an executor may use a request until the task it returned completes, and the
    /// caller holds its reference until then - so an awaited batched send needs nothing, because its task
    /// completes after the bytes were written. Answering <i>early</i> is exactly what breaks that: the
    /// caller's <c>finally</c> disposes while the frame is still queued. So the fire-and-forget entry
    /// retains, and releases when the batch is done with it.
    /// </para>
    /// <para>
    /// <b>It batches when the executor underneath can, and pipelines when it cannot.</b> An
    /// <see cref="IRespRunExecutor"/> writes the whole queue as one run - consecutive, on one connection,
    /// with nothing of anybody else's between them - which is the guarantee the shipped <see cref="IBatch"/>
    /// gives. Without one, the commands are issued individually before any is awaited: they still travel
    /// without waiting for each other, but another caller's command may land between two of them.
    /// </para>
    /// <para>
    /// <b>A run must resolve to a single slot</b>, because one connection means one server - so a cluster
    /// batch is split by slot and written as a run per group. That needs no server selection: a request
    /// already carries the slot it folded while being written, and one is only folded when the context is
    /// a cluster, so outside cluster everything is <c>NoSlot</c> and there is exactly one group. Grouping
    /// by <i>server</i> would give fewer, larger runs and would race a reshard; see <see cref="Dispatch"/>.
    /// </para>
    /// </remarks>
    internal sealed class RespBatchExecutor : IRespExecutor
    {
        private readonly IRespExecutor _inner;
        private readonly object _sync = new();
        private List<IPendingSend>? _pending = [];

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
            // fire-and-forget: answer now, and take a reference so the frame outlives the answer
            if ((request.Flags & CommandFlags.FireAndForget) != 0 && request.TryRetain(out var retained))
            {
                Enqueue(new PendingForget(retained));
                return default; // completed, null payload - which Parse turns into default(T)
            }

            var pending = new PendingSend(request);
            Enqueue(pending);
            return new ValueTask<RespPayload>(pending.Task);

            void Enqueue(IPendingSend entry)
            {
                lock (_sync)
                {
                    var queue = _pending;
                    if (queue is null)
                    {
                        entry.Release(); // hand back anything we took before failing
                        throw AlreadyExecuted();
                    }

                    queue.Add(entry);
                }
            }
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
            List<IPendingSend> queue;
            lock (_sync)
            {
                queue = _pending ?? throw AlreadyExecuted();
                _pending = null; // one shot: a second execute is a bug, not an empty flush
            }

            if (queue.Count == 0) return;

            // the indexes whose send threw before producing a task; almost always none, so the list is
            // not built unless one does
            List<int>? faulted = null;
            var sends = new ValueTask<RespPayload>[queue.Count];

            if (_inner is IRespRunExecutor runner)
            {
                try
                {
                    Dispatch(runner, queue, sends, cancellationToken);
                }
                catch (Exception ex)
                {
                    // a run is written as a unit, so it fails as one - there is no half-sent state to
                    // reconcile, and every caller gets the same answer
                    foreach (var pending in queue) pending.Fail(ex);
                    foreach (var pending in queue) pending.Release();
                    return;
                }
            }
            else
            {
                // no run support: send them one at a time, which pipelines but does not guarantee that
                // another caller's command will not land between two of ours
                for (var i = 0; i < queue.Count; i++)
                {
                    var pending = queue[i];
                    try
                    {
                        sends[i] = _inner.SendAsync(pending.Request, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // a send that threw synchronously never produced a task to await; fault this one
                        // and carry on issuing the rest, so one bad command does not strand the others
                        pending.Fail(ex);
                        faulted ??= [];
                        faulted.Add(i);
                        sends[i] = default;
                    }
                }
            }

            try
            {
                for (var i = 0; i < queue.Count; i++)
                {
                    if (faulted is not null && faulted.Contains(i)) continue;

                    var pending = queue[i];
                    try
                    {
                        pending.Complete(await sends[i].ConfigureAwait(false));
                    }
                    catch (Exception ex)
                    {
                        pending.Fail(ex);
                    }
                }
            }
            finally
            {
                // whatever happened, give back every reference this batch took of its own
                foreach (var pending in queue) pending.Release();
            }
        }

        /// <summary>Write the queue as runs - one per slot, which is usually one in total.</summary>
        /// <remarks>
        /// <para>
        /// <b>Grouping by slot needs no server selection, which is why it can happen here.</b> A request
        /// already carries the slot it folded while it was being written, and
        /// <c>RespRequestBuilder</c> only folds one <i>when the context is a cluster</i> - so outside
        /// cluster every request is <c>NoSlot</c>, they all fall in one group, and this costs a comparison
        /// per command and nothing else.
        /// </para>
        /// <para>
        /// <b>Slot rather than node, and node would be wrong rather than merely harder.</b> Grouping by
        /// server gets fewer, larger runs - which is what the shipped <c>RedisBatch.Execute</c> does, per
        /// bridge - but it races a <b>reshard</b>: the slot-to-node map can move between the grouping and
        /// the write, and a group assembled for one node is then a run that no longer belongs to it. A
        /// slot is a property of the keys rather than of the topology, so a group built from slots stays
        /// true however the cluster rearranges itself underneath. Splitting more finely is the price, and
        /// it is the cheap half of the trade.
        /// </para>
        /// <para>
        /// <b>Ordering holds within a slot, not across slots</b> - which is the shipped guarantee too: a
        /// batch spanning bridges has never promised an order between them. Commands that must be ordered
        /// relative to each other touch the same keys, and so share a slot.
        /// </para>
        /// </remarks>
        private static void Dispatch(
            IRespRunExecutor runner,
            List<IPendingSend> queue,
            ValueTask<RespPayload>[] sends,
            CancellationToken cancellationToken)
        {
            if (IsSingleSlot(queue))
            {
                var run = new RespRequest[queue.Count];
                for (var i = 0; i < queue.Count; i++) run[i] = queue[i].Request;
                _ = runner.SendAsync(run, sends, cancellationToken);
                return;
            }

            // a cluster batch touching more than one slot: one run per slot, and the replies scattered
            // back into the caller's positions so completion order stays queue order
            var bySlot = new Dictionary<int, List<int>>();
            for (var i = 0; i < queue.Count; i++)
            {
                var slot = queue[i].Request.Slot;
                if (!bySlot.TryGetValue(slot, out var indexes)) bySlot.Add(slot, indexes = []);
                indexes.Add(i);
            }

            foreach (var group in bySlot.Values)
            {
                var run = new RespRequest[group.Count];
                for (var j = 0; j < group.Count; j++) run[j] = queue[group[j]].Request;

                var replies = new ValueTask<RespPayload>[group.Count];
                _ = runner.SendAsync(run, replies, cancellationToken);

                for (var j = 0; j < group.Count; j++) sends[group[j]] = replies[j];
            }
        }

        /// <summary>Whether the whole queue resolves to one slot, which is always so outside cluster.</summary>
        private static bool IsSingleSlot(List<IPendingSend> queue)
        {
            var slot = queue[0].Request.Slot;
            for (var i = 1; i < queue.Count; i++)
            {
                if (queue[i].Request.Slot != slot) return false;
            }

            return true;
        }

        /// <summary>Fault everything still queued, for a batch that is abandoned rather than executed.</summary>
        /// <remarks>
        /// <b>Not tidiness.</b> A caller awaiting a queued command holds the only reference to its rendered
        /// frame and releases it when that task completes; a batch that is simply dropped would leave every
        /// one of those awaits outstanding for ever, and the pooled buffers behind them with it.
        /// </remarks>
        internal void Abandon()
        {
            List<IPendingSend>? queue;
            lock (_sync)
            {
                queue = _pending;
                _pending = null;
            }

            if (queue is null) return;
            var fault = new InvalidOperationException(
                "The batch was discarded without being executed, so this command was never sent.");
            foreach (var pending in queue)
            {
                pending.Fail(fault);
                pending.Release();
            }
        }

        private static InvalidOperationException AlreadyExecuted() => new(
            "This batch has already been executed; create another to send more commands.");

        /// <summary>One queued command: the frame to send, and whatever is waiting for its reply.</summary>
        /// <remarks>
        /// <b>Two implementations, because fire-and-forget is not a degenerate case of waiting - it is the
        /// absence of it.</b> Completing a promise nobody holds still costs the promise; declining to make
        /// one costs nothing.
        /// </remarks>
        private interface IPendingSend
        {
            /// <summary>The frame to send when the batch is executed.</summary>
            RespRequest Request { get; }

            /// <summary>Hand the reply to whoever asked for it.</summary>
            void Complete(RespPayload? payload);

            /// <summary>Report that this command did not happen.</summary>
            void Fail(Exception fault);

            /// <summary>Give back any reference this entry took of its own.</summary>
            void Release();
        }

        /// <summary>A queued command somebody is awaiting.</summary>
        /// <remarks>
        /// <para>
        /// <b>It <i>is</i> the completion source</b> rather than holding one, so a queued command costs a
        /// single allocation. <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> is
        /// load-bearing: without it, executing a batch of a thousand commands would run all thousand
        /// callers' continuations inline, one after another, on whichever thread happened to flush.
        /// </para>
        /// <para>
        /// <b>Releases nothing</b>: the caller's own reference covers the frame, because its <c>await</c>
        /// does not finish until this completes. That is the ordinary contract, and the reason this layer
        /// is nearly free.
        /// </para>
        /// </remarks>
        private sealed class PendingSend(RespRequest request)
            : TaskCompletionSource<RespPayload>(TaskCreationOptions.RunContinuationsAsynchronously), IPendingSend
        {
            public RespRequest Request { get; } = request;

            public void Complete(RespPayload? payload) => TrySetResult(payload!);

            public void Fail(Exception fault) => TrySetException(fault);

            public void Release()
            {
            }
        }

        /// <summary>A queued command nobody is awaiting.</summary>
        /// <remarks>
        /// <para>
        /// <b>No completion source and no task</b>: <c>SendAsync</c> answers a default
        /// <see cref="ValueTask{TResult}"/>, which is already completed and carries a null payload - and a
        /// null payload is what <c>RespExecutor.Parse</c> turns into <c>default(T)</c>, which is what
        /// fire-and-forget has always returned. So a fire-and-forget command in a batch allocates only
        /// this entry, and would allocate nothing at all if the entry were pooled.
        /// </para>
        /// <para>
        /// <b>And this is the one place the batch must take a reference of its own.</b> Completing the
        /// caller's task at queue time is exactly what breaks the contract the rest of this relies on: the
        /// caller's <c>finally</c> disposes its reference immediately, while the frame is still sitting in
        /// the queue waiting to be sent. So the entry retains, and releases when the batch is done with it.
        /// Marc's original instinct about an increment belongs here and nowhere else.
        /// </para>
        /// <para>
        /// A borrowed request cannot be retained, and one that fails to is queued the ordinary way instead
        /// - the caller waits for execute, which is slower than it needed to be but never wrong.
        /// </para>
        /// </remarks>
        private sealed class PendingForget(RespRequest retained) : IPendingSend
        {
            public RespRequest Request { get; } = retained;

            /// <remarks>Nobody asked, so there is nobody to tell - which is what the flag means.</remarks>
            public void Complete(RespPayload? payload)
            {
            }

            /// <inheritdoc cref="Complete"/>
            /// <remarks>
            /// Including a failure: fire-and-forget declines the <i>outcome</i>, not just the value, and
            /// the shipped path has the same shape - a message with no task has nowhere to put a fault.
            /// </remarks>
            public void Fail(Exception fault)
            {
            }

            public void Release() => Request.Dispose();
        }
    }
}
