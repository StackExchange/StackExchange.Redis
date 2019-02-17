using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// A mutex primitive that can be waited or awaited, with support for schedulers
    /// </summary>
    public sealed class WaitAwaitMutex
    {
        /*
         * - must have single lock-token-holder (mutex)
         * - must be waitable (sync)
         * - must be awaitable (async)
         * - async context does not flow
         *   (not required; think ConfigureAwait(false))
         * - must allow fully async consumer
         *   ("wait" and "release" can be from different threads)
         * - must not suck when using both sync+async callers
         *   (I'm looking at you, SemaphoreSlim... you know what you did)
         * - must be low allocation
         * - should allow control of the threading model for async callback
         * - fairness would be nice, but is not a hard demand
         * - timeout support is required
         *   value can be per mutex - doesn't need to be per-Wait[Async]
         * - a "using"-style API is a nice-to-have, to avoid try/finally
         * - we won't even *attempt* to detect re-entrancy
         *   (if you try and take a lock that you have, that's your fault)
         *
         * - sync path uses per-thread ([ThreadStatic])/Monitor pulse for comms
         * - async path uses custom awaitable with zero-alloc on immediate win
         */

        /* usage:

                using (var token = mutex.TryWait())
                {
                    if (token) {...}
                }

        or

                using (var token = await mutex.TryWaitAsync())
                {
                    if (token) {...}
                }
        */

        private readonly PipeScheduler _scheduler;

        /// <summary>
        /// Time to wait, in milliseconds - or zero for immediate-only
        /// </summary>
        public int TimeoutMilliseconds { get; }

        /// <summary>
        /// Create a new WaitAwaitMutex instance
        /// </summary>
        /// <param name = "timeoutMilliseconds">Time to wait, in milliseconds - or zero for immediate-only</param>
        /// <param name="scheduler">The scheduler to use for async continuations, or the thread-pool if omitted</param>
        public WaitAwaitMutex(int timeoutMilliseconds, PipeScheduler scheduler = null)
        {
            if (timeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            TimeoutMilliseconds = timeoutMilliseconds;
            _scheduler = scheduler ?? PipeScheduler.ThreadPool;
        }

        /// <summary>
        /// The result of a Wait/WaitAsync operation on WaitAwaitMutex; the caller *must* check Success to see whether the mutex was obtained
        /// </summary>
        public readonly struct LockToken : IDisposable
        {
            /// <summary>
            /// Indicates whether the mutex was successfully taken
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator true(LockToken token) => (token._token & LockState.MaskState) == LockState.Success;
            /// <summary>
            /// Indicates whether the mutex was successfully taken
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator false(LockToken token) => (token._token & LockState.MaskState) != LockState.Success;

            private readonly WaitAwaitMutex _parent;
            private readonly int _token;

            /// <summary>
            /// Indicates whether the mutex was successfully taken
            /// </summary>
            public bool Success => (_token & LockState.MaskState) == LockState.Success;

            /// <summary>
            /// Indicates whether the mutex was canceled
            /// </summary>
            public bool Canceled => (_token & LockState.MaskState) == LockState.Canceled;

            internal LockToken(WaitAwaitMutex parent, int token)
            {
                _parent = parent;
                _token = token;
            }

            /// <summary>
            /// Release the mutex, if obtained
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                if (Success) _parent.Release(_token, demandMatch: true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Release(int token, bool demandMatch = true)
        {
            void ThrowInvalidLockHolder() => throw new InvalidOperationException("Attempt to release a WaitAwaitMutex that was not held");

            // release the token (we can check for wrongness without needing the lock, note)
            if (Interlocked.CompareExchange(ref _token, (token & ~LockState.MaskState) | LockState.Pending, token) != token)
            {
                if (demandMatch) ThrowInvalidLockHolder();
                return;
            }

            if (_mayHavePendingItems) ActivateNextQueueItem();

        }
        private void ActivateNextQueueItem()
        {
            // see if we can nudge the next waiter
            lock (_queue)
            {
                try
                {
                    int token; // if work to do, try and get a new token
                    if (_queue.Count == 0 || (token = TryTake()) == 0) return;

                    while (_queue.Count != 0)
                    {
                        var next = _queue.Dequeue();
                        if (next is AsyncPendingLockToken) _pendingAsyncOperations--;
                        if (next.TrySetResult(_scheduler, token)) return; // so we don't release the token
                    }

                    // nobody actually wanted it; return it
                    Volatile.Write(ref _token, (token & ~LockState.MaskState) | LockState.Pending);
                }
                finally
                {
                    FixMayHavePendingItemsInsideLock();
                }
            }
        }
        private void DequeueExpired()
        {
            lock (_queue)
            {
                while (_queue.Count != 0)
                {
                    var next = _queue.Peek();
                    var remaining = UpdateTimeOut(next.Start, TimeoutMilliseconds);
                    if (remaining == 0)
                    {
                        // tell them that they failed
                        _queue.Dequeue();
                        if (next is AsyncPendingLockToken) _pendingAsyncOperations--;
                        next.TrySetResult(_scheduler, default);
                    }
                    else
                    {
                        break;
                    }
                }
                if (_pendingAsyncOperations != 0) SetNextAsyncTimeoutInsideLock();
            }
        }

        private uint _timeoutStart;
        private CancellationTokenSource _timeoutCancel;
        private void SetNextAsyncTimeoutInsideLock()
        {
            void CancelExistingTimeout()
            {
                if (_timeoutCancel != null)
                {
                    try { _timeoutCancel.Cancel(); } catch { }
                    try { _timeoutCancel.Dispose(); } catch { }
                    _timeoutCancel = null;
                }
            }
            uint nextItemStart;
            if (_pendingAsyncOperations == 0)
            {
                CancelExistingTimeout();
                return;
            }
            if ((nextItemStart = _queue.Peek().Start) == _timeoutStart)
            {   // timeout hasn't changed (so: don't change anything)
                return;
            }

            // something has changed
            CancelExistingTimeout();

            _timeoutStart = nextItemStart;
            var localTimeout = UpdateTimeOut(nextItemStart, TimeoutMilliseconds);
            if (localTimeout == 0)
            {   // take a peek back right away (just... not on this thread)
                _scheduler.Schedule(s => ((WaitAwaitMutex)s).DequeueExpired(), this);
            }
            else
            {
                // take a peek back in a little while, kthx
                var cts = new CancellationTokenSource();
                var timeout = Task.Delay(localTimeout, cts.Token);
                timeout.ContinueWith((_, state) =>
                {
                    try { ((WaitAwaitMutex)state).DequeueExpired(); } catch { }
                }, this);
                _timeoutCancel = cts;
            }
        }

        private int _token;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetNextToken(int token)
            // 2 low bits are status; 30 high bits are counter
            => (int)((((uint)token << 2) + 1) >> 2) | LockState.Success;

        private static class LockState
        {   // using this as a glorified enum; can't use enum directly because
            // or Interlocked etc support
            public const int
                Pending = 0,
                Success = 1,
                Failure = 2,
                Canceled = 3,

                MaskState = 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TryTake()
        {
            int current = Volatile.Read(ref _token), next;
            return ((current & LockState.MaskState) == LockState.Pending
                && Interlocked.CompareExchange(ref _token, next = GetNextToken(current), current) == current)
                ? next : 0;
        }

        private int TryTakeBySpinning()
        {
            // try a SpinWait to see if we can avoid the expensive bits
            SpinWait spin = new SpinWait();
            do
            {
                spin.SpinOnce();
                var token = TryTake();
                if (token != 0) return token;
            } while (!spin.NextSpinWillYield);
            return default;
        }

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

        private volatile bool _mayHavePendingItems;
        private void FixMayHavePendingItemsInsideLock() => _mayHavePendingItems = _queue.Count != 0;

        private int TakeWithTimeout()
        {
            // try and spin
            var token = TryTakeBySpinning();
            if (token != 0) return token;

            // if "now or never", bail
            if (TimeoutMilliseconds == 0) return default;

            bool itemLockTaken = false, queueLockTaken = false;
            var start = GetTime();

            var item = GetPerThreadLockObject();
            try
            {
                // we want to have the item-lock *before* we put anything in the queue
                // (and we only want to do that once we've checked we can reset it)
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

                item.Reset(start);

                // now lock the global queue, and then have a final stab at getting it cheaply
                _mayHavePendingItems = true; // set this *before* getting the lock
                Monitor.TryEnter(_queue, TimeoutMilliseconds, ref queueLockTaken);
                if (!queueLockTaken) return default; // couldn't even get the lock, let alone the mutex

                token = TryTake();
                if (token != 0)
                {
                    FixMayHavePendingItemsInsideLock();
                    return token;
                }

                // otherwise enqueue the pending item, and release
                // the global queue *before* we wait
                _queue.Enqueue(item);
                Monitor.Exit(_queue);
                queueLockTaken = false;

                // k, the item is now in the queue; we're going to
                // wait to see if it gets pulsed; note: we're not
                // going to depend on the result here - the value
                // inside the object is the single source of truth here
                // because otherwise we could get a race condition where it
                // gets a token *just after* the Wait times out, which
                // could lead to a dropped token, and a blocked mux
                Monitor.Wait(item, UpdateTimeOut(start, TimeoutMilliseconds));
                Monitor.Exit(item);
                itemLockTaken = false;

                var result = item.GetResult();
                if ((result & LockState.MaskState) != LockState.Success)
                {
                    // if we *didn't* get the lock, we *could* still be in the queue;
                    // since we're in the failure path, let's take a moment to see if we can
                    // remove ourselves from the queue; otherwise we need to consider the
                    // lock object tainted
                    // (note the outer finally will release the queue lock either way)
                    Monitor.TryEnter(_queue, 0, ref queueLockTaken);
                    if (queueLockTaken)
                    {
                        if (_queue.Count == 0) { } // nothing to do; queue is empty
                        else if (_queue.Peek() == item)
                        {
                            _queue.Dequeue(); // we were next and we cleaned up; nice!
                        }
                        else if (_queue.Count == 1) { } // only one item and it isn't us: nothing to do
                        else
                        {
                            // we *might* be later in the queue, but we can't check or be sure,
                            // and we don't want the sync object getting treated oddly; nuke it
                            ResetPerThreadLockObject();
                        }
                        FixMayHavePendingItemsInsideLock();
                    }
                    else // we didn't get the queue lock; no idea whether we're still in the queue
                    {
                        ResetPerThreadLockObject();
                    }
                }
                return result;
            }
            finally
            {
                if (queueLockTaken) Monitor.Exit(_queue);
                if (itemLockTaken) Monitor.Exit(item);
            }
        }

        int _pendingAsyncOperations;
        AwaitableLockToken TakeWithTimeoutAsync()
        {
            // try and spin
            var token = TryTakeBySpinning();
            if (token != 0) return new AwaitableLockToken(new LockToken(this, token));

            // if "now or never", bail
            if (TimeoutMilliseconds == 0) return default;

            var start = GetTime();

            // lock the global queue; then have a final stab at getting it cheaply
            bool queueLockTaken = false;
            try
            {
                _mayHavePendingItems = true; // set this *before* getting the lock
                Monitor.TryEnter(_queue, TimeoutMilliseconds, ref queueLockTaken);
                if (!queueLockTaken) return default; // couldn't even get the lock, let alone the mutex

                token = TryTake();
                if (token != 0)
                {
                    FixMayHavePendingItemsInsideLock();
                    return new AwaitableLockToken(new LockToken(this, token));
                }

                // otherwise enqueue the pending item, and release
                // the global queue
                var asyncItem = new AsyncPendingLockToken(this, start);
                _queue.Enqueue(asyncItem);
                if (_pendingAsyncOperations++ == 0) SetNextAsyncTimeoutInsideLock(); // first async op

                return new AwaitableLockToken(asyncItem);
            }
            finally
            {
                if (queueLockTaken) Monitor.Exit(_queue);
            }
        }

        /// <summary>
        /// Custom awaitable result from WaitAsync on WaitAwaitMutex
        /// </summary>
        public readonly struct AwaitableLockToken : INotifyCompletion, ICriticalNotifyCompletion
        {
            private readonly AsyncPendingLockToken _pending;
            private readonly LockToken _token;
            internal AwaitableLockToken(LockToken token)
            {
                _token = token;
                _pending = default;
            }
            internal AwaitableLockToken(AsyncPendingLockToken pending)
            {
                _token = default;
                _pending = pending;
            }

            /// <summary>
            /// Obtain the LockToken after completion of this async operation
            /// </summary>
            public LockToken GetResult() => _pending?.GetResultAsToken() ?? _token;

            /// <summary>
            /// Obtain the awaiter associated with this awaitable result
            /// </summary>
            public AwaitableLockToken GetAwaiter() => this;

            /// <summary>
            /// Schedule a continuation to be invoked after completion of this async operation
            /// </summary>
            public void OnCompleted(Action continuation)
            {
                if (continuation != null)
                {
                    if (_pending == null) continuation(); // already complete; invoke directly
                    else _pending.OnCompleted(continuation);
                }
            }

            /// <summary>
            /// Schedule a continuation to be invoked after completion of this async operation
            /// </summary>
            public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

            /// <summary>
            /// Indicates whether this awaitable result has completed
            /// </summary>
            public bool IsCompleted => _pending?.IsCompleted() ?? true;

            /// <summary>
            /// Indicates whether this awaitable result completed without an asynchronous step
            /// </summary>
            public bool CompletedSynchronously => _pending == null;
        }

        internal abstract class PendingLockToken
        {
            public void Reset(uint start)
            {
                Start = start;
                Volatile.Write(ref _token, 0);
            }

            protected PendingLockToken() { }
            protected PendingLockToken(uint start)
            {
                Start = start;
            }

            public bool TrySetResult(PipeScheduler scheduler, int token)
            {
                int oldValue = Volatile.Read(ref _token);
                if ((oldValue & LockState.MaskState) == LockState.Pending
                    && Interlocked.CompareExchange(ref _token, token, oldValue) == oldValue)
                {
                    OnAssigned(scheduler);
                    return true;
                }
                return false;
            }

            private int _token; // combined state and counter

            public uint Start { get; private set; }

            // if already complete: returns the token; otherwise, dooms the operation
            public int GetResult()
            {
                int oldValue, newValue;
                do
                {
                    oldValue = Volatile.Read(ref _token);
                    if ((oldValue & LockState.MaskState) == LockState.Pending)
                    {
                        // doom it
                        newValue = (oldValue & ~LockState.MaskState) | LockState.Failure;
                    }
                    else
                    {
                        // everything else: that's already fixed, just return it
                        return oldValue;
                    }
                    // try to update the doomed value; if something changes: redo from start
                } while (Interlocked.CompareExchange(ref _token, newValue, oldValue) != oldValue);
                return newValue;
            }

            public bool IsCompleted() => (Volatile.Read(ref _token) & LockState.MaskState) != LockState.Pending;

            protected abstract void OnAssigned(PipeScheduler scheduler);
        }
        internal sealed class AsyncPendingLockToken : PendingLockToken
        {
            private Action _continuation;
            private readonly WaitAwaitMutex _mutex;

            public AsyncPendingLockToken(WaitAwaitMutex mutex, uint start) : base(start)
                => _mutex = mutex;

            protected override void OnAssigned(PipeScheduler scheduler)
            {
                var callback = Interlocked.Exchange(ref _continuation, null);
                if (callback != null)
                {
                    scheduler.Schedule(s => ((Action)s).Invoke(), callback);
                }
            }

            internal void OnCompleted(Action continuation)
            {
                if (continuation == null) return; // nothing to do

                if (IsCompleted())
                {
                    continuation.Invoke();
                    return;
                }

                if (Interlocked.CompareExchange(ref _continuation, continuation, null) != null)
                {
                    ThrowNotSupported();
                }
                void ThrowNotSupported() => throw new NotSupportedException($"Only one pending continuation is permitted"); ;
            }

            internal LockToken GetResultAsToken() => new LockToken(_mutex, GetResult());
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AwaitableLockToken TryWaitAsync()
        {
            int token = TryTake();
            if (token != 0) return new AwaitableLockToken(new LockToken(this, token));

            // otherwise, do things the hard way
            return TakeWithTimeoutAsync();
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LockToken TryWait()
        {
            var token = TryTake();
            return new LockToken(this, token != 0 ? token : TakeWithTimeout());
        }

        private readonly Queue<PendingLockToken> _queue = new Queue<PendingLockToken>();

        internal sealed class SyncPendingLockToken : PendingLockToken
        {
            protected override void OnAssigned(PipeScheduler scheduler)
            {
                lock (this)
                {
                    Monitor.Pulse(this); // wake up a sleeper
                }
            }
        }
        [ThreadStatic]
        static SyncPendingLockToken _perThreadLockObject;
        private static SyncPendingLockToken GetPerThreadLockObject() => _perThreadLockObject ?? GetNewPerThreadLockObject();
        private static SyncPendingLockToken GetNewPerThreadLockObject() => _perThreadLockObject = new SyncPendingLockToken();
        private static void ResetPerThreadLockObject() => _perThreadLockObject = null;
    }
}
