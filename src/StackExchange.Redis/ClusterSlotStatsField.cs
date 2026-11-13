using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Metrics that can appear in a <c>CLUSTER SLOT-STATS</c> per-slot map.
/// </summary>
internal enum ClusterSlotStatsField
{
    /// <summary>
    /// Unknown or unrecognized metric.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// The number of keys in the slot.
    /// </summary>
    [AsciiHash("key-count")]
    KeyCount,

    /// <summary>
    /// CPU time spent on the slot's keys, in microseconds.
    /// </summary>
    [AsciiHash("cpu-usec")]
    CpuMicroseconds,

    /// <summary>
    /// Memory used by the slot's keys, in bytes.
    /// </summary>
    [AsciiHash("memory-bytes")]
    MemoryBytes,

    /// <summary>
    /// Network bytes received for the slot's keys.
    /// </summary>
    [AsciiHash("network-bytes-in")]
    NetworkBytesIn,

    /// <summary>
    /// Network bytes sent for the slot's keys.
    /// </summary>
    [AsciiHash("network-bytes-out")]
    NetworkBytesOut,
}

/// <summary>
/// Metadata and parsing methods for ClusterSlotStatsField.
/// </summary>
internal static partial class ClusterSlotStatsFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out ClusterSlotStatsField field);
}
