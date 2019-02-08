using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis
{
    internal sealed class WaitAwaitMutex
    {
        private readonly int _timeoutMilliseconds;
        private readonly PipeScheduler _scheduler;

        public int TimeoutMilliseconds => _timeoutMilliseconds;

        /// <summary>
        /// Create a new WaitAwaitMutex instance
        /// </summary>
        /// <param name = "timeoutMilliseconds" > Time to wait, in milliseconds - or non-positive for immediate-only</param>
        /// <param name="scheduler">The scheduler to use for async continuations</param>
        public WaitAwaitMutex(PipeScheduler scheduler, int timeoutMilliseconds)
        {
            if (timeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            _timeoutMilliseconds = timeoutMilliseconds;
            _scheduler = scheduler ?? DedicatedThreadPoolPipeScheduler.Default;
        }

        /*
         * - must have single conch-holder (mutex)
         * - must be waitable (sync)
         * - must be awaitable (async)
         * - must allow fully async consumer
         *   ("wait" and "release" can be from different threads)
         * - must not suck when using both sync+async callers
         *   (I'm looking at you, SemaphoreSlim... you know what you did)
         * - must be low allocation
         * - should allow control of the threading model for async callback
         * - fairness would be nice, but is not a hard demand
         * - a "using"-style API is a nice-to-have, to avoid try/finally
         * - for this application, timeout doesn't need to be per-call
         */

        public readonly struct LockToken : IDisposable
        {
            private readonly WaitAwaitMutex _parent;
            private readonly int _token;

            /// <summary>
            /// Indicates whether the mutex was successfully taken
            /// </summary>
            public bool Success => _token != 0;

            /// <summary>
            /// Indicates whether the mutex is still actually held; this is
            /// more expensive than checking whether it was originally taken
            /// </summary>
            public bool IsValid() => (_token != 0 & _parent != null) // note deliberate unusual short-circuit
                && _parent.IsValid(_token);

            internal LockToken(WaitAwaitMutex parent, int token)
            {
                _parent = parent;
                _token = token;
            }
            internal void ReleaseWithoutFail()
            {
                if (_token != 0) _parent.Release(_token, demandMatch: false);
            }
            public void Dispose()
            {
                if (_token != 0) _parent.Release(_token, demandMatch: true);
            }
        }

        private bool IsValid(int token) => _currentToken == token;

        static void ThrowInvalidLockHolder() => throw new InvalidOperationException("Attempt to release a WaitAwaitMutex that was not held");
        private void Release(int token, bool demandMatch = true)
        {
            // we can check for wrongness without needing the lock, note
            if (token != _currentToken)
            {
                if (demandMatch) ThrowInvalidLockHolder();
                return;
            }

            // it doesn't matter how long it takes to acquire this; we really want it
            lock (_syncLock)
            {
                // double-checked!
                if (token != _currentToken)
                {
                    if (demandMatch) ThrowInvalidLockHolder();
                    return;
                }

                if (_asyncQueue.Count != 0)
                {
                    _scheduler.Schedule(s => ((WaitAwaitMutex)s).DequeueOne(), this);
                }

                _currentToken = 0; // release it and wake up the next blocked thread
                if (_syncWaiters != 0) Monitor.Pulse(_syncLock);
            }
        }

        private void PurgeQueueInsideLock()
        {
            AsyncItem next;
            while (_asyncQueue.Count != 0)
            {
                next = _asyncQueue.Peek();
                var remaining = UpdateTimeOut(next.Start, _timeoutMilliseconds);
                if (remaining == 0)
                {
                    // burn it, sorry
                    _asyncQueue.Dequeue().Timeout(_scheduler);
                }
            }
        }
        private void DequeueOne()
        {
            AsyncItem next;
            LockToken conch = default;
            try
            {

                lock (_syncLock)
                {
                    // first, clear down any dead items
                    PurgeQueueInsideLock();

                    if (_asyncQueue.Count == 0) return;

                    conch = TryTakeInsideLock();
                    if (!conch.Success) return;

                    next = _asyncQueue.Dequeue();
                    SetNextAsyncTimeoutInsideLock();
                }
                if (next.Reactivate(conch)) { conch = default; } // to prevent release
            }
            finally
            {
                conch.ReleaseWithoutFail();
            }
        }

        private uint _timeoutStart;
        private CancellationTokenSource _timeoutCancel;
        private void SetNextAsyncTimeoutInsideLock()
        {
            uint nextItemStart;
            if (_asyncQueue.Count != 0 || (nextItemStart = _asyncQueue.Peek().Start) == _timeoutStart)
            {   // nothing more to do, or the timeout hasn't changed (so: don't change anything)
                return;
            }
            if (_timeoutCancel != null)
            {
                try { _timeoutCancel.Cancel(); } catch { }
                _timeoutCancel = null;
            }

            _timeoutStart = nextItemStart;
            var localTimeout = UpdateTimeOut(nextItemStart, _timeoutMilliseconds);
            if (localTimeout == 0)
            {   // take a peek back right away (just... not on this thread)
                _scheduler.Schedule(s => ((WaitAwaitMutex)s).DequeueOne(), this);
            }
            else
            {
                // take a peek back in a little while, kthx
                var cts = new CancellationTokenSource();
                var timeout = Task.Delay(_timeoutMilliseconds, cts.Token);
                timeout.ContinueWith((_, state) => {
                    try { ((WaitAwaitMutex)state).DequeueOne(); } catch { }
                }, this);
            }
        }

        private readonly object _syncLock = new object();

        private volatile int _currentToken;
        private int _nextToken;


        private LockToken TryTakeInsideLock()
        {
            if (_currentToken == 0)
            {
                // increment the token and record it, avoiding the zero sentinel
                while ((_currentToken = ++_nextToken) == 0) { }
                return new LockToken(this, _currentToken);
            }
            return default;
        }

        private LockToken TryTakeWithoutCompetition()
        {
            // see if it is immediately available without blocking
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_syncLock, 0, ref lockTaken);
                return lockTaken ? TryTakeInsideLock() : default;
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_syncLock);
            }
        }

        LockToken TryTakeBySpinning()
        {
            // try a SpinWait to see if we can avoid the expensive bits
            SpinWait spin = new SpinWait();
            while (!spin.NextSpinWillYield)
            {
                if (_currentToken == 0)
                {
                    var winner = TryTakeWithoutCompetition();
                    if (winner.Success) return winner;
                }
                spin.SpinOnce();
            }
            return default;
        }

        public bool IsTaken => _currentToken != 0;

        private static uint GetTime() => (uint)Environment.TickCount;
        // borrowed from SpinWait
        private static int UpdateTimeOut(uint startTime, int originalWaitMillisecondsTimeout)
        {
            uint elapsedMilliseconds = (GetTime() - startTime);

            // Check the elapsed milliseconds is greater than max int because this property is uint
            if (elapsedMilliseconds > int.MaxValue)
            {
                return 0;
            }

            // Subtract the elapsed time from the current wait time
            int currentWaitTimeout = originalWaitMillisecondsTimeout - (int)elapsedMilliseconds;
            if (currentWaitTimeout <= 0)
            {
                return 0;
            }

            return currentWaitTimeout;
        }

        int _syncWaiters;
        LockToken TakeWithTimeout()
        {
            bool lockTaken = false;
            try
            {
                var start = GetTime();
                Monitor.TryEnter(_syncLock, _timeoutMilliseconds, ref lockTaken);
                if (!lockTaken) return default; // couldn't even get the lock, let alone the conch

                _syncWaiters++;
                var conch = TryTakeInsideLock();
                if (conch.Success) return conch;

                // keep hoping for a pulse
                while (Monitor.Wait(_syncLock, UpdateTimeOut(start, _timeoutMilliseconds)))
                {
                    conch = TryTakeInsideLock();
                    if (conch.Success) return conch;
                }
                return default;
            }
            finally
            {
                if (lockTaken)
                {
                    _syncWaiters--;
                    Monitor.Exit(_syncLock);
                }
            }
        }

        ValueTask<LockToken> TakeWithTimeoutAsync()
        {
            bool lockTaken = false;
            try
            {
                var start = GetTime();
                Monitor.TryEnter(_syncLock, _timeoutMilliseconds, ref lockTaken);
                if (!lockTaken) return default; // couldn't even get the lock, let alone the conch

                var conch = TryTakeInsideLock();
                if (conch.Success) return new ValueTask<LockToken>(conch);

                var pending = AsyncItem.Create(start);
                _asyncQueue.Enqueue(pending);
                if (_asyncQueue.Count == 1) SetNextAsyncTimeoutInsideLock();
                return new ValueTask<LockToken>(pending.Task);
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(_syncLock);
                }
            }
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        ValueTask<LockToken> WaitAsync()
        {
            LockToken conch;
            if (_currentToken == 0)
            {
                conch = TryTakeWithoutCompetition();
                if (conch.Success) return new ValueTask<LockToken>(conch);
            }

            conch = TryTakeBySpinning();
            if (conch.Success) return new ValueTask<LockToken>(conch);

            // if the caller is impatient (zero-timeout) and we haven't
            // got it already: give up; else - wait!
            return _timeoutMilliseconds == 0
                ? new ValueTask<LockToken>(default(LockToken))
                : TakeWithTimeoutAsync();
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        public LockToken Wait()
        {
            LockToken conch;
            if (_currentToken == 0)
            {
                conch = TryTakeWithoutCompetition();
                if (conch.Success) return conch;
            }

            conch = TryTakeBySpinning();
            if (conch.Success) return conch;

            // if the caller is impatient (zero-timeout) and we haven't
            // got it already: give up; else - wait!
            return _timeoutMilliseconds == 0 ? default : TakeWithTimeout();
        }

        private readonly Queue<AsyncItem> _asyncQueue = new Queue<AsyncItem>();

        private readonly struct AsyncItem
        {
            private readonly TaskCompletionSource<LockToken> _source;
            private readonly uint _start;
            public uint Start => _start;
            public Task<LockToken> Task => _source.Task;
            public static AsyncItem Create(uint start)
                => new AsyncItem(start, new TaskCompletionSource<LockToken>(TaskCreationOptions.None));

            internal void Timeout(PipeScheduler scheduler) // use the scheduler to callback a "nope, you didn't get it"
                => scheduler.Schedule(s => ((TaskCompletionSource<LockToken>)s).TrySetResult(default), _source);

            internal bool Reactivate(LockToken conch) => _source.TrySetResult(conch);

            public AsyncItem(uint start, TaskCompletionSource<LockToken> source)
            {
                _start = start;
                _source = source;
            }

        }

    }
}
