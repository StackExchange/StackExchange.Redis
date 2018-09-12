using System;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal abstract class ResultBox
    {
        protected Exception _exception;
        public abstract bool IsAsync { get; }
        public bool IsFaulted => _exception != null;

        public void SetException(Exception exception) => _exception = exception ?? s_cancelled;

        public abstract bool TryComplete(bool isAsync);

        public void Cancel() => _exception = s_cancelled;

        // in theory nobody should directly observe this; the only things
        // that call Cancel are transactions etc - TCS-based, and we detect
        // that and use TrySetCanceled instead
        // about any confusion in stack-trace
        private static readonly Exception s_cancelled = new TaskCanceledException();
    }

    internal sealed class ResultBox<T> : ResultBox
    {
        private static readonly ResultBox<T>[] store = new ResultBox<T>[64];
        private object stateOrCompletionSource;
        private int _usageCount;
        private T value;

        public ResultBox(object stateOrCompletionSource)
        {
            this.stateOrCompletionSource = stateOrCompletionSource;
            _usageCount = 1;
        }

        public static ResultBox<T> Get(object stateOrCompletionSource)
        {
            ResultBox<T> found;
            for (int i = 0; i < store.Length; i++)
            {
                if ((found = Interlocked.Exchange(ref store[i], null)) != null)
                {
                    found.Reset(stateOrCompletionSource);
                    return found;
                }
            }

            return new ResultBox<T>(stateOrCompletionSource);
        }

        public static void UnwrapAndRecycle(ResultBox<T> box, bool recycle, out T value, out Exception exception)
        {
            if (box == null)
            {
                value = default(T);
                exception = null;
            }
            else
            {
                value = box.value;
                exception = box._exception;
                box.value = default(T);
                box._exception = null;
                if (recycle)
                {
                    var newCount = Interlocked.Decrement(ref box._usageCount);
                    if (newCount != 0)
                        throw new InvalidOperationException($"Result box count error: is {newCount} in UnwrapAndRecycle (should be 0)");

                    // Clear state prior to recycling, so as not to root it
                    box.stateOrCompletionSource = null;
                    for (int i = 0; i < store.Length; i++)
                    {
                        if (Interlocked.CompareExchange(ref store[i], box, null) == null) return;
                    }
                }
            }
        }

        public void SetResult(T value)
        {
            this.value = value;
        }

        internal bool TrySetResult(T value)
        {
            if (_exception != null) return false;
            this.value = value;
            return true;
        }

        public override bool IsAsync => stateOrCompletionSource is TaskCompletionSource<T>;

        public override bool TryComplete(bool isAsync)
        {
            if (stateOrCompletionSource is TaskCompletionSource<T> tcs)
            {
                if (isAsync || (tcs.Task.CreationOptions & TaskCreationOptions.RunContinuationsAsynchronously) != 0)
                {
                    // either on the async completion step, or the task is guarded
                    // againsts thread-stealing; complete it directly
                    // (note: RunContinuationsAsynchronously is only usable from NET46)
                    UnwrapAndRecycle(this, true, out T val, out Exception ex);

                    if (ex == null)
                    {
                        tcs.TrySetResult(val);
                    }
                    else
                    {
                        if (ex is TaskCanceledException) tcs.TrySetCanceled();
                        else tcs.TrySetException(ex);
                        // mark it as observed
                        GC.KeepAlive(tcs.Task.Exception);
                        GC.SuppressFinalize(tcs.Task);
                    }
                    return true;
                }
                else
                {
                    // could be thread-stealing continuations; push to async to preserve the reader thread
                    return false;
                }
            }
            else
            {
                lock (this)
                { // tell the waiting thread that we're done
                    Monitor.PulseAll(this);
                }
                ConnectionMultiplexer.TraceWithoutContext("Pulsed", "Result");
                return true;
            }
        }

        private void Reset(object stateOrCompletionSource)
        {
            var newCount = Interlocked.Increment(ref _usageCount);
            if (newCount != 1) throw new InvalidOperationException($"Result box count error: is {newCount} in Reset (should be 1)");
            value = default(T);
            _exception = null;

            this.stateOrCompletionSource = stateOrCompletionSource;
        }
    }
}
