using System;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// A latency entry as reported by the built-in LATENCY HISTORY command.
/// </summary>
public readonly struct LatencyHistoryEntry
{
    internal static readonly ResultProcessor<LatencyHistoryEntry[]> ToArray = new Processor();

    /// <summary>One <c>LATENCY HISTORY</c> element: when it happened, and how long it took.</summary>
    /// <param name="reader">Positioned on the element.</param>
    /// <param name="parsed">The entry.</param>
    /// <remarks>
    /// <b>Internal and static so both cores read it the same way.</b> The shipped processor below and the
    /// context surface's handler are two callers of this one walk; a server's own account of its latency
    /// should not depend on which core asked for it. Same argument as <c>Diagnostics.ParseInfo</c>.
    /// </remarks>
    internal static bool TryParseEntry(ref RespReader reader, out LatencyHistoryEntry parsed)
    {
        if (reader.IsAggregate
            && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var timestamp)
            && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var duration))
        {
            parsed = new LatencyHistoryEntry(timestamp, duration);
            return true;
        }
        parsed = default;
        return false;
    }

    private sealed class Processor : ArrayResultProcessor<LatencyHistoryEntry>
    {
        protected override bool TryParse(ref RespReader reader, out LatencyHistoryEntry parsed)
            => TryParseEntry(ref reader, out parsed);
    }

    /// <summary>
    /// The time at which this entry was recorded.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// The latency recorded for this event.
    /// </summary>
    public int DurationMilliseconds { get; }

    internal LatencyHistoryEntry(long timestamp, long duration)
    {
        Timestamp = RedisBase.UnixEpoch.AddSeconds(timestamp);
        DurationMilliseconds = checked((int)duration);
    }
}
