using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace StackExchange.Redis
{
    sealed partial class CompletionManager
    {
        private static readonly WaitCallback processAsyncCompletionQueue = ProcessAsyncCompletionQueue,
            anyOrderCompletionHandler = AnyOrderCompletionHandler;

        private readonly Queue<ICompletable> asyncCompletionQueue = new Queue<ICompletable>();

        private readonly ConnectionMultiplexer multiplexer;

        private readonly string name;

        long completedSync, completedAsync, failedAsync;
        private readonly bool allowSyncContinuations;
        public CompletionManager(ConnectionMultiplexer multiplexer, string name)
        {
            this.multiplexer = multiplexer;
            this.name = name;
            this.allowSyncContinuations = multiplexer.RawConfig.AllowSynchronousContinuations;
        }
        public void CompleteSyncOrAsync(ICompletable operation)
        {
            if (operation == null) return;
            if (operation.TryComplete(false, allowSyncContinuations))
            {
                multiplexer.Trace("Completed synchronously: " + operation, name);
                Interlocked.Increment(ref completedSync);
                return;
            }
            else
            {
                if (multiplexer.PreserveAsyncOrder)
                {
                    multiplexer.Trace("Queueing for asynchronous completion", name);
                    bool startNewWorker;
                    lock (asyncCompletionQueue)
                    {
                        asyncCompletionQueue.Enqueue(operation);
                        startNewWorker = asyncCompletionQueue.Count == 1;
                    }
                    if (startNewWorker)
                    {
                        multiplexer.Trace("Starting new async completion worker", name);
                        OnCompletedAsync();
                        ThreadPool.QueueUserWorkItem(processAsyncCompletionQueue, this);
                    }
                } else
                {
                    multiplexer.Trace("Using thread-pool for asynchronous completion", name);
                    ThreadPool.QueueUserWorkItem(anyOrderCompletionHandler, operation);
                    Interlocked.Increment(ref completedAsync); // k, *technically* we haven't actually completed this yet, but: close enough
                }
            }
        }
        internal void GetCounters(ConnectionCounters counters)
        {
            lock (asyncCompletionQueue)
            {
                counters.ResponsesAwaitingAsyncCompletion = asyncCompletionQueue.Count;
            }
            counters.CompletedSynchronously = Interlocked.Read(ref completedSync);
            counters.CompletedAsynchronously = Interlocked.Read(ref completedAsync);
            counters.FailedAsynchronously = Interlocked.Read(ref failedAsync);
        }

        internal int GetOutstandingCount()
        {
            lock(asyncCompletionQueue)
            {
                return asyncCompletionQueue.Count;
            }
        }

        internal void GetStormLog(StringBuilder sb)
        {
            lock(asyncCompletionQueue)
            {
                if (asyncCompletionQueue.Count == 0) return;
                sb.Append("Response awaiting completion: ").Append(asyncCompletionQueue.Count).AppendLine();
                int total = 0;
                foreach(var item in asyncCompletionQueue)
                {
                    if (++total >= 500) break;
                    item.AppendStormLog(sb);
                    sb.AppendLine();
                }
            }
        }

        private static void AnyOrderCompletionHandler(object state)
        {
            try
            {
                ConnectionMultiplexer.TraceWithoutContext("Completing async (any order): " + state);
                ((ICompletable)state).TryComplete(true, true);
            }
            catch (Exception ex)
            {
                ConnectionMultiplexer.TraceWithoutContext("Async completion error: " + ex.Message);
            }
        }

        private static void ProcessAsyncCompletionQueue(object state)
        {
            ((CompletionManager)state).ProcessAsyncCompletionQueueImpl();
        }

        partial void OnCompletedAsync();
        private void ProcessAsyncCompletionQueueImpl()
        {
            int total = 0;
            do
            {
                ICompletable next;
                lock (asyncCompletionQueue)
                {
                    next = asyncCompletionQueue.Count == 0 ? null
                        : asyncCompletionQueue.Dequeue();
                }
                if(next == null && Thread.Yield()) // give it a moment and try again
                {
                    lock (asyncCompletionQueue)
                    {
                        next = asyncCompletionQueue.Count == 0 ? null
                            : asyncCompletionQueue.Dequeue();
                    }
                }
                if (next == null) break; // nothing to do
                try
                {
                    multiplexer.Trace("Completing async (ordered): " + next, name);
                    next.TryComplete(true, allowSyncContinuations);
                    Interlocked.Increment(ref completedAsync);
                }
                catch(Exception ex)
                {
                    multiplexer.Trace("Async completion error: " + ex.Message, name);
                    Interlocked.Increment(ref failedAsync);
                }
                total++;
            } while (true);

            multiplexer.Trace("Async completion worker processed " + total + " operations", name);
        }


    }
}
