using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
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
         * - must have single lock-token-holder (mutex)
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
            lock (_queue)
            {
                // double-checked!
                if (token != _currentToken)
                {
                    if (demandMatch) ThrowInvalidLockHolder();
                    return;
                }

                //  while we have the lock; see if we can wake someone up
                if (_queue.Count != 0)
                {
                    // generate a new token
                    _currentToken = 0;
                    var newToken = TryTakeInsideLock();
                    Debug.Assert(newToken.Success);

                    do
                    {
                        // try to hand that new lock to a recipient
                        var next = _queue.Dequeue();
                        if (next.TrySetAndActivate(_scheduler, newToken)) return; // so we don't clear the token
                    }
                    while (_queue.Count != 0);
                }

                // nobody wants it; release it
                _currentToken = 0;
            }
        }
        private void DequeueExpired()
        {
            lock (_queue)
            {
                while (_queue.Count != 0)
                {
                    var next = _queue.Peek();
                    var remaining = UpdateTimeOut(next.Start, _timeoutMilliseconds);
                    if (remaining == 0)
                    {
                        // tell them that they failed
                        _queue.Dequeue().TrySetAndActivate(_scheduler, default);
                    }
                    else
                    {
                        break;
                    }
                }
                if (_queue.Count != 0) SetNextAsyncTimeoutInsideLock();
            }
        }

        private uint _timeoutStart;
        private CancellationTokenSource _timeoutCancel;
        private void SetNextAsyncTimeoutInsideLock()
        {
            uint nextItemStart;
            if (_queue.Count != 0 || (nextItemStart = _queue.Peek().Start) == _timeoutStart)
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
                _scheduler.Schedule(s => ((WaitAwaitMutex)s).DequeueExpired(), this);
            }
            else
            {
                // take a peek back in a little while, kthx
                var cts = new CancellationTokenSource();
                var timeout = Task.Delay(_timeoutMilliseconds, cts.Token);
                timeout.ContinueWith((_, state) =>
                {
                    try { ((WaitAwaitMutex)state).DequeueExpired(); } catch { }
                }, this);
            }
        }

        private volatile int _currentToken;
        private int _nextToken;


        private LockToken TryTakeInsideLock()
        {
            if (_currentToken == 0)
            {
                // increment the token and record it
                int next = ++_nextToken;
                if (next == 0) next++; // avoiding the zero sentinel
                _currentToken = next;
                return new LockToken(this, next);
            }
            return default;
        }

        private LockToken TryTakeWithoutCompetition()
        {
            // see if it is immediately available without blocking
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_queue, 0, ref lockTaken);
                return lockTaken ? TryTakeInsideLock() : default;
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_queue);
            }
        }

        LockToken TryTakeBySpinning()
        {
            // try a SpinWait to see if we can avoid the expensive bits
            SpinWait spin = new SpinWait();
            while (!spin.NextSpinWillYield)
            {
                spin.SpinOnce();
                if (_currentToken == 0)
                {
                    var winner = TryTakeWithoutCompetition();
                    if (winner.Success) return winner;
                }
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

        LockToken TakeWithTimeout()
        {
            bool queueLockTaken = false, itemLockTaken = false;
            var start = GetTime();

            var item = GetPerThreadLockObject();
            try
            {
                // we want to have the item-lock *before* we put anything in the queue
                Monitor.TryEnter(item, 0, ref itemLockTaken);
                if (!itemLockTaken)
                {
                    // this should have been available immediately; if it isn't, something
                    // is very wrong; we can try again, though
                    item = GetNewPerThreadLockObject();
                    Monitor.TryEnter(item, 0, ref itemLockTaken);
                    Debug.Assert(itemLockTaken);
                    if (!itemLockTaken) return default; // just give up!
                }

                // now lock the global queue; have a final stab at getting it cheaply,
                Monitor.TryEnter(_queue, _timeoutMilliseconds, ref queueLockTaken);
                if (!queueLockTaken) return default; // couldn't even get the lock, let alone the mutex
                var token = TryTakeInsideLock();
                if (token.Success) return token;
                item.ResetInsideLock();

                // otherwise enqueue the pending item, and release
                // the global queue *before* we wait
                _queue.Enqueue(QueueItem.CreateSync(start, item));
                Monitor.Exit(_queue);
                queueLockTaken = false;

                // k, the item is now in the queue; we're going to
                // wait to see if it gets pulsed; note: we're not
                // going to depend on the result here - the value
                // inside the object is the single source of truth here
                // because otherwise we could get a race condition where it
                // gets a token *just after* the Wait times out, which
                // could lead to a dropped token, and a blocked mux
                Monitor.Wait(item, UpdateTimeOut(start, _timeoutMilliseconds));

                // keep in mind that we have the item lock here
                return item.GetResultInsideLock();
            }
            finally
            {
                if (queueLockTaken) Monitor.Exit(_queue);
                if (itemLockTaken) Monitor.Exit(item);
            }
        }

        AwaitableLockToken TakeWithTimeoutAsync()
        {
            bool queueLockTaken = false;
            var start = GetTime();

            QueueItem asyncItem;
            try
            {
                // lock the global queue; then have a final stab at getting it cheaply,
                Monitor.TryEnter(_queue, _timeoutMilliseconds, ref queueLockTaken);
                if (!queueLockTaken) return default; // couldn't even get the lock, let alone the mutex
                var token = TryTakeInsideLock();
                if (token.Success) return new AwaitableLockToken(token);

                // otherwise enqueue the pending item, and release
                // the global queue
                asyncItem = QueueItem.CreateAsync(start);
                _queue.Enqueue(asyncItem);

                if (_queue.Count == 1) SetNextAsyncTimeoutInsideLock();
            }
            finally
            {
                if (queueLockTaken) Monitor.Exit(_queue);
            }
            return asyncItem.ForAwait();
        }

        public readonly struct AwaitableLockToken : INotifyCompletion, ICriticalNotifyCompletion
        {
            private readonly AsyncLockToken _pending;
            private readonly LockToken _token;
            internal AwaitableLockToken(LockToken token)
            {
                _token = token;
                _pending = default;
            }
            internal AwaitableLockToken(AsyncLockToken pending)
            {
                _token = default;
                _pending = pending;
            }
            public LockToken GetResult() => _pending?.GetResult() ?? _token;

            public AwaitableLockToken GetAwaiter() => this;

            public void OnCompleted(Action continuation)
            {
                if (continuation != null)
                {
                    if (_pending == null) continuation(); // already complete; invoke directly
                    else _pending.OnCompleted(continuation);
                }
            }

            public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

            public bool IsCompleted => _pending?.IsCompleted() ?? true;
        }

        internal abstract class PendingLockToken
        {
            public abstract bool TrySetResult(PipeScheduler scheduler, LockToken token);
        }
        internal sealed class AsyncLockToken : PendingLockToken
        {
            private LockToken _token;
            private Action _continuation;
            private bool _isComplete;

            public LockToken GetResult()
            {
                lock (this)
                {
                    if (!_isComplete) ThrowIncomplete();
                    return _token;
                }
                void ThrowIncomplete() => throw new InvalidOperationException("GetResult cannot be used until the operation has completed");
            }

            public bool IsCompleted()
            {
                lock (this) { return _isComplete; }
            }

            public override bool TrySetResult(PipeScheduler scheduler, LockToken token)
            {
                Action callback;
                lock (this)
                {
                    if (_isComplete) return false;
                    _token = token;
                    callback = _continuation;
                    _continuation = null;
                    _isComplete = true;
                }
                if (callback != null)
                {
                    scheduler.Schedule(s => ((Action)s).Invoke(), callback);
                }
                return true;
            }

            internal void OnCompleted(Action continuation)
            {
                if (continuation == null) return; // nothing to do
                lock (this)
                {
                    if (!_isComplete)
                    {
                        _continuation += continuation;
                        return;
                    }
                }
                // will only get here if was already complete, because of the return
                continuation.Invoke();
            }
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        public AwaitableLockToken WaitAsync()
        {
            LockToken token;
            if (_currentToken == 0)
            {
                token = TryTakeWithoutCompetition();
                if (token.Success) return new AwaitableLockToken(token);
            }

            token = TryTakeBySpinning();
            if (token.Success) return new AwaitableLockToken(token);

            // if the caller is impatient (zero-timeout) and we haven't
            // got it already: give up; else - wait!
            return _timeoutMilliseconds == 0 ? default : TakeWithTimeoutAsync();
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        public LockToken Wait()
        {
            LockToken token;
            if (_currentToken == 0)
            {
                token = TryTakeWithoutCompetition();
                if (token.Success) return token;
            }

            token = TryTakeBySpinning();
            if (token.Success) return token;

            // if the caller is impatient (zero-timeout) and we haven't
            // got it already: give up; else - wait!
            return _timeoutMilliseconds == 0 ? default : TakeWithTimeout();
        }

        private readonly Queue<QueueItem> _queue = new Queue<QueueItem>();


        internal sealed class SyncLockToken : PendingLockToken
        {
            private LockToken _token;

            private bool _isWaiting;
            public override bool TrySetResult(PipeScheduler scheduler, LockToken token)
            {
                lock (this)
                {
                    if (_isWaiting)
                    {
                        _token = token;
                        Monitor.Pulse(this);
                        return true;
                    }
                    return false;
                }
            }
            internal void ResetInsideLock()
            {
                _token = default;
                _isWaiting = true;
            }

            internal LockToken GetResultInsideLock()
            {
                var val = _token;
                _token = default;
                _isWaiting = false;
                return val;
            }
        }
        [ThreadStatic]
        static SyncLockToken _perThreadLockObject;
        private static SyncLockToken GetPerThreadLockObject() => _perThreadLockObject ?? GetNewPerThreadLockObject();
        private static SyncLockToken GetNewPerThreadLockObject() => _perThreadLockObject = new SyncLockToken();


        private readonly struct QueueItem
        {
            private readonly PendingLockToken _source;

            public uint Start { get; }

            public AwaitableLockToken ForAwait() => new AwaitableLockToken((AsyncLockToken)_source);

            public static QueueItem CreateAsync(uint start) => new QueueItem(start, new AsyncLockToken());

            public static QueueItem CreateSync(uint start, SyncLockToken token) => new QueueItem(start, token);

            internal bool TrySetAndActivate(PipeScheduler scheduler, LockToken token) => _source.TrySetResult(scheduler, token);

            public QueueItem(uint start, PendingLockToken source)
            {
                Start = start;
                _source = source;
            }

        }

    }
}
