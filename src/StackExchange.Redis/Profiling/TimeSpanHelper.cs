using System;
using System.Diagnostics;

namespace StackExchange.Redis.Profiling
{
    /// <summary>
    /// A helper class for dealing with Low/High Resolution Stopwatches and their ticks
    /// </summary>
    public class TimeSpanHelper
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="ticks"></param>
        /// <returns>A TimeSpan represented from the ticks</returns>
        public static TimeSpan FromStopwatchTicks(long ticks)
        {
            return Stopwatch.IsHighResolution ? TimeSpan.FromMilliseconds(ticks / ((double) Stopwatch.Frequency / 1000)) : TimeSpan.FromTicks(ticks);
        }
    }
}
