using System;
using System.Diagnostics;

namespace StackExchange.Redis.Profiling
{
    /// <summary>
    /// A helper class for dealing with Low/High Resolution Stopwatches and their ticks
    /// </summary>
    internal static class TimeSpanHelper
    {
        /// <summary>
        /// Used to construct a timespan from ticks obtained using Stopwatch.GetTimestamp()
        /// </summary>
        /// <param name="ticks"></param>
        /// <returns>A TimeSpan constructed from the ticks</returns>
        internal static TimeSpan FromStopwatchTicks(long ticks)
        {
            return Stopwatch.IsHighResolution ? TimeSpan.FromMilliseconds(ticks / ((double) Stopwatch.Frequency / 1000)) : TimeSpan.FromTicks(ticks);
        }
    }
}
