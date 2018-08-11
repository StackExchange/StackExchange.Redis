using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StackExchange.Redis
{
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
    }
}
