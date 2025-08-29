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
    private static int _tallyReadCount,
        _tallyAsyncReadCount,
        _tallyAsyncReadInlineCount,
        _tallyWriteCount,
        _tallyAsyncWriteCount,
        _tallyAsyncWriteInlineCount,
        _tallyCopyOutCount,
        _tallyDiscardFullCount,
        _tallyDiscardPartialCount,
        _tallyPipelineFullAsyncCount,
        _tallyPipelineSendAsyncCount,
        _tallyPipelineFullSyncCount,
        _tallyBatchWriteCount,
        _tallyBatchWriteFullPageCount,
        _tallyBatchWritePartialPageCount,
        _tallyBatchWriteMessageCount;

    private static long _tallyWriteBytes, _tallyReadBytes, _tallyCopyOutBytes, _tallyDiscardAverage;
#endif
    [Conditional("DEBUG")]
    internal static void OnRead(int bytes)
    {
#if DEBUG
        Interlocked.Increment(ref _tallyReadCount);
        if (bytes > 0) Interlocked.Add(ref _tallyReadBytes, bytes);
#endif
    }

    public static void OnBatchWrite(int messageCount)
    {
#if DEBUG
        Interlocked.Increment(ref _tallyBatchWriteCount);
        if (messageCount != 0) Interlocked.Add(ref _tallyBatchWriteMessageCount, messageCount);
#endif
    }

    public static void OnBatchWriteFullPage()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyBatchWriteFullPageCount);
#endif
    }
    public static void OnBatchWritePartialPage()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyBatchWritePartialPageCount);
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
    internal static void OnWrite(int bytes)
    {
#if DEBUG
        Interlocked.Increment(ref _tallyWriteCount);
        if (bytes > 0) Interlocked.Add(ref _tallyWriteBytes, bytes);
#endif
    }

    [Conditional("DEBUG")]
    internal static void OnAsyncWrite(int bytes, bool inline)
    {
#if DEBUG
        Interlocked.Increment(ref inline ? ref _tallyAsyncWriteInlineCount : ref _tallyAsyncWriteCount);
        if (bytes > 0) Interlocked.Add(ref _tallyWriteBytes, bytes);
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

    [Conditional("DEBUG")]
    public static void OnPipelineFullAsync()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyPipelineFullAsyncCount);
#endif
    }

    [Conditional("DEBUG")]
    public static void OnPipelineSendAsync()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyPipelineSendAsyncCount);
#endif
    }

    [Conditional("DEBUG")]
    public static void OnPipelineFullSync()
    {
#if DEBUG
        Interlocked.Increment(ref _tallyPipelineFullSyncCount);
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

    public int ReadCount { get; } = Interlocked.Exchange(ref _tallyReadCount, 0);
    public int AsyncReadCount { get; } = Interlocked.Exchange(ref _tallyAsyncReadCount, 0);
    public int AsyncReadInlineCount { get; } = Interlocked.Exchange(ref _tallyAsyncReadInlineCount, 0);
    public long ReadBytes { get; } = Interlocked.Exchange(ref _tallyReadBytes, 0);

    public int WriteCount { get; } = Interlocked.Exchange(ref _tallyWriteCount, 0);
    public int AsyncWriteCount { get; } = Interlocked.Exchange(ref _tallyAsyncWriteCount, 0);
    public int AsyncWriteInlineCount { get; } = Interlocked.Exchange(ref _tallyAsyncWriteInlineCount, 0);
    public long WriteBytes { get; } = Interlocked.Exchange(ref _tallyWriteBytes, 0);
    public int CopyOutCount { get; } = Interlocked.Exchange(ref _tallyCopyOutCount, 0);
    public long CopyOutBytes { get; } = Interlocked.Exchange(ref _tallyCopyOutBytes, 0);
    public long DiscardAverage { get; } = Interlocked.Exchange(ref _tallyDiscardAverage, 32);
    public int DiscardFullCount { get; } = Interlocked.Exchange(ref _tallyDiscardFullCount, 0);
    public int DiscardPartialCount { get; } = Interlocked.Exchange(ref _tallyDiscardPartialCount, 0);
    public int PipelineFullAsyncCount { get; } = Interlocked.Exchange(ref _tallyPipelineFullAsyncCount, 0);
    public int PipelineSendAsyncCount { get; } = Interlocked.Exchange(ref _tallyPipelineSendAsyncCount, 0);
    public int PipelineFullSyncCount { get; } = Interlocked.Exchange(ref _tallyPipelineFullSyncCount, 0);
    public int BatchWriteCount { get; } = Interlocked.Exchange(ref _tallyBatchWriteCount, 0);
    public int BatchWriteFullPageCount { get; } = Interlocked.Exchange(ref _tallyBatchWriteFullPageCount, 0);
    public int BatchWritePartialPageCount { get; } = Interlocked.Exchange(ref _tallyBatchWritePartialPageCount, 0);
    public int BatchWriteMessageCount { get; } = Interlocked.Exchange(ref _tallyBatchWriteMessageCount, 0);
#endif
}
