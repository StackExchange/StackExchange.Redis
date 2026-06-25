using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#if !NET
namespace StackExchange.Redis;

/*
Compensating for the fact that netfx SemaphoreSlim is kinda janky (https://blog.marcgravell.com/2019/02/fun-with-spiral-of-death.html).

This uses a simple queue of sync/async callers, and assumes a reasonable caller (the original MutexSlim is more defensive, as
a general purpose public API).
*/

internal partial struct AwaitableMutex
{
    private readonly State _state;

    private partial AwaitableMutex(int timeoutMilliseconds)
    {
        _state = new(timeoutMilliseconds);
    }

    public partial void Dispose() => _state?.Dispose();

    public partial bool IsAvailable => _state.IsAvailable;
    public partial int TimeoutMilliseconds => _state.TimeoutMilliseconds;

    public partial bool TryTakeInstant() => _state.TryTakeInstant();

    public partial ValueTask<bool> TryTakeAsync(CancellationToken cancellationToken)
        => _state.TryTakeAsync(cancellationToken);

    public partial bool TryTakeSync() => _state.TryTakeSync();

    public partial void Release() => _state.Release();

    private sealed class State : IDisposable
    {
        private readonly Queue<IPendingCaller> _queue = new();
        private bool _isHeld;
        private bool _isDisposed;

        public State(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds < Timeout.Infinite) ThrowOutOfRangeException();
            TimeoutMilliseconds = timeoutMilliseconds;

            static void ThrowOutOfRangeException() => throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        }

        public int TimeoutMilliseconds { get; }

        public bool IsAvailable
        {
            get
            {
                lock (_queue)
                {
                    return !_isDisposed && !_isHeld && _queue.Count == 0;
                }
            }
        }

        public bool TryTakeInstant()
        {
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_queue, 0, ref lockTaken);
                if (!lockTaken) return false;

                return TryTakeInsideLock();
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_queue);
            }
        }

        public bool TryTakeSync()
        {
            bool lockTaken = false;
            try
            {
                // try to acquire uncontested lock - that way we can avoid checking the time
                Monitor.TryEnter(_queue, 0, ref lockTaken);
                if (lockTaken && TryTakeInsideLock()) return true;
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_queue);
            }

            return TryTakeSyncSlow();
        }

        public ValueTask<bool> TryTakeAsync(CancellationToken cancellationToken)
        {
            bool lockTaken = false;
            try
            {
                // try to acquire uncontested lock - that way we can avoid allocating the pending caller
                Monitor.TryEnter(_queue, 0, ref lockTaken);
                if (lockTaken)
                {
                    if (_isDisposed) return DisposedAsync();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return CanceledAsync(cancellationToken);
                    }

                    if (TryTakeInsideLockCore()) return new ValueTask<bool>(true);
                }
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_queue);
            }

            return TryTakeAsyncSlow(cancellationToken);
        }

        private ValueTask<bool> TryTakeAsyncSlow(CancellationToken cancellationToken)
        {
            lock (_queue)
            {
                if (_isDisposed) return DisposedAsync();
                if (cancellationToken.IsCancellationRequested)
                {
                    return CanceledAsync(cancellationToken);
                }

                if (TryTakeInsideLockCore()) return new ValueTask<bool>(true);
                if (TimeoutMilliseconds == 0) return new ValueTask<bool>(false);
                if (cancellationToken.IsCancellationRequested) return CanceledAsync(cancellationToken);

                var pending = new AsyncPendingCaller(TimeoutMilliseconds, cancellationToken);
                _queue.Enqueue(pending);
                return new ValueTask<bool>(pending.Task);
            }
        }

        public void Release()
        {
            lock (_queue)
            {
                ThrowIfDisposed();
                if (!_isHeld) ThrowNotHeld();

                while (_queue.Count != 0)
                {
                    if (_queue.Dequeue().TryGrant()) return;
                }

                _isHeld = false;
            }

            static void ThrowNotHeld() => throw new SemaphoreFullException();
        }

        private bool TryTakeInsideLock()
        {
            ThrowIfDisposed();
            return TryTakeInsideLockCore();
        }

        private bool TryTakeInsideLockCore()
        {
            if (_isHeld || _queue.Count != 0) return false;
            _isHeld = true;
            return true;
        }

        private bool TryTakeSyncSlow()
        {
            if (TimeoutMilliseconds == 0) return false;

            var start = GetTime();
            SyncPendingCaller? pending = null;
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_queue, TimeoutMilliseconds, ref lockTaken);
                if (!lockTaken) return false;
                if (TryTakeInsideLock()) return true;

                var remaining = GetRemainingTimeout(start, TimeoutMilliseconds);
                if (remaining == 0) return false;

                pending = new SyncPendingCaller(start, TimeoutMilliseconds);
                _queue.Enqueue(pending);
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_queue);
            }

            return pending!.Wait();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            lock (_queue)
            {
                _isHeld = false;
                while (_queue.Count != 0)
                {
                    _queue.Dequeue().Abort();
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed) ThrowDisposed();
        }

        private static void ThrowDisposed() => throw new ObjectDisposedException(nameof(AwaitableMutex));

        private static ValueTask<bool> DisposedAsync()
            => new(Task.FromException<bool>(new ObjectDisposedException(nameof(AwaitableMutex))));

        private static ValueTask<bool> CanceledAsync(CancellationToken cancellationToken)
            => new(Task.FromCanceled<bool>(cancellationToken));

        private static uint GetTime() => (uint)Environment.TickCount;

        private static int GetRemainingTimeout(uint startTime, int originalTimeoutMilliseconds)
        {
            if (originalTimeoutMilliseconds == Timeout.Infinite) return Timeout.Infinite;

            var elapsedMilliseconds = GetTime() - startTime;
            if (elapsedMilliseconds > int.MaxValue) return 0;

            var remaining = originalTimeoutMilliseconds - (int)elapsedMilliseconds;
            return remaining <= 0 ? 0 : remaining;
        }

        private interface IPendingCaller
        {
            bool TryGrant();
            void Abort();
        }

        private sealed class SyncPendingCaller : IPendingCaller
        {
            private readonly uint _start;
            private readonly int _timeoutMilliseconds;
            private bool _isComplete;
            private bool _wasGranted;
            private bool _wasAborted;

            public SyncPendingCaller(uint start, int timeoutMilliseconds)
            {
                _start = start;
                _timeoutMilliseconds = timeoutMilliseconds;
            }

            public bool Wait()
            {
                lock (this)
                {
                    while (!_isComplete)
                    {
                        var remaining = GetRemainingTimeout(_start, _timeoutMilliseconds);
                        if (remaining == 0)
                        {
                            _isComplete = true;
                            return false;
                        }

                        if (remaining == Timeout.Infinite)
                        {
                            Monitor.Wait(this);
                        }
                        else
                        {
                            Monitor.Wait(this, remaining);
                        }
                    }

                    if (_wasAborted) ThrowDisposed();
                    return _wasGranted;
                }
            }

            public bool TryGrant()
            {
                lock (this)
                {
                    if (_isComplete) return false;
                    _wasGranted = true;
                    _isComplete = true;
                    Monitor.Pulse(this);
                    return true;
                }
            }

            public void Abort()
            {
                lock (this)
                {
                    if (_isComplete) return;
                    _wasAborted = true;
                    _isComplete = true;
                    Monitor.Pulse(this);
                }
            }
        }

        private sealed class AsyncPendingCaller : TaskCompletionSource<bool>, IPendingCaller
        {
            private static readonly TimerCallback s_onTimeout = state => ((AsyncPendingCaller)state!).TryComplete(CompletionState.TimedOut);
            private static readonly Action<object?> s_onCanceled = state => ((AsyncPendingCaller)state!).TryComplete(CompletionState.Canceled);

            private readonly CancellationToken _cancellationToken;
            private readonly CancellationTokenRegistration _cancellation;
            private readonly Timer? _timeout;
            private int _completionState;

            public AsyncPendingCaller(int timeoutMilliseconds, CancellationToken cancellationToken)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                _cancellationToken = cancellationToken;
                if (timeoutMilliseconds != Timeout.Infinite)
                {
                    _timeout = new Timer(s_onTimeout, this, timeoutMilliseconds, Timeout.Infinite);
                }

                if (cancellationToken.CanBeCanceled)
                {
                    _cancellation = cancellationToken.Register(s_onCanceled, this);
                }
            }

            public bool TryGrant() => TryComplete(CompletionState.Granted);

            public void Abort() => TryComplete(CompletionState.Disposed);

            private bool TryComplete(CompletionState completionState)
            {
                var newState = (int)completionState;
                if (Interlocked.CompareExchange(ref _completionState, newState, (int)CompletionState.Pending) != (int)CompletionState.Pending)
                {
                    return false;
                }

                if (completionState != CompletionState.TimedOut) _timeout?.Dispose();
                if (completionState != CompletionState.Canceled) _cancellation.Dispose();
                Complete(completionState);
                return true;
            }

            private void Complete(CompletionState completionState)
            {
                switch (completionState)
                {
                    case CompletionState.Granted:
                        TrySetResult(true);
                        break;
                    case CompletionState.TimedOut:
                        TrySetResult(false);
                        break;
                    case CompletionState.Canceled:
                        TrySetCanceled(_cancellationToken);
                        break;
                    case CompletionState.Disposed:
                        TrySetException(new ObjectDisposedException(nameof(AwaitableMutex)));
                        break;
                }
            }

            private enum CompletionState
            {
                Pending = 0,
                Granted = 1,
                TimedOut = 2,
                Canceled = 3,
                Disposed = 4,
            }
        }
    }
}
#endif
