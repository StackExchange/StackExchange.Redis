using System;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// A latency entry as reported by the built-in LATENCY LATEST command.
/// </summary>
public readonly struct LatencyLatestEntry
{
    internal static readonly ResultProcessor<LatencyLatestEntry[]> ToArray = new Processor();

    /// <summary>One <c>LATENCY LATEST</c> element: the event, its last spike, and its worst.</summary>
    /// <param name="reader">Positioned on the element.</param>
    /// <param name="parsed">The entry.</param>
    /// <remarks><inheritdoc cref="LatencyHistoryEntry.TryParseEntry" path="/remarks"/></remarks>
    internal static bool TryParseEntry(ref RespReader reader, out LatencyLatestEntry parsed)
    {
        if (reader.IsAggregate && reader.TryMoveNext() && reader.IsScalar)
        {
            var eventName = reader.ReadString()!;

            if (reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var timestamp)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var duration)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var maxDuration))
            {
                parsed = new LatencyLatestEntry(eventName, timestamp, duration, maxDuration);
                return true;
            }
        }
        parsed = default;
        return false;
    }

    private sealed class Processor : ArrayResultProcessor<LatencyLatestEntry>
    {
        protected override bool TryParse(ref RespReader reader, out LatencyLatestEntry parsed)
            => TryParseEntry(ref reader, out parsed);
    }

    /// <summary>
    /// The name of this event.
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// The time at which this entry was recorded.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// The latency recorded for this event.
    /// </summary>
    public int DurationMilliseconds { get; }

    /// <summary>
    /// The max latency recorded for all events.
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
