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

        private int asyncResultCounter;

        long completedSync, completedAsync, failedAsync;

        public CompletionManager(ConnectionMultiplexer multiplexer, string name)
        {
            this.multiplexer = multiplexer;
            this.name = name;
        }
        public void CompleteSyncOrAsync(ICompletable operation)
        {
            if (operation == null) return;
            if (operation.TryComplete(false))
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
                    lock (asyncCompletionQueue)
                    {
                        asyncCompletionQueue.Enqueue(operation);
                    }
                    bool startNewWorker = Interlocked.Increment(ref asyncResultCounter) == 1;
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
                ((ICompletable)state).TryComplete(true);
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

        bool DecrementAsyncCounterAndCheckForMoreAsyncWork()
        {
            if (Thread.VolatileRead(ref asyncResultCounter) == 1)
            {   // if we're on the very last item, then rather than exit immediately,
                // let's give it a moment to see if more work comes in
                Thread.Yield();
            }
            return Interlocked.Decrement(ref asyncResultCounter) != 0;
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
                    if (asyncCompletionQueue.Count == 0)
                    {
                        // compete; note that since we didn't do work we do NOT
                        // want to decr the counter; fortunately "break" gets
                        // this correct
                        break;
                    }
                    next = asyncCompletionQueue.Dequeue();
                }
                try
                {
                    multiplexer.Trace("Completing async (ordered): " + next, name);
                    next.TryComplete(true);
                    Interlocked.Increment(ref completedAsync);
                }
                catch(Exception ex)
                {
                    multiplexer.Trace("Async completion error: " + ex.Message, name);
                    Interlocked.Increment(ref failedAsync);
                }
                total++;
            } while (DecrementAsyncCounterAndCheckForMoreAsyncWork());

            multiplexer.Trace("Async completion worker processed " + total + " operations", name);
        }


    }
}
