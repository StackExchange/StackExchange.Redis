using System;

namespace StackExchange.Redis;

/// <summary>
/// A latency entry as reported by the built-in LATENCY HISTORY command
/// </summary>
public readonly struct LatencyHistoryEntry
{
    internal static readonly ResultProcessor<LatencyHistoryEntry[]> ToArray = new Processor();

    private sealed class Processor : ArrayResultProcessor<LatencyHistoryEntry>
    {
        protected override bool TryParse(in RawResult raw, out LatencyHistoryEntry parsed)
        {
            if (raw.Type == ResultType.MultiBulk)
            {
                var items = raw.GetItems();
                if (items.Length >= 2
                    && items[0].TryGetInt64(out var timestamp)
                    && items[1].TryGetInt64(out var duration))
                {
                    parsed = new LatencyHistoryEntry(timestamp, duration);
                    return true;
                }
            }
            parsed = default;
            return false;
        }
    }

    /// <summary>
    /// The time at which this entry was recorded
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// The latency recorded for this event
    /// </summary>
    public int DurationMilliseconds { get; }

    internal LatencyHistoryEntry(long timestamp, long duration)
    {
        Timestamp = RedisBase.UnixEpoch.AddSeconds(timestamp);
        DurationMilliseconds = checked((int)duration);
    }
}
