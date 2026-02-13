using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis;

public partial interface IServer
{
    /// <summary>
    /// Start a new <c>HOTKEYS</c> profiling session.
    /// </summary>
    /// <param name="metrics">The metrics to record during this capture (defaults to "all").</param>
    /// <param name="count">The number of keys to retain and report when <see cref="HotKeysGet"/> is invoked. If zero, the server default is used (currently 10).</param>
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
        int[]? slots = null,
        CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Start a new <c>HOTKEYS</c> profiling session.
    /// </summary>
    /// <param name="metrics">The metrics to record during this capture (defaults to "all").</param>
    /// <param name="count">The number of keys to retain and report when <see cref="HotKeysGet"/> is invoked. If zero, the server default is used (currently 10).</param>
    /// <param name="duration">The duration of this profiling session.</param>
    /// <param name="sampleRatio">Profiling frequency; effectively: measure every Nth command.</param>
    /// <param name="slots">The key-slots to record during this capture (defaults to "all" / "all on this node").</param>
    /// <param name="flags">The command flags to use.</param>
    [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
    Task HotKeysStartAsync(
        HotKeysMetrics metrics = (HotKeysMetrics)~0, // everything by default
        long count = 0,
        TimeSpan duration = default,
        long sampleRatio = 1,
        int[]? slots = null,
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
    /// No metrics.
    /// </summary>
    None = 0,

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
    // Note: names are intentionally chosen to align reasonably well with the Redis command output; some
    // liberties have been taken, for example "all-commands-all-slots-us" and "net-bytes-all-commands-all-slots"
    // have been named "AllCommandsAllSlotsTime" and "AllCommandsAllSlotsNetworkBytes" for consistency
    // with each-other.

    /// <summary>
    /// The metrics captured during this profiling session.
    /// </summary>
    public HotKeysMetrics Metrics { get; }

    /// <summary>
    /// Indicates whether the capture currently active.
    /// </summary>
    public bool TrackingActive { get; }

    /// <summary>
    /// Profiling frequency; effectively: measure every Nth command.
    /// </summary>
    public long SampleRatio { get; }

    /// <summary>
    /// Gets whether sampling is in use.
    /// </summary>
    public bool IsSampled => SampleRatio > 1;

    /// <summary>
    /// The key slots active for this profiling session.
    /// </summary>
    public ReadOnlySpan<SlotRange> SelectedSlots => _selectedSlots;

    private readonly SlotRange[]? _selectedSlots;

    /// <summary>
    /// Gets whether slot filtering is in use.
    /// </summary>
    public bool IsSlotFiltered =>
        NetworkBytesAllCommandsSelectedSlotsRaw >= 0; // this key only present if slot-filtering active

    /// <summary>
    /// The total CPU measured for all commands in all slots, without any sampling or filtering applied.
    /// </summary>
    public TimeSpan AllCommandsAllSlotsTime => NonNegativeMicroseconds(AllCommandsAllSlotsMicroseconds);

    internal long AllCommandsAllSlotsMicroseconds { get; } = -1;

    internal long AllCommandSelectedSlotsMicroseconds { get; } = -1;
    internal long SampledCommandsSelectedSlotsMicroseconds { get; } = -1;

    /// <summary>
    /// When slot filtering is used, this is the total CPU time measured for all commands in the selected slots.
    /// </summary>
    public TimeSpan? AllCommandsSelectedSlotsTime => AllCommandSelectedSlotsMicroseconds < 0
        ? null
        : NonNegativeMicroseconds(AllCommandSelectedSlotsMicroseconds);

    /// <summary>
    /// When sampling and slot filtering are used, this is the total CPU time measured for the sampled commands in the selected slots.
    /// </summary>
    public TimeSpan? SampledCommandsSelectedSlotsTime => SampledCommandsSelectedSlotsMicroseconds < 0
        ? null
        : NonNegativeMicroseconds(SampledCommandsSelectedSlotsMicroseconds);

    private static TimeSpan NonNegativeMicroseconds(long us)
    {
        const long TICKS_PER_MICROSECOND = TimeSpan.TicksPerMillisecond / 1000; // 10, but: clearer
        return TimeSpan.FromTicks(Math.Max(us, 0) / TICKS_PER_MICROSECOND);
    }

    /// <summary>
    /// The total network usage measured for all commands in all slots, without any sampling or filtering applied.
    /// </summary>
    public long AllCommandsAllSlotsNetworkBytes { get; }

    internal long NetworkBytesAllCommandsSelectedSlotsRaw { get; } = -1;
    internal long NetworkBytesSampledCommandsSelectedSlotsRaw { get; } = -1;

    /// <summary>
    /// When slot filtering is used, this is the total network usage measured for all commands in the selected slots.
    /// </summary>
    public long? AllCommandsSelectedSlotsNetworkBytes => NetworkBytesAllCommandsSelectedSlotsRaw < 0
        ? null
        : NetworkBytesAllCommandsSelectedSlotsRaw;

    /// <summary>
    /// When sampling and slot filtering are used, this is the total network usage measured for the sampled commands in the selected slots.
    /// </summary>
    public long? SampledCommandsSelectedSlotsNetworkBytes => NetworkBytesSampledCommandsSelectedSlotsRaw < 0
        ? null
        : NetworkBytesSampledCommandsSelectedSlotsRaw;

    internal long CollectionStartTimeUnixMilliseconds { get; } = -1;

    /// <summary>
    /// The start time of the capture.
    /// </summary>
    public DateTime CollectionStartTime =>
        RedisBase.UnixEpoch.AddMilliseconds(Math.Max(CollectionStartTimeUnixMilliseconds, 0));

    internal long CollectionDurationMicroseconds { get; }

    /// <summary>
    /// The duration of the capture.
    /// </summary>
    public TimeSpan CollectionDuration => NonNegativeMicroseconds(CollectionDurationMicroseconds);

    internal long TotalCpuTimeUserMicroseconds { get; } = -1;

    /// <summary>
    /// The total user CPU time measured in the profiling session.
    /// </summary>
    public TimeSpan? TotalCpuTimeUser => TotalCpuTimeUserMicroseconds < 0
        ? null
        : NonNegativeMicroseconds(TotalCpuTimeUserMicroseconds);

    internal long TotalCpuTimeSystemMicroseconds { get; } = -1;

    /// <summary>
    /// The total system CPU measured in the profiling session.
    /// </summary>
    public TimeSpan? TotalCpuTimeSystem => TotalCpuTimeSystemMicroseconds < 0
        ? null
        : NonNegativeMicroseconds(TotalCpuTimeSystemMicroseconds);

    /// <summary>
    /// The total CPU time measured in the profiling session (this is just <see cref="TotalCpuTimeUser"/> + <see cref="TotalCpuTimeSystem"/>).
    /// </summary>
    public TimeSpan? TotalCpuTime => TotalCpuTimeUser + TotalCpuTimeSystem;

    internal long TotalNetworkBytesRaw { get; } = -1;

    /// <summary>
    /// The total network data measured in the profiling session.
    /// </summary>
    public long? TotalNetworkBytes => TotalNetworkBytesRaw < 0
        ? null
        : TotalNetworkBytesRaw;

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

        internal long DurationMicroseconds => durationMicroseconds;

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
            => obj is MetricKeyCpu other && _key.Equals(other.Key) &&
               durationMicroseconds == other.DurationMicroseconds;
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
