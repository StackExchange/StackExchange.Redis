using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace StackExchange.Redis
{
#pragma warning disable CA1416 // windows only APIs; we've guarded against that
    internal static class PerfCounterHelper
    {
        private static readonly object staticLock = new object();
        private static volatile PerformanceCounter _cpu;
        private static volatile bool _disabled = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

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
            GetThreadPoolStats(out string iocp, out string worker);
            var cpu = includePerformanceCounters ? GetSystemCpuPercent() : "n/a";
            return $"IOCP: {iocp}, WORKER: {worker}, Local-CPU: {cpu}";
        }

        internal static string GetSystemCpuPercent()
        {
            return TryGetSystemCPU(out float systemCPU)
                ? Math.Round(systemCPU, 2) + "%"
                : "unavailable";
        }

        internal static int GetThreadPoolStats(out string iocp, out string worker)
        {
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxIoThreads);
            ThreadPool.GetAvailableThreads(out int freeWorkerThreads, out int freeIoThreads);
            ThreadPool.GetMinThreads(out int minWorkerThreads, out int minIoThreads);

            int busyIoThreads = maxIoThreads - freeIoThreads;
            int busyWorkerThreads = maxWorkerThreads - freeWorkerThreads;

            iocp = $"(Busy={busyIoThreads},Free={freeIoThreads},Min={minIoThreads},Max={maxIoThreads})";
            worker = $"(Busy={busyWorkerThreads},Free={freeWorkerThreads},Min={minWorkerThreads},Max={maxWorkerThreads})";
            return busyWorkerThreads;
        }
    }
#pragma warning restore CA1416 // windows only APIs; we've guarded against that
}
