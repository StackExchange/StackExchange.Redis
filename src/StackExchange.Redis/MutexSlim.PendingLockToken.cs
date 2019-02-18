using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StackExchange.Redis
{
    partial class MutexSlim
    {
        internal abstract class PendingLockToken
        {
            private int _token = LockState.Pending; // combined state and counter

            public uint Start { get; private set; } // for timeout tracking

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected void Reset(uint start)
            {
                Start = start;
                Volatile.Write(ref _token, LockState.Pending);
            }

            protected PendingLockToken() { }
            protected PendingLockToken(uint start) => Start = start;

            public bool TrySetResult(PipeScheduler scheduler, int token)
            {
                int oldValue = Volatile.Read(ref _token);
                if (LockState.GetState(oldValue) == LockState.Pending
                    && Interlocked.CompareExchange(ref _token, token, oldValue) == oldValue)
                {
                    OnAssigned(scheduler);
                    return true;
                }
                return false;
            }

            internal bool TryCancel()
            {
                int oldValue;
                do
                {
                    // depends on the current state...
                    oldValue = Volatile.Read(ref _token);
                    switch (LockState.GetState(oldValue))
                    {
                        case LockState.Canceled:
                            return true; // fine, already canceled
                        case LockState.Timeout:
                        case LockState.Success:
                            return false; // nope, already reported
                    }
                    // otherwise, attempt to change the field; in case of conflict; re-do from start
                } while (Interlocked.CompareExchange(ref _token, LockState.ChangeState(oldValue, LockState.Canceled), oldValue) != oldValue);
                return true;


            }

            // if already complete: returns the token; otherwise, dooms the operation
            public int GetResult()
            {
                int oldValue, newValue;
                do
                {
                    oldValue = Volatile.Read(ref _token);
                    if (LockState.GetState(oldValue) != LockState.Pending)
                    {
                        // value is already fixed; just return it
                        return oldValue;
                    }
                    // we don't ever want to report different values from GetResult, so
                    // if you called GetResult prematurely: you doomed it to failure
                    newValue = LockState.ChangeState(oldValue, LockState.Timeout);

                    // if something changed while we were thinking, redo from start
                } while (Interlocked.CompareExchange(ref _token, newValue, oldValue) != oldValue);
                return newValue;
            }

            public bool IsCompleted => LockState.IsCompleted(Volatile.Read(ref _token));

            public bool IsCompletedSuccessfully => LockState.IsCompletedSuccessfully(Volatile.Read(ref _token));

            public bool IsCanceled => LockState.IsCanceled(Volatile.Read(ref _token));

            protected abstract void OnAssigned(PipeScheduler scheduler);
        }
    }
}
