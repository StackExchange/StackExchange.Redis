using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RESPite.Operations;

namespace StackExchange.Redis
{
    /// <summary>
    /// Runs a synchronous call's continuations on the calling thread while it waits, rather than on the pool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why.</b> A synchronous call blocks on an operation whose continuations go to the thread-pool, so it
    /// wakes only when a pool thread runs them - under a saturated pool (the case <c>DedicatedThreads</c> is
    /// for, and .NET Framework, which does not inject threads for blocking waits) the reply has arrived and the
    /// caller still cannot move. v3 woke its synchronous waiters from the reader. Doing the same here by running
    /// continuations on the reader deadlocks - a continuation can send, a send can wait on backpressure that only
    /// the reader can relieve - so they run on the blocked caller's thread instead, which is idle and is the
    /// thread the work belongs to. See <see cref="IContinuationSink"/>.
    /// </para>
    /// <para>
    /// <b>One per thread, reused,</b> so a synchronous call costs no allocation; a generation number tells calls
    /// apart, and a continuation posted for an earlier call is declined (and goes to the pool) rather than run in
    /// the middle of a later one. A synchronous call made from inside a pumped continuation gets a pump of its own.
    /// </para>
    /// <para>
    /// <b>Two phases, and the split is what makes it safe.</b> Between <c>Enter</c> and the wait, operations the
    /// call rents are only CAPTURED (and told to keep their continuation); nothing is attached, because if the
    /// async call throws before the wait, nothing will ever end the call - and an attached, open pump would then
    /// swallow the continuations of every later operation on this thread, which hung connects across the suite
    /// the first time. Only the wait attaches, inside a <c>finally</c> that closes the pump; operations rented
    /// while it pumps (a composite command's next step) attach as they are rented.
    /// </para>
    /// </remarks>
    internal sealed class SyncPump : IContinuationSink
    {
        [ThreadStatic]
        private static SyncPump? t_spare;

        [ThreadStatic]
        private static SyncPump? t_current;

        private readonly object _sync = new();
        private Queue<WorkItem>? _queue;
        private long _generation;
        private bool _open;
        private bool _done;
        private bool _pumping;
        private bool _running;
        private SyncPump? _outer;

        // what the call rented before it waited; a small ring, because a call that never reaches its wait (it threw)
        // leaves capture on until the next call on this thread, and must not grow anything in the meantime
        private const int CaptureCapacity = 8;
        private readonly RespPayloadOperation?[] _captured = new RespPayloadOperation?[CaptureCapacity];
        private readonly short[] _capturedTokens = new short[CaptureCapacity];
        private int _capturedCount;

        private readonly struct WorkItem(Action<object?> continuation, object? state)
        {
            public void Run() => continuation(state);
        }

        /// <summary>The pump of the synchronous call in progress on this thread, if any.</summary>
        internal static SyncPump? Current => t_current;

        /// <summary>An operation was rented on this thread during this call.</summary>
        internal void OnRented(RespPayloadOperation operation)
        {
            // NOT while running a pumped continuation: if that code waits synchronously for what it rents (a wait
            // that is not itself a SyncCall), attaching would queue the reply's continuation behind the very item
            // that is blocked on it - the classic synchronization-context deadlock. Such an operation completes
            // through the pool, as it always did.
            if (_running) return;
            operation.Interpose();
            var token = operation.Token;
            if (_pumping)
            {
                operation.TryAttachSink(token, this, Generation); // already waiting: claim it now
                return;
            }

            var slot = _capturedCount++ % CaptureCapacity;
            _captured[slot] = operation;
            _capturedTokens[slot] = token;
        }

        /// <summary>The generation operations rented now should attach under.</summary>
        internal long Generation => Volatile.Read(ref _generation);

        /// <summary>Start a synchronous call on this thread.</summary>
        internal static SyncPump Enter()
        {
            // a pump that is PUMPING is in use further up this stack (a sync call inside a pumped continuation):
            // that one needs a pump of its own. One that is merely capturing was left by a call that never
            // reached its wait, and is simply restarted
            var outer = t_current is { _pumping: true } busy ? busy : null;
            var pump = outer is null ? (t_spare ??= new SyncPump()) : new SyncPump();
            lock (pump._sync)
            {
                pump._generation++;
                pump._open = false; // not until the wait: see the type remarks
                pump._done = false;
                pump._outer = outer;
            }

            Array.Clear(pump._captured, 0, CaptureCapacity);
            pump._capturedCount = 0;

            t_current = pump;
            return pump;
        }

        /// <summary>End the synchronous call: decline further posts, and hand anything still queued to the pool.</summary>
        internal static void Exit(SyncPump pump)
        {
            Queue<WorkItem>? leftover = null;
            pump._pumping = false;
            pump._running = false;
            Array.Clear(pump._captured, 0, CaptureCapacity);
            pump._capturedCount = 0;
            lock (pump._sync)
            {
                pump._open = false;
                if (pump._queue is { Count: > 0 } queue)
                {
                    leftover = new Queue<WorkItem>(queue);
                    queue.Clear();
                }
            }

            if (ReferenceEquals(t_current, pump)) t_current = pump._outer;
            pump._outer = null;

            if (leftover is not null)
            {
                foreach (var item in leftover) ThreadPool.UnsafeQueueUserWorkItem(static s => ((WorkItemBox)s!).Item.Run(), new WorkItemBox(item));
            }
        }

        private sealed class WorkItemBox(WorkItem item)
        {
            public WorkItem Item { get; } = item;
        }

        /// <inheritdoc/>
        public bool TryPost(long generation, Action<object?> continuation, object? state)
        {
            lock (_sync)
            {
                if (!_open || generation != _generation) return false;
                (_queue ??= new Queue<WorkItem>()).Enqueue(new WorkItem(continuation, state));
                Monitor.Pulse(_sync);
                return true;
            }
        }

        /// <summary>Called when the awaited operation completes, from whichever thread completed it.</summary>
        internal readonly Action SignalDone;

        private SyncPump() => SignalDone = OnDone;

        private void OnDone()
        {
            lock (_sync)
            {
                _done = true;
                Monitor.Pulse(_sync);
            }
        }

        /// <summary>What the pump was doing, for the exception a missed timeout raises.</summary>
        internal string Describe()
        {
            lock (_sync)
            {
                return $"open={_open}, done={_done}, pumping={_pumping}, running={_running}, queued={_queue?.Count ?? 0}, "
                    + $"captured={Math.Min(_capturedCount, CaptureCapacity)}, generation={_generation}, nested={_outer is not null}";
            }
        }

        /// <summary>Run posted continuations on this thread until the call is done or the timeout passes.</summary>
        /// <param name="timeoutMilliseconds">The limit, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns>Whether the call completed in time.</returns>
        internal bool RunUntilDone(int timeoutMilliseconds)
        {
            // open, then claim what was rented: from here a posted continuation will be run, and Exit is certain
            lock (_sync) _open = true;
            _pumping = true;
            var generation = Generation;
            var count = Math.Min(_capturedCount, CaptureCapacity);
            for (var i = 0; i < count; i++)
            {
                _captured[i]?.TryAttachSink(_capturedTokens[i], this, generation);
            }

            var deadline = timeoutMilliseconds == Timeout.Infinite ? long.MaxValue : Stopwatch.GetTimestamp() + (timeoutMilliseconds * Stopwatch.Frequency / 1000);
            while (true)
            {
                WorkItem item;
                lock (_sync)
                {
                    while ((_queue is null || _queue.Count == 0) && !_done)
                    {
                        int wait;
                        if (deadline == long.MaxValue)
                        {
                            wait = Timeout.Infinite;
                        }
                        else
                        {
                            var remaining = (deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
                            if (remaining <= 0) return false;
                            wait = (int)Math.Min(remaining, int.MaxValue);
                        }

                        Monitor.Wait(_sync, wait);
                    }

                    if (_queue is null || _queue.Count == 0) return true; // done, and nothing left to run
                    item = _queue.Dequeue();
                }

                _running = true;
                try
                {
                    item.Run(); // outside the lock: it may post more, or complete the call
                }
                finally
                {
                    _running = false;
                }
            }
        }
    }
}
