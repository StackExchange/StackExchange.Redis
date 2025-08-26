using System.Diagnostics;
using System.Threading;

namespace Resp;
#if DEBUG
public partial class DebugCounters
#else
internal partial class DebugCounters
#endif
{
#if DEBUG
    private static int _tallyRead,
        _tallyAsyncRead,
        _tallyGrow,
        _tallyShuffleCount,
        _tallyCopyOutCount,
        _tallyDiscardFullCount,
        _tallyDiscardPartialCount;

    private static long _tallyShuffleBytes, _tallyReadBytes, _tallyCopyOutBytes, _tallyDiscardAverage;
#endif
    [Conditional("DEBUG")]
    internal static void OnRead(int bytes)
    {
#if DEBUG
        Interlocked.Increment(ref _tallyRead);
        if (bytes > 0) Interlocked.Add(ref _tallyReadBytes, bytes);
#endif
    }

    [Conditional("DEBUG")]
    internal static void OnAsyncRead(int bytes)
    {
#if DEBUG
        Interlocked.Increment(ref _tallyAsyncRead);
        if (bytes > 0) Interlocked.Add(ref _tallyReadBytes, bytes);
#endif
    }

    [Conditional("DEBUG")]
    internal static void OnGrow()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyGrow);
#endif
    }

    [Conditional("DEBUG")]
    internal static void OnShuffle(int bytes)
    {
#if DEBUG
        Interlocked.Increment(ref _tallyShuffleCount);
        if (bytes > 0) Interlocked.Add(ref _tallyShuffleBytes, bytes);
#endif
    }

    [Conditional("DEBUG")]
    internal static void OnCopyOut(int bytes)
    {
#if DEBUG
        Interlocked.Increment(ref _tallyCopyOutCount);
        if (bytes > 0) Interlocked.Add(ref _tallyCopyOutBytes, bytes);
#endif
    }

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

    private DebugCounters()
    {
    }
    public static DebugCounters Flush() => new();

#if DEBUG
    private static void EstimatedMovingRangeAverage(ref long field, long value)
    {
        var oldValue = Volatile.Read(ref field);
        var delta = (value - oldValue) >> 3; // is is a 7:1 old:new EMRA, using integer/bit math (alplha=0.125)
        if (delta != 0) Interlocked.Add(ref field, delta);
        // note: strictly conflicting concurrent calls can skew the value incorrectly; this is, however,
        // preferable to getting into a CEX squabble or requiring a lock - it is debug-only and just useful data
    }

    public int Read { get; } = Interlocked.Exchange(ref _tallyRead, 0);
    public int AsyncRead { get; } = Interlocked.Exchange(ref _tallyAsyncRead, 0);
    public long ReadBytes { get; } = Interlocked.Exchange(ref _tallyReadBytes, 0);
    public int Grow { get; } = Interlocked.Exchange(ref _tallyGrow, 0);
    public int ShuffleCount { get; } = Interlocked.Exchange(ref _tallyShuffleCount, 0);
    public long ShuffleBytes { get; } = Interlocked.Exchange(ref _tallyShuffleBytes, 0);
    public int CopyOutCount { get; } = Interlocked.Exchange(ref _tallyCopyOutCount, 0);
    public long CopyOutBytes { get; } = Interlocked.Exchange(ref _tallyCopyOutBytes, 0);
    public long DiscardAverage { get; } = Interlocked.Exchange(ref _tallyDiscardAverage, 32);
    public int DiscardFullCount { get; } = Interlocked.Exchange(ref _tallyDiscardFullCount, 0);
    public int DiscardPartialCount { get; } = Interlocked.Exchange(ref _tallyDiscardPartialCount, 0);
#endif
}
