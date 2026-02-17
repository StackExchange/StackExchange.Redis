using System.Diagnostics;

namespace RESPite.Internal;

internal partial class DebugCounters
{
#if DEBUG
    private static int
        _tallyAsyncReadCount,
        _tallyAsyncReadInlineCount,
        _tallyDiscardFullCount,
        _tallyDiscardPartialCount,
        _tallyBufferCreatedCount,
        _tallyBufferRecycledCount,
        _tallyBufferMessageCount,
        _tallyBufferPinCount,
        _tallyBufferLeakCount;

    private static long
        _tallyReadBytes,
        _tallyDiscardAverage,
        _tallyBufferMessageBytes,
        _tallyBufferRecycledBytes,
        _tallyBufferMaxOutstandingBytes,
        _tallyBufferTotalBytes;
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

    [Conditional("DEBUG")]
    public static void OnBufferCreated()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyBufferCreatedCount);
#endif
    }

    [Conditional("DEBUG")]
    public static void OnBufferRecycled(int messageBytes)
    {
#if DEBUG
        Interlocked.Increment(ref _tallyBufferRecycledCount);
        var now = Interlocked.Add(ref _tallyBufferRecycledBytes, messageBytes);
        var outstanding = Volatile.Read(ref _tallyBufferMessageBytes) - now;

        while (true)
        {
            var oldOutstanding = Volatile.Read(ref _tallyBufferMaxOutstandingBytes);
            // loop until either it isn't an increase, or we successfully perform
            // the swap
            if (outstanding <= oldOutstanding
                || Interlocked.CompareExchange(
                    ref _tallyBufferMaxOutstandingBytes,
                    outstanding,
                    oldOutstanding) == oldOutstanding) break;
        }
#endif
    }

    [Conditional("DEBUG")]
    public static void OnBufferCompleted(int messageCount, int messageBytes)
    {
#if DEBUG
        Interlocked.Add(ref _tallyBufferMessageCount, messageCount);
        Interlocked.Add(ref _tallyBufferMessageBytes, messageBytes);
#endif
    }

    [Conditional("DEBUG")]
    public static void OnBufferCapacity(int bytes)
    {
#if DEBUG
        Interlocked.Add(ref _tallyBufferTotalBytes, bytes);
#endif
    }

    [Conditional("DEBUG")]
    public static void OnBufferPinned()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyBufferPinCount);
#endif
    }

    [Conditional("DEBUG")]
    public static void OnBufferLeaked()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyBufferLeakCount);
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

    public int BufferCreatedCount { get; } = Interlocked.Exchange(ref _tallyBufferCreatedCount, 0);
    public int BufferRecycledCount { get; } = Interlocked.Exchange(ref _tallyBufferRecycledCount, 0);
    public long BufferRecycledBytes { get; } = Interlocked.Exchange(ref _tallyBufferRecycledBytes, 0);
    public long BufferMaxOutstandingBytes { get; } = Interlocked.Exchange(ref _tallyBufferMaxOutstandingBytes, 0);
    public int BufferMessageCount { get; } = Interlocked.Exchange(ref _tallyBufferMessageCount, 0);
    public long BufferMessageBytes { get; } = Interlocked.Exchange(ref _tallyBufferMessageBytes, 0);
    public long BufferTotalBytes { get; } = Interlocked.Exchange(ref _tallyBufferTotalBytes, 0);
    public int BufferPinCount { get; } = Interlocked.Exchange(ref _tallyBufferPinCount, 0);
    public int BufferLeakCount { get; } = Interlocked.Exchange(ref _tallyBufferLeakCount, 0);
#endif
}
