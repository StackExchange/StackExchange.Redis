using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in a HOTKEYS response.
/// </summary>
internal enum HotKeysField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// Whether tracking is active.
    /// </summary>
    [AsciiHash("tracking-active")]
    TrackingActive,

    /// <summary>
    /// Sample ratio.
    /// </summary>
    [AsciiHash("sample-ratio")]
    SampleRatio,

    /// <summary>
    /// Selected slots.
    /// </summary>
    [AsciiHash("selected-slots")]
    SelectedSlots,

    /// <summary>
    /// All commands all slots microseconds.
    /// </summary>
    [AsciiHash("all-commands-all-slots-us")]
    AllCommandsAllSlotsUs,

    /// <summary>
    /// All commands selected slots microseconds.
    /// </summary>
    [AsciiHash("all-commands-selected-slots-us")]
    AllCommandsSelectedSlotsUs,

    /// <summary>
    /// Sampled command selected slots microseconds (singular).
    /// </summary>
    [AsciiHash("sampled-command-selected-slots-us")]
    SampledCommandSelectedSlotsUs,

    /// <summary>
    /// Sampled commands selected slots microseconds (plural).
    /// </summary>
    [AsciiHash("sampled-commands-selected-slots-us")]
    SampledCommandsSelectedSlotsUs,

    /// <summary>
    /// Network bytes all commands all slots.
    /// </summary>
    [AsciiHash("net-bytes-all-commands-all-slots")]
    NetBytesAllCommandsAllSlots,

    /// <summary>
    /// Network bytes all commands selected slots.
    /// </summary>
    [AsciiHash("net-bytes-all-commands-selected-slots")]
    NetBytesAllCommandsSelectedSlots,

    /// <summary>
    /// Network bytes sampled commands selected slots.
    /// </summary>
    [AsciiHash("net-bytes-sampled-commands-selected-slots")]
    NetBytesSampledCommandsSelectedSlots,

    /// <summary>
    /// Collection start time in Unix milliseconds.
    /// </summary>
    [AsciiHash("collection-start-time-unix-ms")]
    CollectionStartTimeUnixMs,

    /// <summary>
    /// Collection duration in milliseconds.
    /// </summary>
    [AsciiHash("collection-duration-ms")]
    CollectionDurationMs,

    /// <summary>
    /// Collection duration in microseconds.
    /// </summary>
    [AsciiHash("collection-duration-us")]
    CollectionDurationUs,

    /// <summary>
    /// Total CPU time user in milliseconds.
    /// </summary>
    [AsciiHash("total-cpu-time-user-ms")]
    TotalCpuTimeUserMs,

    /// <summary>
    /// Total CPU time user in microseconds.
    /// </summary>
    [AsciiHash("total-cpu-time-user-us")]
    TotalCpuTimeUserUs,

    /// <summary>
    /// Total CPU time system in milliseconds.
    /// </summary>
    [AsciiHash("total-cpu-time-sys-ms")]
    TotalCpuTimeSysMs,

    /// <summary>
    /// Total CPU time system in microseconds.
    /// </summary>
    [AsciiHash("total-cpu-time-sys-us")]
    TotalCpuTimeSysUs,

    /// <summary>
    /// Total network bytes.
    /// </summary>
    [AsciiHash("total-net-bytes")]
    TotalNetBytes,

    /// <summary>
    /// By CPU time in microseconds.
    /// </summary>
    [AsciiHash("by-cpu-time-us")]
    ByCpuTimeUs,

    /// <summary>
    /// By network bytes.
    /// </summary>
    [AsciiHash("by-net-bytes")]
    ByNetBytes,
}

/// <summary>
/// Metadata and parsing methods for HotKeysField.
/// </summary>
internal static partial class HotKeysFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out HotKeysField field);
}
