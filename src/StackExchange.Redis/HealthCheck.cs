using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StackExchange.Redis;

/// <summary>
/// Describes a health check to perform against instances.
/// </summary>
public sealed partial class HealthCheck : ICloneable
{
    private static HealthCheck? _default;

    /// <summary>
    /// The default health check options. These options are immutable and cannot be modified; to customize, either
    /// use <see cref="Clone"/> to create a mutable copy, or create a new instance - and customize as needed.
    /// </summary>
    public static HealthCheck Default => _default ?? CreateDefault();

    private static HealthCheck CreateDefault()
    {
        var options = new HealthCheck();
        options.Freeze();
        // memoize, preferring to re-use the existing instance if we're competing (but since frozen: that's fine)
        return Interlocked.CompareExchange(ref _default, options, null) ?? options;
    }

    internal void Freeze() => _frozen = true;
    private bool _frozen;

    /// <summary>
    /// Create a mutable copy of this health check.
    /// </summary>
    public HealthCheck Clone() => new()
    {
        // note: do not copy _frozen
        Interval = Interval,
        ProbeCount = ProbeCount,
        ProbeTimeout = ProbeTimeout,
        ProbeInterval = ProbeInterval,
        Probe = Probe,
        ProbePolicy = ProbePolicy,
    };

    object ICloneable.Clone() => Clone();

    /// <summary>
    /// Create a new health check instance.
    /// </summary>
    public HealthCheck()
    {
        Interval = TimeSpan.FromSeconds(10);
        ProbeCount = 3;
        ProbeTimeout = TimeSpan.FromSeconds(2);
        ProbeInterval = TimeSpan.FromSeconds(1);
        Probe = HealthCheckProbe.Ping;
        ProbePolicy = HealthCheckProbePolicy.AnySuccess;
    }

    /// <summary>
    /// Gets or sets the interval at which health checks should be performed.
    /// </summary>
    public TimeSpan Interval
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Gets or sets the number of probes to perform for this health check.
    /// </summary>
    public int ProbeCount
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Gets or sets the time that should be allowed for an individual probe to complete.
    /// </summary>
    public TimeSpan ProbeTimeout
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Gets or sets the interval between failed probes.
    /// </summary>
    public TimeSpan ProbeInterval
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Gets or sets the probe to use for this health check.
    /// </summary>
    public HealthCheckProbe Probe
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Gets or sets the policy to use for this health check.
    /// </summary>
    public HealthCheckProbePolicy ProbePolicy
    {
        get;
        set => SetField(ref field, value);
    }

    // ReSharper disable once RedundantAssignment
    private void SetField<T>(ref T field, T value, [CallerMemberName] string caller = "")
    {
        if (_frozen) Throw(caller);
        field = value;

        static void Throw(string caller) => throw new InvalidOperationException($"{nameof(HealthCheck)}.{caller} cannot be modified once the object is in use.");
    }
}
