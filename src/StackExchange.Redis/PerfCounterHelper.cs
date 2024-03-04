using System.Threading;

namespace StackExchange.Redis
{
    internal static class PerfCounterHelper
    {
        internal static string GetThreadPoolAndCPUSummary()
        {
            GetThreadPoolStats(out string iocp, out string worker, out string? workItems);
            return $"IOCP: {iocp}, WORKER: {worker}, POOL: {workItems ?? "n/a"}";
        }

        internal static int GetThreadPoolStats(out string iocp, out string worker, out string? workItems)
        {
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxIoThreads);
            ThreadPool.GetAvailableThreads(out int freeWorkerThreads, out int freeIoThreads);
            ThreadPool.GetMinThreads(out int minWorkerThreads, out int minIoThreads);

            int busyIoThreads = maxIoThreads - freeIoThreads;
            int busyWorkerThreads = maxWorkerThreads - freeWorkerThreads;

            iocp = $"(Busy={busyIoThreads},Free={freeIoThreads},Min={minIoThreads},Max={maxIoThreads})";
            worker = $"(Busy={busyWorkerThreads},Free={freeWorkerThreads},Min={minWorkerThreads},Max={maxWorkerThreads})";

#if NETCOREAPP
            workItems = $"(Threads={ThreadPool.ThreadCount},QueuedItems={ThreadPool.PendingWorkItemCount},CompletedItems={ThreadPool.CompletedWorkItemCount},Timers={Timer.ActiveCount})";
#else
            workItems = null;
#endif

            return busyWorkerThreads;
        }
    }
}
