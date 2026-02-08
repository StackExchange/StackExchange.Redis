using System;
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
    bool HotKeysStop(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Stop the current <c>HOTKEYS</c> capture, if any.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    Task<bool> HotKeysStopAsync(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Discard the last <c>HOTKEYS</c> capture data, if any.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    void HotKeysReset(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Discard the last <c>HOTKEYS</c> capture data, if any.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    Task HotKeysResetAsync(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Fetch the most recent <c>HOTKEYS</c> profiling data.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    /// <returns>The data captured during <c>HOTKEYS</c> profiling.</returns>
    HotKeysResult? HotKeysGet(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Fetch the most recent <c>HOTKEYS</c> profiling data.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    /// <returns>The data captured during <c>HOTKEYS</c> profiling.</returns>
    Task<HotKeysResult?> HotKeysGetAsync(CommandFlags flags = CommandFlags.None);
}

/// <summary>
/// Metrics to record during <c>HOTKEYS</c> profiling.
/// </summary>
[Flags]
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
    public SlotRange[] SelectedSlots { get; } = [];

    /// <summary>
    /// The total CPU measured for all commands in all slots.
    /// </summary>
    public TimeSpan TotalCpuTime => TimeSpan.FromMilliseconds(TotalCpuTimeMilliseconds);

    private long TotalCpuTimeMilliseconds { get; }

    /// <summary>
    /// The total network usage measured for all commands in all slots.
    /// </summary>
    public long TotalNetworkBytes { get; }

    private long CollectionStartTimeUnixMilliseconds { get; }

    /// <summary>
    /// The start time of the capture.
    /// </summary>
    public DateTime CollectionStartTime => RedisBase.UnixEpoch.AddMilliseconds(CollectionStartTimeUnixMilliseconds);

    private long CollectionDurationMilliseconds { get; }

    /// <summary>
    /// The duration of the capture.
    /// </summary>
    public TimeSpan CollectionDuration => TimeSpan.FromMilliseconds(CollectionDurationMilliseconds);

    private long TotalCpuTimeUserMilliseconds { get; }

    /// <summary>
    /// The total user CPU time measured.
    /// </summary>
    public TimeSpan TotalCpuTimeUser => TimeSpan.FromMilliseconds(TotalCpuTimeUserMilliseconds);

    private long TotalCpuTimeSystemMilliseconds { get; }

    /// <summary>
    /// The total system CPU measured.
    /// </summary>
    public TimeSpan TotalCpuTimeSystem => TimeSpan.FromMilliseconds(TotalCpuTimeSystemMilliseconds);

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
    public MetricKeyCpu[] CpuByKey { get; } = [];

    /// <summary>
    /// Hot keys, as measured by network activity.
    /// </summary>
    public MetricKeyBytes[] NetworkBytesByKey { get; } = [];

    private const long TicksPerMicroSeconds = TimeSpan.TicksPerMillisecond / 1000; // 10, but: clearer

    /// <summary>
    /// A hot key, as measured by CPU activity.
    /// </summary>
    /// <param name="key">The key observed.</param>
    /// <param name="microSeconds">The time taken, in microseconds.</param>
    public readonly struct MetricKeyCpu(in RedisKey key, long microSeconds)
    {
        private readonly RedisKey _key = key;

        /// <summary>
        /// The key observed.
        /// </summary>
        public RedisKey Key => _key;

        /// <summary>
        /// The time taken, in microseconds.
        /// </summary>
        public long MicroSeconds => microSeconds;

        /// <summary>
        /// The time taken.
        /// </summary>
        public TimeSpan Duration => TimeSpan.FromTicks(microSeconds / TicksPerMicroSeconds);

        /// <inheritdoc/>
        public override string ToString() => $"{_key}: {Duration}";

        /// <inheritdoc/>
        public override int GetHashCode() => _key.GetHashCode() ^ microSeconds.GetHashCode();

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is MetricKeyCpu other && _key.Equals(other.Key) && MicroSeconds == other.MicroSeconds;
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
