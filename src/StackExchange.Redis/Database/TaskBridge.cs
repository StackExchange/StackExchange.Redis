using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace StackExchange.Redis
{
    /// <summary>
    /// Carries <c>IRedisAsync.AsyncState</c> onto the tasks the new core hands back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This cannot come from the operation, and that is a property of the BCL rather than of this
    /// design.</b> <see cref="Task.AsyncState"/> is set when a <see cref="Task"/> is constructed and is
    /// immutable afterwards; <c>ValueTask&lt;T&gt;.AsTask()</c> exposes no state parameter, so a task
    /// backed by an <c>IValueTaskSource</c> always reports <see langword="null"/>. The only sanctioned
    /// way to get a task that carries state is to allocate one that was born with it -
    /// <c>new TaskCompletionSource&lt;T&gt;(state)</c>.
    /// </para>
    /// <para>
    /// <b>So state costs one object.</b> A pending command on our own operation, without state, gets the
    /// operation's own promise task (<c>RespPayloadOperation&lt;T&gt;.AsTask</c>) - one object; with state it gets a
    /// completion source born with it - two. Either way the bridge is also what marks a fault observed (see
    /// <c>Bridge</c>), which <c>ValueTask.AsTask()</c> cannot do. A completed result allocates nothing without
    /// state. The new <c>RespDatabaseContext</c> surface returns <c>ValueTask</c> and needs none of this; it
    /// exists for <see cref="IDatabaseAsync"/>, which returns <see cref="Task"/>.
    /// </para>
    /// <para>
    /// <b>Do not be tempted to stamp the state onto whatever <c>AsTask()</c> returns.</b> It is reachable
    /// - <c>UnsafeAccessor</c> can write <c>Task.m_stateObject</c>, and this library already uses that
    /// technique in <c>Delegates.cs</c> - and for a <i>pending</i> source it would even be correct and
    /// cheaper, since the task returned is freshly allocated and nobody else holds it. But for an
    /// already-completed value <c>AsTask()</c> may hand back the shared <c>Task.FromResult</c> cache
    /// singleton, and stamping that corrupts an instance the entire process shares. There is no reliable
    /// way to ask whether the task you were given is yours, so the completed path has to allocate
    /// regardless - which is most of the saving gone. Left as a possible optimisation behind this one
    /// helper, rather than spread across ~270 call sites.
    /// </para>
    /// </remarks>
    internal static class TaskBridge
    {
        /// <summary>Convert to a task, carrying the database's async state when there is one.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="pending">The operation's result.</param>
        /// <param name="asyncState">The database's async state, or null.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// <para>
        /// <b>Fire-and-forget deliberately carries no state</b>, which is what the shipped surface does:
        /// it hands back a stateless completed task, and <c>CombineFireAndForgetAndRegularAsyncInTransaction</c>
        /// asserts exactly that, one line away from asserting that the ordinary command does carry it.
        /// </para>
        /// <para>
        /// <b>A pending result is never handed out as <c>ValueTask.AsTask()</c></b>, even without state: that task
        /// belongs to the runtime, and a fault on it that the caller drops raises
        /// <see cref="TaskScheduler.UnobservedTaskException"/> - which v3 never did, because it marked every faulted
        /// command task observed as it faulted. See <see cref="Bridge{T}"/>.
        /// </para>
        /// </remarks>
        internal static Task<T> AsTask<T>(this ValueTask<T> pending, object? asyncState, CommandFlags flags)
        {
            if ((flags & CommandFlags.FireAndForget) != 0) asyncState = null;

            if (pending.IsCompletedSuccessfully)
            {
                // a synchronously-completed result - notably a client-side cache hit - needs no bridge; .Result
                // consumes the source, as it must be consumed exactly once
                var result = pending.Result;
                if (asyncState is null) return Task.FromResult(result);
                var completed = new TaskCompletionSource<T>(asyncState);
                completed.SetResult(result);
                return completed.Task;
            }

            // our own operation, as most sends are: register on it with a static callback - no delegate per command
            if (RespPayloadOperation<T>.TryTakeDispatched(pending, out var token) is { } operation)
            {
                // without state, the operation's own promise task - one object, no completion source
                return asyncState is null ? operation.AsTask(token) : new SourceBridge<T>(operation, token, asyncState).Task;
            }

            return new Bridge<T>(pending, asyncState).Task;
        }

        /// <inheritdoc cref="AsTask{T}(ValueTask{T}, object?, CommandFlags)"/>
        internal static Task AsTask(this ValueTask pending, object? asyncState, CommandFlags flags)
        {
            if ((flags & CommandFlags.FireAndForget) != 0) asyncState = null;

            if (pending.IsCompletedSuccessfully)
            {
                pending.GetAwaiter().GetResult(); // consumes the source; see AsTask<T>
                if (asyncState is null) return Task.CompletedTask;
                var completed = new TaskCompletionSource<bool>(asyncState);
                completed.SetResult(true);
                return completed.Task;
            }

            if (RespPayloadOperation<bool>.TryTakeDispatched(pending, out var token) is { } operation)
            {
                return asyncState is null ? operation.AsTask(token) : new SourceVoidBridge(operation, token, asyncState).Task;
            }

            return new VoidBridge(pending, asyncState).Task;
        }

        /// <summary>
        /// <see cref="Bridge{T}"/>, registered on the source itself rather than through the task's awaiter.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <remarks>
        /// Identical in outcome - result, cancellation, a fault marked observed as it faults - and one object
        /// cheaper: the awaiter takes an <see cref="Action"/>, which bound to this instance is a delegate per
        /// command, where the source takes a static callback and this instance as its state.
        /// </remarks>
        private sealed class SourceBridge<T> : TaskCompletionSource<T>
        {
            private static readonly Action<object?> s_onCompleted = static state => ((SourceBridge<T>)state!).OnCompleted();

            private readonly IValueTaskSource<T> _source;
            private readonly short _token;

            internal SourceBridge(IValueTaskSource<T> source, short token, object? asyncState) : base(asyncState)
            {
                _source = source;
                _token = token;

                // already settled - faulted under FailFast, say - is answered NOW, as Bridge answers it
                if (source.GetStatus(token) != ValueTaskSourceStatus.Pending) OnCompleted();
                else source.OnCompleted(s_onCompleted, this, token, ValueTaskSourceOnCompletedFlags.None);
            }

            private void OnCompleted()
            {
                try
                {
                    TrySetResult(_source.GetResult(_token));
                }
                catch (OperationCanceledException ex)
                {
                    TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    TrySetException(ex);
                    _ = Task.Exception; // observed; see Bridge
                }
            }
        }

        /// <summary><see cref="SourceBridge{T}"/> for a command whose only result is that it succeeded.</summary>
        private sealed class SourceVoidBridge : TaskCompletionSource<bool>
        {
            private static readonly Action<object?> s_onCompleted = static state => ((SourceVoidBridge)state!).OnCompleted();

            private readonly IValueTaskSource _source;
            private readonly short _token;

            internal SourceVoidBridge(IValueTaskSource source, short token, object? asyncState) : base(asyncState)
            {
                _source = source;
                _token = token;
                if (source.GetStatus(token) != ValueTaskSourceStatus.Pending) OnCompleted();
                else source.OnCompleted(s_onCompleted, this, token, ValueTaskSourceOnCompletedFlags.None);
            }

            private void OnCompleted()
            {
                try
                {
                    _source.GetResult(_token);
                    TrySetResult(true);
                }
                catch (OperationCanceledException ex)
                {
                    TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    TrySetException(ex);
                    _ = Task.Exception;
                }
            }
        }

        /// <summary>
        /// A task of our own over a pending <see cref="ValueTask{TResult}"/>: born with the async state, and marking
        /// a fault observed as it faults.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <remarks>
        /// <para>
        /// <b>Observed, not swallowed.</b> Reading <see cref="Task.Exception"/> marks the fault handled, so a caller
        /// that drops the task never sees <see cref="TaskScheduler.UnobservedTaskException"/> - and down-level, with
        /// <c>ThrowUnobservedTaskExceptions</c> enabled, is not taken down by it. A caller who awaits still gets the
        /// exception. This is v3's <c>TaskResultBox</c> behaviour; its <c>GC.SuppressFinalize(task)</c> is not
        /// copied, because the finalizer is on the task's internal exception holder, not on <see cref="Task"/>.
        /// </para>
        /// <para>
        /// <b>Attached straight to the source</b> rather than through an <c>async</c> helper: one object where the
        /// state machine cost two, and no <c>RunContinuationsAsynchronously</c>, because the core already completes
        /// its operations off the IO thread (see <c>docs/ThreadTheft.md</c>).
        /// </para>
        /// </remarks>
        private sealed class Bridge<T> : TaskCompletionSource<T>
        {
            private readonly ConfiguredValueTaskAwaitable<T>.ConfiguredValueTaskAwaiter _awaiter;

            internal Bridge(ValueTask<T> pending, object? asyncState) : base(asyncState)
            {
                _awaiter = pending.ConfigureAwait(false).GetAwaiter();

                // already faulted or cancelled - "no connection" under FailFast, say - is answered NOW, as
                // ValueTask.AsTask() answers it: registering would hop to the pool first, and a caller checking
                // IsFaulted straight away (AsyncTasksReportFailureIfServerUnavailable) would see it still running
                if (_awaiter.IsCompleted) OnCompleted();
                else _awaiter.UnsafeOnCompleted(OnCompleted);
            }

            private void OnCompleted()
            {
                try
                {
                    TrySetResult(_awaiter.GetResult());
                }
                catch (OperationCanceledException ex)
                {
                    // cancelled, not faulted: callers read TaskStatus.Canceled, and a transaction whose
                    // condition failed completes its queued commands exactly this way
                    TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    TrySetException(ex);
                    _ = Task.Exception; // observed; see the remarks
                }
            }
        }

        /// <inheritdoc cref="Bridge{T}"/>
        private sealed class VoidBridge : TaskCompletionSource<bool>
        {
            private readonly ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter _awaiter;

            internal VoidBridge(ValueTask pending, object? asyncState) : base(asyncState)
            {
                _awaiter = pending.ConfigureAwait(false).GetAwaiter();

                // already faulted or cancelled - "no connection" under FailFast, say - is answered NOW, as
                // ValueTask.AsTask() answers it: registering would hop to the pool first, and a caller checking
                // IsFaulted straight away (AsyncTasksReportFailureIfServerUnavailable) would see it still running
                if (_awaiter.IsCompleted) OnCompleted();
                else _awaiter.UnsafeOnCompleted(OnCompleted);
            }

            private void OnCompleted()
            {
                try
                {
                    _awaiter.GetResult();
                    TrySetResult(true);
                }
                catch (OperationCanceledException ex)
                {
                    TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    TrySetException(ex);
                    _ = Task.Exception;
                }
            }
        }
    }
}
