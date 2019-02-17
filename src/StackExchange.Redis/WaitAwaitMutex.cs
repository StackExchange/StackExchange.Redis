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

        using (var token = mutex.Wait())
        {
            if(token.Success) {...}
        }

        or

        using (var token = await mutex.WaitAsync())
        {
            if(token.Success) {...}
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
            private readonly WaitAwaitMutex _parent;
            private readonly int _token;

            /// <summary>
            /// Indicates whether the mutex was successfully taken
            /// </summary>
            public bool Success => _token != 0;

            internal LockToken(WaitAwaitMutex parent, int token)
            {
                _parent = parent;
                _token = token;
            }

            /// <summary>
            /// Release the mutex, if obtained
            /// </summary>
            public void Dispose()
            {
                if (_token != 0) _parent.Release(_token, demandMatch: true);
            }
        }

        private void Release(int token, bool demandMatch = true)
        {
            void ThrowInvalidLockHolder() => throw new InvalidOperationException("Attempt to release a WaitAwaitMutex that was not held");

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
                    bool success = TryTakeInsideLock(out var newToken);
                    Debug.Assert(success); // should have worked: we have the lock and it was zero

                    do
                    {
                        // try to hand that new lock to a recipient
                        var next = _queue.Dequeue();
                        if (next.IsAsync) _pendingAsyncOperations--;
                        if (next.TrySetResult(_scheduler, newToken)) return; // so we don't clear the token
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
                    var remaining = UpdateTimeOut(next.Start, TimeoutMilliseconds);
                    if (remaining == 0)
                    {
                        // tell them that they failed
                        _queue.Dequeue();
                        if (next.IsAsync) _pendingAsyncOperations--;
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
            {   CancelExistingTimeout();
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

        private volatile int _currentToken;
        private int _nextToken;

        private bool TryTakeImmediately(ref bool queueLockTaken, out LockToken token)
        {
            Monitor.TryEnter(_queue, 0, ref queueLockTaken);
            if (queueLockTaken & _currentToken == 0)
            {
                // increment the token and record it
                int next = ++_nextToken;
                if (next == 0) next++; // avoiding the zero sentinel
                _currentToken = next;
                token = new LockToken(this, next);
                return true;
            }
            token = default;
            return false;
        }

        private bool TryTakeInsideLock(out LockToken token)
        {
            if (_currentToken == 0)
            {
                // increment the token and record it
                int next = ++_nextToken;
                if (next == 0) next++; // avoiding the zero sentinel
                _currentToken = next;
                token = new LockToken(this, next);
                return true;
            }
            token = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TryTakeBySpinning(ref bool queueLockTaken, out LockToken token)
        {
            // try a SpinWait to see if we can avoid the expensive bits
            SpinWait spin = new SpinWait();
            do
            {
                // release (if we hold it) and spin
                if (queueLockTaken)
                {
                    Monitor.Exit(_queue);
                    queueLockTaken = false;
                }
                spin.SpinOnce();

                if (TryTakeImmediately(ref queueLockTaken, out token)) return true;
            } while (!spin.NextSpinWillYield);
            token = default;
            return false;
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

        LockToken TakeWithTimeout(ref bool queueLockTaken)
        {
            // try and spin
            if (TryTakeBySpinning(ref queueLockTaken, out var token)) return token;

            // if "now or never", bail
            if (TimeoutMilliseconds == 0) return default;

            bool itemLockTaken = false;
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

                // now lock the global queue (if we don't already have it); and then have a final stab at getting it cheaply
                if (!queueLockTaken)
                {
                    Monitor.TryEnter(_queue, TimeoutMilliseconds, ref queueLockTaken);
                    if (!queueLockTaken) return default; // couldn't even get the lock, let alone the mutex

                    if (TryTakeInsideLock(out token)) return token;
                }
                item.Reset();

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
                Monitor.Wait(item, UpdateTimeOut(start, TimeoutMilliseconds));

                // keep in mind that we have the item lock here
                var result = item.GetResult();
                if (!result.Success) // timeout
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
                        else if (_queue.Peek().IsFor(item))
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
                if (itemLockTaken) Monitor.Exit(item);
            }
        }

        int _pendingAsyncOperations;
        AwaitableLockToken TakeWithTimeoutAsync(ref bool queueLockTaken)
        {
            // try and spin
            if (TryTakeBySpinning(ref queueLockTaken, out var token)) return new AwaitableLockToken(token);

            // if "now or never", bail
            if (TimeoutMilliseconds == 0) return default;

            var start = GetTime();

            // lock the global queue; then have a final stab at getting it cheaply
            if (!queueLockTaken)
            {
                Monitor.TryEnter(_queue, TimeoutMilliseconds, ref queueLockTaken);
                if (!queueLockTaken) return default; // couldn't even get the lock, let alone the mutex

                if (TryTakeInsideLock(out token)) return new AwaitableLockToken(token);
            }

            // otherwise enqueue the pending item, and release
            // the global queue
            var asyncItem = QueueItem.CreateAsync(start);
            _queue.Enqueue(asyncItem);
            if (_pendingAsyncOperations++ == 0) SetNextAsyncTimeoutInsideLock(); // first async op

            return asyncItem.ForAwait();
        }

        /// <summary>
        /// Custom awaitable result from WaitAsync on WaitAwaitMutex
        /// </summary>
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

            /// <summary>
            /// Obtain the LockToken after completion of this async operation
            /// </summary>
            public LockToken GetResult() => _pending?.GetResult() ?? _token;

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
            public void Reset()
            {
                Interlocked.Exchange(ref _state, LockState.Pending);
                _token = default;
            }

            public bool TrySetResult(PipeScheduler scheduler, LockToken token)
            {
                if (Interlocked.CompareExchange(ref _state, LockState.Assigning, LockState.Pending) != LockState.Pending)
                    return false; // not pending
                _token = token;
                if (Interlocked.CompareExchange(ref _state, LockState.Completed, LockState.Assigning) != LockState.Assigning)
                {
                    // it got completed or reset while we were assigning; that means we lose :(
                    _token = default;
                    return false;
                }
                OnAssigned(scheduler);
                return true;
            }

            private LockToken _token;
            private int _state;
            private static class LockState
            {   // using this as a glorified enum; can't use enum directly because
                // or Interlocked etc support
                public const int
                    Pending = 0, // incomplete operation is in progress
                    Assigning = 1, // we're in the process of assigning the token
                    Completed = 2; // token has been successfully assigned
            }

            // if already complete: returns the token; otherwise, dooms the operation
            public LockToken GetResult()
            {
                // return the token *if the status is already completed*, either way changing it *to* completed
                return Interlocked.Exchange(ref _state, LockState.Completed) == LockState.Completed
                    ? _token : default;
            }

            public bool IsCompleted() => Volatile.Read(ref _state) == LockState.Completed;

            protected abstract void OnAssigned(PipeScheduler scheduler);
        }
        internal sealed class AsyncLockToken : PendingLockToken
        {
            private Action _continuation;

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
                    throw new NotSupportedException($"Only one pending continuation is permitted");
                }
            }
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AwaitableLockToken WaitAsync()
        {
            bool queueLockTaken = false;
            try
            {
                // try to take as uncontested (zero timeout)
                if (_currentToken == 0 && TryTakeImmediately(ref queueLockTaken, out var token)) return new AwaitableLockToken(token);

                // otherwise, do things the hard way
                return TakeWithTimeoutAsync(ref queueLockTaken);
            }
            finally
            {
                if (queueLockTaken) Monitor.Exit(_queue);
            }
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LockToken Wait()
        {
            bool queueLockTaken = false;
            try
            {
                // try to take as uncontested (zero timeout)
                if (_currentToken == 0 && TryTakeImmediately(ref queueLockTaken, out var token)) return token;

                // otherwise, do things the hard way
                return TakeWithTimeout(ref queueLockTaken);
            }
            finally
            {
                if (queueLockTaken) Monitor.Exit(_queue);
            }
        }

        private readonly Queue<QueueItem> _queue = new Queue<QueueItem>();

        internal sealed class SyncLockToken : PendingLockToken
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
        static SyncLockToken _perThreadLockObject;
        private static SyncLockToken GetPerThreadLockObject() => _perThreadLockObject ?? GetNewPerThreadLockObject();
        private static SyncLockToken GetNewPerThreadLockObject() => _perThreadLockObject = new SyncLockToken();
        private static void ResetPerThreadLockObject() => _perThreadLockObject = null;


        private readonly struct QueueItem
        {
            private readonly PendingLockToken _source;

            public uint Start { get; }
            public bool IsAsync => _source is AsyncLockToken;

            public AwaitableLockToken ForAwait() => new AwaitableLockToken((AsyncLockToken)_source);

            public static QueueItem CreateAsync(uint start) => new QueueItem(start, new AsyncLockToken());

            public static QueueItem CreateSync(uint start, SyncLockToken token) => new QueueItem(start, token);

            internal bool TrySetResult(PipeScheduler scheduler, LockToken token) => _source.TrySetResult(scheduler, token);

            internal bool IsFor(PendingLockToken item) => (object)item == (object)_source; // ref-check

            public QueueItem(uint start, PendingLockToken source)
            {
                Start = start;
                _source = source;
            }

        }

    }
}
