using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace RESPite.Transports;

/// <summary>
/// A counting semaphore for exactly one waiter at a time, whose asynchronous wait allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>For the split read loop's hand-off</b>, where the filler waits for a free buffer and the parser for a filled
/// one - one waiter each. <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/> allocated a wait node and an
/// async promise, and registered on the cancellation token, every time it had to wait - which for the parser, once
/// its spin runs out, is most reads under load; measured at ~10% of CPU at 6 callers.
/// </para>
/// <para>
/// One waiter makes it simple: a wait either takes a count or parks on a single reusable
/// <see cref="ManualResetValueTaskSourceCore{TResult}"/>, which cannot be re-armed until its waiter has resumed.
/// Cancellation is not a token per wait but <see cref="Cancel"/>, called once - from one registration for the
/// connection's life - and answered with <see langword="false"/>.
/// </para>
/// </remarks>
internal sealed class SingleWaiterSemaphore : IValueTaskSource<bool>
{
    private readonly object _lock = new();
    private ManualResetValueTaskSourceCore<bool> _core = new() { RunContinuationsAsynchronously = true };
    private int _count;
    private bool _waiting;
    private bool _cancelled;

    /// <summary>Create with an initial count.</summary>
    /// <param name="initialCount">How many waits succeed before a release is needed.</param>
    internal SingleWaiterSemaphore(int initialCount) => _count = initialCount;

    /// <summary>Take a count if one is available, without waiting.</summary>
    /// <returns>Whether a count was taken.</returns>
    internal bool TryWait()
    {
        // a lock-free look first: this is polled in a spin, and an empty count is the common answer
        if (Volatile.Read(ref _count) == 0 && !Volatile.Read(ref _cancelled)) return false;
        lock (_lock)
        {
            if (_cancelled || _count == 0) return false;
            _count--;
            return true;
        }
    }

    /// <summary>Take a count, waiting for one if necessary.</summary>
    /// <returns>True with a count taken; false if cancelled, now or while waiting.</returns>
    internal ValueTask<bool> WaitAsync()
    {
        lock (_lock)
        {
            if (_cancelled) return new ValueTask<bool>(false);
            if (_count > 0)
            {
                _count--;
                return new ValueTask<bool>(true);
            }

            Debug.Assert(!_waiting, "only one waiter at a time");
            _core.Reset();
            _waiting = true;
            return new ValueTask<bool>(this, _core.Version);
        }
    }

    /// <summary>Return a count, waking the waiter if there is one.</summary>
    internal void Release()
    {
        lock (_lock)
        {
            if (!_waiting)
            {
                _count++;
                return;
            }

            _waiting = false;
        }

        _core.SetResult(true); // handed straight to the waiter, so the count is not incremented
    }

    /// <summary>Wake the waiter with false, and make every later wait return false at once.</summary>
    internal void Cancel()
    {
        lock (_lock)
        {
            if (_cancelled) return;
            _cancelled = true;
            if (!_waiting) return;
            _waiting = false;
        }

        _core.SetResult(false);
    }

    bool IValueTaskSource<bool>.GetResult(short token) => _core.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _core.GetStatus(token);

    void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
