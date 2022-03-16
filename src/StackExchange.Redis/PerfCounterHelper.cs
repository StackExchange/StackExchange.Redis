using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace StackExchange.Redis
{
    internal static class PerfCounterHelper
    {
        private static readonly object staticLock = new();
        private static volatile PerformanceCounter? _cpu;
        private static volatile bool _disabled = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

#if NET5_0_OR_GREATER
        [SupportedOSPlatform("Windows")]
#endif
        public static bool TryGetSystemCPU(out float value)
        {
            value = -1;

            try
            {
                if (!_disabled && _cpu == null)
                {
                    lock (staticLock)
                    {
                        if (_cpu == null)
                        {
                            _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");

                            // First call always returns 0, so get that out of the way.
                            _cpu.NextValue();
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Some environments don't allow access to Performance Counters, so stop trying.
                _disabled = true;
            }
            catch (Exception e)
            {
                // this shouldn't happen, but just being safe...
                Trace.WriteLine(e);
            }

            if (!_disabled && _cpu != null)
            {
                value = _cpu.NextValue();
                return true;
            }
            return false;
        }

        internal static string GetThreadPoolAndCPUSummary(bool includePerformanceCounters)
        {
            GetThreadPoolStats(out string iocp, out string worker, out string? workItems);
            var cpu = includePerformanceCounters ? GetSystemCpuPercent() : "n/a";
            return $"IOCP: {iocp}, WORKER: {worker}, POOL: {workItems ?? "n/a"}, Local-CPU: {cpu}";
        }

        internal static string GetSystemCpuPercent() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TryGetSystemCPU(out float systemCPU)
                ? Math.Round(systemCPU, 2) + "%"
                : "unavailable";

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
            workItems = $"(Threads={ThreadPool.ThreadCount},QueuedItems={ThreadPool.PendingWorkItemCount},CompletedItems={ThreadPool.CompletedWorkItemCount})";
#else
            workItems = null;
#endif

            return busyWorkerThreads;
        }
    }
}
