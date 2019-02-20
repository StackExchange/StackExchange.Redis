using System;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal interface IResultBox
    {
        bool IsAsync { get; }
        bool IsFaulted { get; }
        void SetException(Exception ex);
        bool TryComplete(bool isAsync);
        void Cancel();
    }
    internal interface IResultBox<T> : IResultBox
    {
        T GetResult(out Exception ex, bool canRecycle = false);
        void SetResult(T value);
    }

    internal abstract class SimpleResultBox : IResultBox
    {
        private volatile Exception _exception;

        bool IResultBox.IsAsync => false;
        bool IResultBox.IsFaulted => _exception != null;
        void IResultBox.SetException(Exception exception) => _exception = exception ?? CancelledException;
        void IResultBox.Cancel() => _exception = CancelledException;

        bool IResultBox.TryComplete(bool isAsync)
        {
            lock (this)
            { // tell the waiting thread that we're done
                Monitor.PulseAll(this);
            }
            ConnectionMultiplexer.TraceWithoutContext("Pulsed", "Result");
            return true;
        }

        // in theory nobody should directly observe this; the only things
        // that call Cancel are transactions etc - TCS-based, and we detect
        // that and use TrySetCanceled instead
        // about any confusion in stack-trace
        internal static readonly Exception CancelledException = new TaskCanceledException();

        protected Exception Exception
        {
            get => _exception;
            set => _exception = value;
        }
    }

    internal sealed class SimpleResultBox<T> : SimpleResultBox, IResultBox<T>
    {
        private SimpleResultBox() { }
        private T _value;

        [ThreadStatic]
        private static SimpleResultBox<T> _perThreadInstance;

        public static IResultBox<T> Create() => new SimpleResultBox<T>();
        public static IResultBox<T> Get() // includes recycled boxes; used from sync, so makes re-use easy
        {
            var obj = _perThreadInstance ?? new SimpleResultBox<T>();
            _perThreadInstance = null; // in case of oddness; only set back when recycled
            return obj;
        }
        void IResultBox<T>.SetResult(T value) => _value = value;

        T IResultBox<T>.GetResult(out Exception ex, bool canRecycle)
        {
            var value = _value;
            ex = Exception;
            if (canRecycle)
            {
                Exception = null;
                _value = default;
                _perThreadInstance = this;
            }
            return value;
        }
    }

    internal sealed class TaskResultBox<T> : TaskCompletionSource<T>, IResultBox<T>
    {
        // you might be asking "wait, doesn't the Task own these?", to which
        // I say: no; we can't set *immediately* due to thread-theft etc, hence
        // the fun TryComplete indirection - so we need somewhere to buffer them
        private volatile Exception _exception;
        private T _value;

        private TaskResultBox(object asyncState, TaskCreationOptions creationOptions) : base(asyncState, creationOptions)
        { }

        bool IResultBox.IsAsync => true;

        bool IResultBox.IsFaulted => _exception != null;

        void IResultBox.Cancel() => _exception = SimpleResultBox.CancelledException;

        void IResultBox.SetException(Exception ex) => _exception = ex ?? SimpleResultBox.CancelledException;

        void IResultBox<T>.SetResult(T value) => _value = value;

        T IResultBox<T>.GetResult(out Exception ex, bool _)
        {
            ex = _exception;
            return _value;
            // nothing to do re recycle: TaskCompletionSource<T> cannot be recycled
        }

        bool IResultBox.TryComplete(bool isAsync)
        {
            if (isAsync || (Task.CreationOptions & TaskCreationOptions.RunContinuationsAsynchronously) != 0)
            {
                // either on the async completion step, or the task is guarded
                // againsts thread-stealing; complete it directly
                // (note: RunContinuationsAsynchronously is only usable from NET46)
                var val = _value;
                var ex = _exception;

                if (ex == null)
                {
                    TrySetResult(val);
                }
                else
                {
                    if (ex is TaskCanceledException) TrySetCanceled();
                    else TrySetException(ex);
                    // mark any exception as observed
                    var task = Task;
                    GC.KeepAlive(task.Exception);
                    GC.SuppressFinalize(task);
                }
                return true;
            }
            else
            {
                // could be thread-stealing continuations; push to async to preserve the reader thread
                return false;
            }
        }

        public static IResultBox<T> Create(out TaskCompletionSource<T> source, object asyncState, TaskCreationOptions creationOptions = TaskCreationOptions.None)
        {
            var obj = new TaskResultBox<T>(asyncState, creationOptions);
            source = obj;
            return obj;
        }
    }
}
