using System;

namespace StackExchange.Redis;

/// <summary>
/// A latency entry as reported by the built-in LATENCY LATEST command
/// </summary>
public readonly struct LatencyLatestEntry
{
    internal static readonly ResultProcessor<LatencyLatestEntry[]> ToArray = new Processor();

    private sealed class Processor : ArrayResultProcessor<LatencyLatestEntry>
    {
        protected override bool TryParse(in RawResult raw, out LatencyLatestEntry parsed)
        {
            if (raw.Type == ResultType.MultiBulk)
            {
                var items = raw.GetItems();
                if (items.Length >= 4
                    && items[1].TryGetInt64(out var timestamp)
                    && items[2].TryGetInt64(out var duration)
                    && items[3].TryGetInt64(out var maxDuration))
                {
                    parsed = new LatencyLatestEntry(items[0].GetString()!, timestamp, duration, maxDuration);
                    return true;
                }
            }
            parsed = default;
            return false;
        }
    }

    /// <summary>
    /// The name of this event
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// The time at which this entry was recorded
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// The latency recorded for this event
    /// </summary>
    public int DurationMilliseconds { get; }

    /// <summary>
    /// The max latency recorded for all events
    /// </summary>
    public int MaxDurationMilliseconds { get; }

    internal LatencyLatestEntry(string eventName, long timestamp, long duration, long maxDuration)
    {
        EventName = eventName;
        Timestamp = RedisBase.UnixEpoch.AddSeconds(timestamp);
        DurationMilliseconds = checked((int)duration);
        MaxDurationMilliseconds = checked((int)maxDuration);
    }
}
