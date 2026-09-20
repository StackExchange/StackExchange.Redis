using System;
using System.Threading.Tasks;

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
    /// <b>So it is paid for only when it is asked for.</b> A database with no async state - which is what
    /// <c>GetDatabase()</c> gives you - takes the plain <c>AsTask()</c> path and is unchanged. The new
    /// <c>RespDatabaseContext</c> surface returns <c>ValueTask</c> and allocates nothing at all; this
    /// exists for <see cref="IDatabaseAsync"/>, which returns <see cref="Task"/> and so was already
    /// allocating one object per command. The delta is one more object, on an opt-in path.
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
    internal static class TransitionalAsyncState
    {
        /// <summary>Convert to a task, carrying the database's async state when there is one.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="pending">The operation's result.</param>
        /// <param name="asyncState">The database's async state, or null.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// <b>Fire-and-forget deliberately carries no state</b>, which is what the shipped surface does:
        /// it hands back a stateless completed task, and <c>CombineFireAndForgetAndRegularAsyncInTransaction</c>
        /// asserts exactly that, one line away from asserting that the ordinary command does carry it.
        /// </remarks>
        internal static Task<T> AsTask<T>(this ValueTask<T> pending, object? asyncState, CommandFlags flags)
        {
            if (asyncState is null || (flags & CommandFlags.FireAndForget) != 0) return pending.AsTask();

            if (pending.IsCompletedSuccessfully)
            {
                // a synchronously-completed result - notably a client-side cache hit - needs no bridge
                var completed = new TaskCompletionSource<T>(asyncState);
                completed.SetResult(pending.Result);
                return completed.Task;
            }

            return Bridge(pending, asyncState);
        }

        /// <inheritdoc cref="AsTask{T}(ValueTask{T}, object?, CommandFlags)"/>
        internal static Task AsTask(this ValueTask pending, object? asyncState, CommandFlags flags)
        {
            if (asyncState is null || (flags & CommandFlags.FireAndForget) != 0) return pending.AsTask();
            if (pending.IsCompletedSuccessfully)
            {
                var completed = new TaskCompletionSource<bool>(asyncState);
                completed.SetResult(true);
                return completed.Task;
            }

            return Bridge(pending, asyncState);
        }

        /// <typeparam name="T">The result type.</typeparam>
        /// <remarks>
        /// <b>No <c>RunContinuationsAsynchronously</c>, and that is deliberate rather than an oversight.</b>
        /// The core already completes its operations with asynchronous continuations (see
        /// <c>docs/ThreadTheft.md</c>), so by the time this resumes the hop off the IO thread has already
        /// happened. Asking for a second one would add latency to buy a guarantee that is already held.
        /// </remarks>
        private static Task<T> Bridge<T>(ValueTask<T> pending, object asyncState)
        {
            var source = new TaskCompletionSource<T>(asyncState);
            _ = CompleteAsync(pending, source);
            return source.Task;

            static async Task CompleteAsync(ValueTask<T> pending, TaskCompletionSource<T> source)
            {
                try
                {
                    source.TrySetResult(await pending.ConfigureAwait(false));
                }
                catch (OperationCanceledException ex)
                {
                    // cancelled, not faulted: callers read TaskStatus.Canceled, and a transaction whose
                    // condition failed completes its queued commands exactly this way
                    source.TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    source.TrySetException(ex);
                }
            }
        }

        /// <inheritdoc cref="Bridge{T}(ValueTask{T}, object)"/>
        private static Task Bridge(ValueTask pending, object asyncState)
        {
            var source = new TaskCompletionSource<bool>(asyncState);
            _ = CompleteAsync(pending, source);
            return source.Task;

            static async Task CompleteAsync(ValueTask pending, TaskCompletionSource<bool> source)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                    source.TrySetResult(true);
                }
                catch (OperationCanceledException ex)
                {
                    source.TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    source.TrySetException(ex);
                }
            }
        }
    }
}
