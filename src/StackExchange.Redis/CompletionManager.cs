using System;
using System.Threading;

namespace StackExchange.Redis
{
    internal static class CompletionManagerHelpers
    {
        public static void CompleteSyncOrAsync(this PhysicalBridge bridge, ICompletable operation)
            => CompletionManager.CompleteSyncOrAsyncImpl(bridge?.completionManager, operation);

        public static void IncrementSyncCount(this PhysicalBridge bridge)
            => bridge?.completionManager?.IncrementSyncCount();

        public static void CompleteAsync(this PhysicalBridge bridge, ICompletable operation)
            => CompletionManager.CompleteAsync(bridge?.completionManager, operation);

        public static void CompleteSyncOrAsync(this CompletionManager manager, ICompletable operation)
            => CompletionManager.CompleteSyncOrAsyncImpl(manager, operation);
    }
    internal sealed partial class CompletionManager
    {
        internal static void CompleteSyncOrAsyncImpl(CompletionManager manager, ICompletable operation)
        {
            if (operation == null) return;
            if (manager != null) manager.PerInstanceCompleteSyncOrAsync(operation);
            else SharedCompleteSyncOrAsync(operation);
        }

        internal void IncrementSyncCount() => Interlocked.Increment(ref completedSync);

        internal static void CompleteAsync(CompletionManager manager, ICompletable operation)
        {
            var sched = manager.multiplexer.SocketManager;
            if (sched != null)
            {
                sched.ScheduleTask(s_AnyOrderCompletionHandler, operation);
                Interlocked.Increment(ref manager.completedAsync);
            }
            else
            {
                SocketManager.Shared.ScheduleTask(s_AnyOrderCompletionHandler, operation);
            }
        }

        private readonly ConnectionMultiplexer multiplexer;

        private readonly string name;

        private long completedSync, completedAsync, failedAsync;
        public CompletionManager(ConnectionMultiplexer multiplexer, string name)
        {
            this.multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
            this.name = name;
        }

        private static void SharedCompleteSyncOrAsync(ICompletable operation)
        {
            if (!operation.TryComplete(false))
            {
                SocketManager.Shared.ScheduleTask(s_AnyOrderCompletionHandler, operation);
            }
        }
        private void PerInstanceCompleteSyncOrAsync(ICompletable operation)
        {
            if (operation == null) { }
            else if (operation.TryComplete(false))
            {
                multiplexer.Trace("Completed synchronously: " + operation, name);
                Interlocked.Increment(ref completedSync);
            }
            else
            {
                multiplexer.Trace("Using thread-pool for asynchronous completion", name);
                (multiplexer.SocketManager ?? SocketManager.Shared).ScheduleTask(s_AnyOrderCompletionHandler, operation);
                Interlocked.Increment(ref completedAsync); // k, *technically* we haven't actually completed this yet, but: close enough
            }
        }

        internal void GetCounters(ConnectionCounters counters)
        {
            counters.CompletedSynchronously = Interlocked.Read(ref completedSync);
            counters.CompletedAsynchronously = Interlocked.Read(ref completedAsync);
            counters.FailedAsynchronously = Interlocked.Read(ref failedAsync);
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
    }
}
