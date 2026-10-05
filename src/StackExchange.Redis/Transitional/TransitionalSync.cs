using System;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Marks a synchronous call, so the operations it rents run their continuations on the calling thread.
    /// </summary>
    /// <remarks>
    /// The sync wrappers hold only a <see cref="System.Threading.Tasks.ValueTask"/>, which does not expose its
    /// operation, so the call is marked before the operation exists: <c>Begin</c> is an argument evaluated ahead
    /// of the async call (C# evaluates arguments left to right), and the matching <c>Wait</c> overload pumps and
    /// ends it. See <see cref="SyncPump"/>.
    /// </remarks>
    internal readonly struct SyncCall
    {
        private SyncCall(SyncPump pump) => Pump = pump;

        /// <summary>The call's pump.</summary>
        internal SyncPump? Pump { get; }

        /// <summary>Start a synchronous call on this thread.</summary>
        internal static SyncCall Begin() => new(SyncPump.Enter());
    }

    /// <summary>
    /// Blocks on an operation of the new core, for the synchronous half of a shipped interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Shared because the rule is subtle, not because the code is long.</b> A <see cref="ValueTask"/>
    /// backed by an <c>IValueTaskSource</c> must have its result consumed exactly once, and the consuming
    /// call is the one that looks droppable; a second copy of this would be a second place to rediscover
    /// that the hard way.
    /// </para>
    /// <para>
    /// The proper fix is routing rather than waiting: <c>RespExecutorBase</c> already has a synchronous
    /// <c>Send</c>, and <c>RespExecutor.Send</c> already uses it, so a context flag consulted by the one
    /// shared funnel would make every sync call complete inline and reduce this to its fast path. Worth
    /// doing when sync stops being deprioritised - not before.
    /// </para>
    /// </remarks>
    internal static class TransitionalSync
    {
        /// <summary>Wait for a synchronous call, running its continuations on this thread meanwhile.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="call">From <see cref="SyncCall.Begin"/>, evaluated before <paramref name="pending"/> was created.</param>
        /// <param name="pending">The operation to wait for.</param>
        /// <param name="multiplexer">Applies the configured timeout.</param>
        /// <param name="executor">The executor, when it enforces its own timeouts.</param>
        /// <returns>The result.</returns>
        internal static T Wait<T>(SyncCall call, ValueTask<T> pending, IConnectionMultiplexer multiplexer, RespExecutorBase? executor)
        {
            var pump = call.Pump;
            if (pump is null) return Wait(pending, multiplexer, executor);
            try
            {
                if (!pending.IsCompleted)
                {
                    pending.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(pump.SignalDone);
                    if (!pump.RunUntilDone(TimeoutFor(multiplexer, executor))) throw new TimeoutException();
                }

                return pending.GetAwaiter().GetResult();
            }
            finally
            {
                SyncPump.Exit(pump);
            }
        }

        /// <inheritdoc cref="Wait{T}(SyncCall, ValueTask{T}, IConnectionMultiplexer, RespExecutorBase)"/>
        /// <param name="call">From <see cref="SyncCall.Begin"/>, evaluated before <paramref name="pending"/> was created.</param>
        /// <param name="pending">The operation to wait for.</param>
        /// <param name="multiplexer">Applies the configured timeout.</param>
        internal static void Wait(SyncCall call, ValueTask pending, IConnectionMultiplexer multiplexer)
        {
            var pump = call.Pump;
            if (pump is null)
            {
                Wait(pending, multiplexer);
                return;
            }

            try
            {
                if (!pending.IsCompleted)
                {
                    pending.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(pump.SignalDone);
                    if (!pump.RunUntilDone(TimeoutFor(multiplexer, null))) throw new TimeoutException();
                }

                pending.GetAwaiter().GetResult();
            }
            finally
            {
                SyncPump.Exit(pump);
            }
        }

        /// <summary>
        /// The same limit the unpumped wait applies: none of our own where the executor times operations out
        /// itself (its exception says far more than a bare timeout), and the multiplexer's otherwise.
        /// </summary>
        private static int TimeoutFor(IConnectionMultiplexer multiplexer, RespExecutorBase? executor)
            => executor is { EnforcesTimeouts: true } ? Timeout.Infinite : multiplexer.TimeoutMilliseconds;

        /// <summary>Wait for a result.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="pending">The operation to wait for.</param>
        /// <param name="multiplexer">Applies the configured timeout, where one is applied here at all.</param>
        /// <param name="executor">Whoever will run it; asked whether it times itself out.</param>
        internal static T Wait<T>(ValueTask<T> pending, IConnectionMultiplexer multiplexer, RespExecutorBase? executor)
        {
            // spelled the same way as the result-less overload below, deliberately: .Result would also
            // consume (it calls IValueTaskSource<T>.GetResult(_token)), but only a reader who already
            // knows that can tell - and the rule is the same rule, so it should look the same
            if (pending.IsCompletedSuccessfully) return pending.GetAwaiter().GetResult();

            var task = pending.AsTask();

            // Stand back when the executor times itself out: its exception names the command, the endpoint
            // and why no connection was available, where the outer timer raises a bare TimeoutException
            // that says only that time passed. Racing them means the useful one usually loses.
            if (executor is { EnforcesTimeouts: true })
            {
                #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
                return task.GetAwaiter().GetResult();
                #pragma warning restore SER308
            }

            #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
            return multiplexer.Wait(task);
            #pragma warning restore SER308
        }

        /// <inheritdoc cref="Wait{T}(ValueTask{T}, IConnectionMultiplexer, RespExecutorBase)"/>
        /// <param name="pending">The operation to wait for.</param>
        /// <param name="multiplexer">Applies the configured timeout.</param>
        internal static void Wait(ValueTask pending, IConnectionMultiplexer multiplexer)
        {
            if (pending.IsCompletedSuccessfully)
            {
                // NOT a no-op, and not optional. A ValueTask backed by an IValueTaskSource must have its
                // result consumed exactly once: GetResult(_token) is what lets the source complete its
                // lifecycle and be reset or returned to its pool. Observing IsCompletedSuccessfully and
                // returning would abandon it - the pooled source is never released, and the next operation
                // to borrow it can see a stale token. There is no value to take here, which is precisely
                // why it looks droppable and is not.
                pending.GetAwaiter().GetResult();
                return;
            }

            #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
            multiplexer.Wait(pending.AsTask()); // AsTask consumes the source too, so the branches stay exclusive
            #pragma warning restore SER308
        }
    }
}
