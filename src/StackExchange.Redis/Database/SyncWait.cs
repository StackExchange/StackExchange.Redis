using System;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
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
    /// <b>Mostly a fast path now.</b> The synchronous members send through a
    /// <see cref="RespContext.Blocking">blocking</see> context, whose sends wait on the calling thread and
    /// hand back a completed task, so this only takes the result. The slow path - blocking on a task - is
    /// for whatever still bypasses that: it is sync-over-async, and needs a pool thread to wake it.
    /// </para>
    /// </remarks>
    internal static class SyncWait
    {
        /// <summary>
        /// The multiplexer's timeout - or, where the executor times operations out itself, a <b>backstop</b>
        /// well beyond it, so that the executor's exception (which names the command, the endpoint and why)
        /// always gets to fire first.
        /// </summary>
        /// <remarks>
        /// <b>A backstop rather than no limit at all</b>, which is what this used to be. "The executor will time it
        /// out" is a promise about one mechanism, and a waiter that trusts it with an infinite wait turns any
        /// operation that escapes it into a hung caller: on CI a synchronous <c>HashFieldGetAndDelete</c> waited
        /// for over ten minutes on a connection that had stopped answering, while the async callers on the same
        /// connection were each timed out by the executor at five seconds, and the hang watchdog killed the
        /// whole test run. Twice the timeout plus a few heartbeats is far enough out never to race the real one.
        /// </remarks>
        private static int TimeoutFor(IConnectionMultiplexer multiplexer, RespExecutorBase? executor, out bool backstop)
        {
            var timeout = multiplexer.TimeoutMilliseconds;
            backstop = executor is { EnforcesTimeouts: true };
            if (!backstop || timeout < 0 || timeout >= (int.MaxValue - BackstopSlackMilliseconds) / 2) return backstop ? Timeout.Infinite : timeout;
            return (timeout * 2) + BackstopSlackMilliseconds;
        }

        private const int BackstopSlackMilliseconds = 5000;

        /// <summary>The executor's timeout should have fired and did not; say so, rather than report a plain timeout.</summary>
        private static RedisTimeoutException MissedTimeout(IConnectionMultiplexer multiplexer)
        {
            var timeout = multiplexer.TimeoutMilliseconds;
            var message = $"A synchronous call was not timed out by its connection within {(timeout * 2) + BackstopSlackMilliseconds}ms, "
                + $"twice the configured {timeout}ms; the connection's own timeout should have fired first, so this is "
                + "a client fault worth reporting.";
            return new(CommandFlags.CommandRetryNever, message, CommandStatus.Unknown);
        }

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
            // IsCompleted, not IsCompletedSuccessfully: a blocking send hands back its failures as completed
            // tasks too, and GetResult throws them here exactly as the task would have
            if (pending.IsCompleted) return pending.GetAwaiter().GetResult();

            var task = pending.AsTask();

            // Stand back when the executor times itself out: its exception names the command, the endpoint
            // and why no connection was available, where the outer timer raises a bare TimeoutException
            // that says only that time passed. Racing them means the useful one usually loses.
            if (executor is { EnforcesTimeouts: true })
            {
                // ...but never without a limit: see TimeoutFor
                var limit = TimeoutFor(multiplexer, executor, out _);
                if (limit != Timeout.Infinite && !((IAsyncResult)task).AsyncWaitHandle.WaitOne(limit)) throw MissedTimeout(multiplexer);
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
            if (pending.IsCompleted)
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
