using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public partial interface IServer
{
    /// <summary>
    /// Start a new <c>HOTKEYS</c> profiling session.
    /// </summary>
    /// <param name="metrics">The metrics to record during this capture (defaults to "all").</param>
    /// <param name="count">The total number of operations to profile.</param>
    /// <param name="duration">The duration of this profiling session.</param>
    /// <param name="sampleRatio">Profiling frequency; effectively: measure every Nth command.</param>
    /// <param name="slots">The key-slots to record during this capture (defaults to "all").</param>
    /// <param name="flags">The command flags to use.</param>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    void HotKeysStart(
        HotKeysMetrics metrics = (HotKeysMetrics)~0, // everything by default
        long count = 0,
        TimeSpan duration = default,
        long sampleRatio = 1,
        short[]? slots = null,
        CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Start a new <c>HOTKEYS</c> profiling session.
    /// </summary>
    /// <param name="metrics">The metrics to record during this capture (defaults to "all").</param>
    /// <param name="count">The total number of operations to profile.</param>
    /// <param name="duration">The duration of this profiling session.</param>
    /// <param name="sampleRatio">Profiling frequency; effectively: measure every Nth command.</param>
    /// <param name="slots">The key-slots to record during this capture (defaults to "all").</param>
    /// <param name="flags">The command flags to use.</param>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    Task HotKeysStartAsync(
        HotKeysMetrics metrics = (HotKeysMetrics)~0, // everything by default
        long count = 0,
        TimeSpan duration = default,
        long sampleRatio = 1,
        short[]? slots = null,
        CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Stop the current <c>HOTKEYS</c> capture, if any.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    bool HotKeysStop(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Stop the current <c>HOTKEYS</c> capture, if any.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    Task<bool> HotKeysStopAsync(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Discard the last <c>HOTKEYS</c> capture data, if any.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    void HotKeysReset(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Discard the last <c>HOTKEYS</c> capture data, if any.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    Task HotKeysResetAsync(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Fetch the most recent <c>HOTKEYS</c> profiling data.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    /// <returns>The data captured during <c>HOTKEYS</c> profiling.</returns>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    HotKeysResult? HotKeysGet(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Fetch the most recent <c>HOTKEYS</c> profiling data.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    /// <returns>The data captured during <c>HOTKEYS</c> profiling.</returns>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    Task<HotKeysResult?> HotKeysGetAsync(CommandFlags flags = CommandFlags.None);
}

/// <summary>
/// Metrics to record during <c>HOTKEYS</c> profiling.
/// </summary>
[Flags]
[Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
public enum HotKeysMetrics
{
    /// <summary>
    /// Capture CPU time.
    /// </summary>
    Cpu = 1 << 0,

    /// <summary>
    /// Capture network bytes.
    /// </summary>
    Network = 1 << 1,
}

/// <summary>
/// Captured data from <c>HOTKEYS</c> profiling.
/// </summary>
[Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
public sealed partial class HotKeysResult
{
    /// <summary>
    /// Indicates whether the capture currently active.
    /// </summary>
    public bool TrackingActive { get; }

    /// <summary>
    /// Profiling frequency; effectively: measure every Nth command.
    /// </summary>
    public long SampleRatio { get; }

    /// <summary>
    /// The key slots active for this profiling session.
    /// </summary>
    public ReadOnlySpan<SlotRange> SelectedSlots => _selectedSlots;

    private readonly SlotRange[]? _selectedSlots;

    /// <summary>
    /// The total CPU measured for all commands in all slots.
    /// </summary>
    public TimeSpan TotalCpuTime => NonNegativeMicroseconds(TotalCpuTimeMicroseconds);

    private static TimeSpan NonNegativeMilliseconds(long ms)
        => TimeSpan.FromMilliseconds(Math.Max(ms, 0));

    private static TimeSpan NonNegativeMicroseconds(long us)
    {
        const long TICKS_PER_MICROSECOND = TimeSpan.TicksPerMillisecond / 1000; // 10, but: clearer
        return TimeSpan.FromTicks(Math.Max(us, 0) / TICKS_PER_MICROSECOND);
    }

    /// <summary>
    /// The total CPU measured for all commands in all slots.
    /// </summary>
    public long TotalCpuTimeMicroseconds { get; } = -1;

    /// <summary>
    /// The total network usage measured for all commands in all slots.
    /// </summary>
    public long TotalNetworkBytes { get; }

    /// <summary>
    /// The start time of the capture.
    /// </summary>
    public long CollectionStartTimeUnixMilliseconds { get; } = -1;

    /// <summary>
    /// The start time of the capture.
    /// </summary>
    public DateTime CollectionStartTime => RedisBase.UnixEpoch.AddMilliseconds(Math.Max(CollectionStartTimeUnixMilliseconds, 0));

    /// <summary>
    /// The duration of the capture.
    /// </summary>
    public long CollectionDurationMilliseconds { get; }

    /// <summary>
    /// The duration of the capture.
    /// </summary>
    public TimeSpan CollectionDuration => NonNegativeMilliseconds(CollectionDurationMilliseconds);

    /// <summary>
    /// The total user CPU time measured.
    /// </summary>
    public long TotalCpuTimeUserMilliseconds { get; } = -1;

    /// <summary>
    /// The total user CPU time measured.
    /// </summary>
    public TimeSpan TotalCpuTimeUser => NonNegativeMilliseconds(TotalCpuTimeUserMilliseconds);

    /// <summary>
    /// The total system CPU measured.
    /// </summary>
    public long TotalCpuTimeSystemMilliseconds { get; } = -1;

    /// <summary>
    /// The total system CPU measured.
    /// </summary>
    public TimeSpan TotalCpuTimeSystem => NonNegativeMilliseconds(TotalCpuTimeSystemMilliseconds);

    /// <summary>
    /// The total network data measured.
    /// </summary>
    public long TotalNetworkBytes2 { get; } // total-net-bytes vs net-bytes-all-commands-all-slots

    // Intentionally do construct a dictionary from the results; the caller is unlikely to be looking
    // for a particular key (lookup), but rather: is likely to want to list them for display; this way,
    // we'll preserve the server's display order.

    /// <summary>
    /// Hot keys, as measured by CPU activity.
    /// </summary>
    public ReadOnlySpan<MetricKeyCpu> CpuByKey => _cpuByKey;

    private readonly MetricKeyCpu[]? _cpuByKey;

    /// <summary>
    /// Hot keys, as measured by network activity.
    /// </summary>
    public ReadOnlySpan<MetricKeyBytes> NetworkBytesByKey => _networkBytesByKey;

    private readonly MetricKeyBytes[]? _networkBytesByKey;

    /// <summary>
    /// A hot key, as measured by CPU activity.
    /// </summary>
    /// <param name="key">The key observed.</param>
    /// <param name="durationMicroseconds">The time taken, in microseconds.</param>
    public readonly struct MetricKeyCpu(in RedisKey key, long durationMicroseconds)
    {
        private readonly RedisKey _key = key;

        /// <summary>
        /// The key observed.
        /// </summary>
        public RedisKey Key => _key;

        /// <summary>
        /// The time taken, in microseconds.
        /// </summary>
        public long DurationMicroseconds => durationMicroseconds;

        /// <summary>
        /// The time taken.
        /// </summary>
        public TimeSpan Duration => NonNegativeMicroseconds(durationMicroseconds);

        /// <inheritdoc/>
        public override string ToString() => $"{_key}: {Duration}";

        /// <inheritdoc/>
        public override int GetHashCode() => _key.GetHashCode() ^ durationMicroseconds.GetHashCode();

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is MetricKeyCpu other && _key.Equals(other.Key) && durationMicroseconds == DurationMicroseconds;
    }

    /// <summary>
    /// A hot key, as measured by network activity.
    /// </summary>
    /// <param name="key">The key observed.</param>
    /// <param name="bytes">The network activity, in bytes.</param>
    public readonly struct MetricKeyBytes(in RedisKey key, long bytes)
    {
        private readonly RedisKey _key = key;

        /// <summary>
        /// The key observed.
        /// </summary>
        public RedisKey Key => _key;

        /// <summary>
        /// The network activity, in bytes.
        /// </summary>
        public long Bytes => bytes;

        /// <inheritdoc/>
        public override string ToString() => $"{_key}: {bytes}B";

        /// <inheritdoc/>
        public override int GetHashCode() => _key.GetHashCode() ^ bytes.GetHashCode();

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is MetricKeyBytes other && _key.Equals(other.Key) && Bytes == other.Bytes;
    }
}
