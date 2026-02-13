using System.Diagnostics;

namespace RESPite.Internal;

internal partial class DebugCounters
{
#if DEBUG
    private static int
        _tallyAsyncReadCount,
        _tallyAsyncReadInlineCount,
        _tallyDiscardFullCount,
        _tallyDiscardPartialCount;

    private static long
        _tallyReadBytes,
        _tallyDiscardAverage;
#endif

    [Conditional("DEBUG")]
    public static void OnDiscardFull(long count)
    {
#if DEBUG
        if (count > 0)
        {
            Interlocked.Increment(ref _tallyDiscardFullCount);
            EstimatedMovingRangeAverage(ref _tallyDiscardAverage, count);
        }
#endif
    }

    [Conditional("DEBUG")]
    public static void OnDiscardPartial(long count)
    {
#if DEBUG
        if (count > 0)
        {
            Interlocked.Increment(ref _tallyDiscardPartialCount);
            EstimatedMovingRangeAverage(ref _tallyDiscardAverage, count);
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void OnAsyncRead(int bytes, bool inline)
    {
#if DEBUG
        Interlocked.Increment(ref inline ? ref _tallyAsyncReadInlineCount : ref _tallyAsyncReadCount);
        if (bytes > 0) Interlocked.Add(ref _tallyReadBytes, bytes);
#endif
    }

#if DEBUG
    private static void EstimatedMovingRangeAverage(ref long field, long value)
    {
        var oldValue = Volatile.Read(ref field);
        var delta = (value - oldValue) >> 3; // is is a 7:1 old:new EMRA, using integer/bit math (alplha=0.125)
        if (delta != 0) Interlocked.Add(ref field, delta);
        // note: strictly conflicting concurrent calls can skew the value incorrectly; this is, however,
        // preferable to getting into a CEX squabble or requiring a lock - it is debug-only and just useful data
    }

    public int AsyncReadCount { get; } = Interlocked.Exchange(ref _tallyAsyncReadCount, 0);
    public int AsyncReadInlineCount { get; } = Interlocked.Exchange(ref _tallyAsyncReadInlineCount, 0);
    public long ReadBytes { get; } = Interlocked.Exchange(ref _tallyReadBytes, 0);

    public long DiscardAverage { get; } = Interlocked.Exchange(ref _tallyDiscardAverage, 32);
    public int DiscardFullCount { get; } = Interlocked.Exchange(ref _tallyDiscardFullCount, 0);
    public int DiscardPartialCount { get; } = Interlocked.Exchange(ref _tallyDiscardPartialCount, 0);
#endif
}
