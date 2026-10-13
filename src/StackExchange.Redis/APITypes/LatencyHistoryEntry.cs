using System;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// A latency entry as reported by the built-in LATENCY HISTORY command.
/// </summary>
public readonly struct LatencyHistoryEntry
{
    /// <summary>One <c>LATENCY HISTORY</c> element: when it happened, and how long it took.</summary>
    /// <param name="reader">Positioned on the element.</param>
    /// <param name="parsed">The entry.</param>
    /// <remarks>
    /// <b>Internal and static so every caller reads it the same way.</b> It was shared by the v3 processor
    /// and the context surface's handler while both cores existed; a server's own account of its latency
    /// should not depend on who asked for it. Same argument as <c>Diagnostics.ParseInfo</c>.
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
