using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal static class TaskManager
    {
        public static bool Execute(Func<Task> action, int timeout)
        {
            var oldContext = SynchronizationContext.Current;
            var context = new SychronousSynchronizationContext(oldContext);
            SynchronizationContext.SetSynchronizationContext(context);
            var oldContinueOnCapturedContext = TaskExtensions.ContinueOnCapturedContext;
            TaskExtensions.ContinueOnCapturedContext = true;
            try
            {
                return context.ExecuteImpl(action, timeout);
            }
            finally
            {
                TaskExtensions.ContinueOnCapturedContext = oldContinueOnCapturedContext;
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }

        private class SychronousSynchronizationContext : SynchronizationContext
        {
            readonly Queue<Action> _pendingOperations = new Queue<Action>();
            readonly SynchronizationContext _postCompletionContext;
            bool _complete;

            public SychronousSynchronizationContext(SynchronizationContext postCompletionContext)
            {
                _postCompletionContext = postCompletionContext;
            }

            public bool ExecuteImpl(Func<Task> action, int timeout)
            {
                lock (_pendingOperations)
                {
                    try
                    {
                        var dueBy = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
                        var task = action();
                        task.ContinueWith(SetComplete, TaskContinuationOptions.ExecuteSynchronously);
                        while (true)
                        {
                            while (_pendingOperations.Count == 0 && !_complete)
                            {
                                var waitFor = (int)(dueBy - DateTime.UtcNow).TotalMilliseconds;
                                if (waitFor <= 0) return false;
                                if (!Monitor.Wait(_pendingOperations, waitFor)) return false;
                            }
                            while (_pendingOperations.Count > 0)
                            {
                                _pendingOperations.Dequeue()();
                            }
                            if (_complete) break;
                        }
                        return true;
                    }
                    finally
                    {
                        _complete = true;
                    }
                }
            }

            private void SetComplete(Task task)
            {
                lock (_pendingOperations)
                {
                    _complete = true;
                    Monitor.Pulse(_pendingOperations);
                }
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                Action operation = () => d(state);
                bool scheduled = false;
                lock (_pendingOperations)
                {
                    if (!_complete)
                    {
                        Debug.WriteLine("Post from (sync): " + Thread.CurrentThread.ManagedThreadId);
                        _pendingOperations.Enqueue(operation);
                        scheduled = true;
                        Monitor.Pulse(_pendingOperations);
                    }
                }
                if (!scheduled)
                {
                    // If some sub-tasks don't complete until after the parent task completes,
                    // post completion to the original synchronization context.
                    Debug.WriteLine("Post from (async): " + Thread.CurrentThread.ManagedThreadId);
                    if (_postCompletionContext == null)
                    {
                        ThreadPool.QueueUserWorkItem(o => d(state));
                    }
                    else
                    {
                        _postCompletionContext.Post(d, state);
                    }
                }
            }
        }
    }
}
