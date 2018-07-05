using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace StackExchange.Redis
{
    internal sealed partial class CompletionManager
    {
        private readonly Queue<ICompletable> asyncCompletionQueue = new Queue<ICompletable>();

        private readonly ConnectionMultiplexer multiplexer;

        private readonly string name;

        private long completedSync, completedAsync, failedAsync;
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
            }
            else
            {
                multiplexer.Trace("Using thread-pool for asynchronous completion", name);
                multiplexer.SocketManager.ScheduleTask(s_AnyOrderCompletionHandler, operation);
                Interlocked.Increment(ref completedAsync); // k, *technically* we haven't actually completed this yet, but: close enough
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

        private static readonly Action<object> s_AnyOrderCompletionHandler = AnyOrderCompletionHandler;
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

        partial void OnCompletedAsync();
        
    }
}
