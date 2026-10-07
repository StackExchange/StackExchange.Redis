using System;
using System.Diagnostics;
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
    internal static class SyncWait
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
                    var deadline = Deadline(TimeoutFor(multiplexer, executor, out var backstop));
                    do
                    {
                        if (!pump.RunUntilDone(Remaining(deadline)))
                        {
                            throw backstop ? MissedTimeout(multiplexer, pump) : new TimeoutException();
                        }
                    }
                    while (WasStale(pump, pending.IsCompleted) && !pending.IsCompleted);
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
                    var deadline = Deadline(TimeoutFor(multiplexer, null, out _));
                    do
                    {
                        if (!pump.RunUntilDone(Remaining(deadline))) throw new TimeoutException();
                    }
                    while (WasStale(pump, pending.IsCompleted) && !pending.IsCompleted);
                }

                pending.GetAwaiter().GetResult();
            }
            finally
            {
                SyncPump.Exit(pump);
            }
        }

        /// <summary>
        /// Whether the pump was woken by a signal that was not this call's - in which case it is cleared, so the
        /// caller re-checks its own operation and waits again.
        /// </summary>
        /// <param name="pump">The pump that reported done.</param>
        /// <param name="completed">Whether this call's operation has completed.</param>
        /// <remarks>
        /// <para>
        /// <b>"Done" is a hint, never the answer.</b> The pump is reused per thread, and a call that gave up - a
        /// timeout - leaves its <c>SignalDone</c> registered on an operation that is still running. When that
        /// operation finally completes, the signal lands on whichever call the thread is making NOW, which then
        /// left the pump and blocked in <c>GetResult</c> on its own operation, with no deadline at all. That was
        /// the CI hang: a synchronous <c>HashFieldExpireNoField</c> parked in <c>GetResult</c> for minutes, on a
        /// connection that was answering normally, after an earlier call on the same thread had timed out.
        /// </para>
        /// <para>
        /// The caller re-checks completion AFTER the flag is cleared, so a genuine signal arriving between the two
        /// cannot be lost: either the operation is already complete, or its signal is still to come.
        /// </para>
        /// </remarks>
        private static bool WasStale(SyncPump pump, bool completed)
        {
            if (completed) return false;
            pump.ClearDone();
            return true;
        }

        private static long Deadline(int timeoutMilliseconds)
            => timeoutMilliseconds == Timeout.Infinite
                ? long.MaxValue
                : Stopwatch.GetTimestamp() + (timeoutMilliseconds * Stopwatch.Frequency / 1000);

        private static int Remaining(long deadline)
        {
            if (deadline == long.MaxValue) return Timeout.Infinite;
            var remaining = (deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
            return remaining <= 0 ? 0 : (int)Math.Min(remaining, int.MaxValue);
        }

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
        private static RedisTimeoutException MissedTimeout(IConnectionMultiplexer multiplexer, SyncPump? pump)
        {
            var timeout = multiplexer.TimeoutMilliseconds;
            var message = $"A synchronous call was not timed out by its connection within {(timeout * 2) + BackstopSlackMilliseconds}ms, "
                + $"twice the configured {timeout}ms; the connection's own timeout should have fired first, so this is "
                + "a client fault worth reporting." + (pump is null ? string.Empty : " Pump: " + pump.Describe());
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
            if (pending.IsCompletedSuccessfully) return pending.GetAwaiter().GetResult();

            var task = pending.AsTask();

            // Stand back when the executor times itself out: its exception names the command, the endpoint
            // and why no connection was available, where the outer timer raises a bare TimeoutException
            // that says only that time passed. Racing them means the useful one usually loses.
            if (executor is { EnforcesTimeouts: true })
            {
                // ...but never without a limit: see TimeoutFor
                var limit = TimeoutFor(multiplexer, executor, out _);
                if (limit != Timeout.Infinite && !((IAsyncResult)task).AsyncWaitHandle.WaitOne(limit)) throw MissedTimeout(multiplexer, null);
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
