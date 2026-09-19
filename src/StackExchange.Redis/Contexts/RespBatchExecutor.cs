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
    /// <b>This pipelines; it does not yet batch.</b> Executing issues every queued send before awaiting any
    /// of them, so they travel without waiting for each other - but they are separate messages, and
    /// another caller's command may land between two of them. The contiguity guarantee wants an executor
    /// overload that takes the whole run - <c>IRespPreambleExecutor</c> is already that shape with N=2,
    /// since <c>FramePairMessage</c> is an <c>IMultiMessage</c> and the bridge expands one inside the
    /// write lock. A batch may span bridges in cluster, though, which is why <c>RedisBatch.Execute</c>
    /// groups per bridge instead of being one message; see the queue for the decided shape.
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
                    pending.Fail(ex);
                    faulted ??= [];
                    faulted.Add(i);
                    sends[i] = default;
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
