using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Describes a health check to perform against instances.
/// </summary>
/// <remarks>
/// Instances are immutable and safe to share between members; use <see cref="Builder"/> to configure
/// a custom check. Note that <em>how often</em> checks run is a group-level concern, configured via
/// <see cref="MultiGroupOptions.HealthCheckInterval"/>, not a property of the check itself.
/// </remarks>
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public sealed partial class HealthCheck
{
    internal const int DefaultProbeCount = 3;
    internal static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan DefaultProbeInterval = TimeSpan.FromMilliseconds(500);

    private static readonly HealthCheck DefaultInstance = new(
        enabled: true,
        DefaultProbeCount,
        DefaultProbeTimeout,
        DefaultProbeInterval,
        HealthCheckProbe.Ping,
        HealthCheckProbePolicy.AllSuccess);

    private static readonly HealthCheck DisabledInstance = new(
        enabled: false,
        DefaultProbeCount,
        DefaultProbeTimeout,
        DefaultProbeInterval,
        HealthCheckProbe.None,
        HealthCheckProbePolicy.AllSuccess);

    /// <summary>
    /// The default health check: three <see cref="HealthCheckProbe.Ping"/> probes, all of which must succeed.
    /// </summary>
    public static HealthCheck Default => DefaultInstance;

    /// <summary>
    /// No health check is performed; every check reports <see cref="HealthCheckResult.Inconclusive"/>, leaving
    /// member selection driven purely by the observed connectivity of each member (and by any circuit-breaker).
    /// </summary>
    public static HealthCheck None => DisabledInstance;

    private HealthCheck(
        bool enabled,
        int probeCount,
        TimeSpan probeTimeout,
        TimeSpan probeInterval,
        HealthCheckProbe probe,
        HealthCheckProbePolicy probePolicy)
    {
        IsEnabled = enabled;
        ProbeCount = probeCount;
        ProbeTimeout = probeTimeout;
        ProbeInterval = probeInterval;
        Probe = probe;
        ProbePolicy = probePolicy;
    }

    /// <inheritdoc/>
    public override string ToString() => IsEnabled
        ? $"{Probe.GetType().Name} x{ProbeCount} ({ProbePolicy.GetType().Name})"
        : "(disabled)";

    /// <summary>
    /// Whether this health check performs any probes; <c>false</c> only for <see cref="None"/>.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Gets the number of probes to perform for this health check.
    /// </summary>
    public int ProbeCount { get; }

    /// <summary>
    /// Gets the time that should be allowed for an individual probe to complete.
    /// </summary>
    public TimeSpan ProbeTimeout { get; }

    /// <summary>
    /// Gets the interval between failed probes.
    /// </summary>
    public TimeSpan ProbeInterval { get; }

    /// <summary>
    /// Gets the probe to use for this health check.
    /// </summary>
    public HealthCheckProbe Probe { get; }

    /// <summary>
    /// Gets the policy to use for this health check.
    /// </summary>
    public HealthCheckProbePolicy ProbePolicy { get; }

    /// <summary>
    /// Allows configuration of a <see cref="HealthCheck"/>.
    /// </summary>
    [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
    public sealed class Builder
    {
        /// <summary>
        /// Create a builder pre-populated with the default values.
        /// </summary>
        public Builder()
        {
        }

        /// <summary>
        /// Create a builder pre-populated from an existing <see cref="HealthCheck"/>.
        /// </summary>
        public Builder(HealthCheck healthCheck)
        {
            ProbeCount = healthCheck.ProbeCount;
            ProbeTimeout = healthCheck.ProbeTimeout;
            ProbeInterval = healthCheck.ProbeInterval;
            Probe = healthCheck.Probe;
            ProbePolicy = healthCheck.ProbePolicy;
        }

        /// <summary>
        /// The number of probes to perform for this health check.
        /// </summary>
        public int ProbeCount { get; set; } = DefaultProbeCount;

        /// <summary>
        /// The time that should be allowed for an individual probe to complete.
        /// </summary>
        public TimeSpan ProbeTimeout { get; set; } = DefaultProbeTimeout;

        /// <summary>
        /// The interval between failed probes.
        /// </summary>
        public TimeSpan ProbeInterval { get; set; } = DefaultProbeInterval;

        /// <summary>
        /// The probe to use for this health check.
        /// </summary>
        public HealthCheckProbe Probe { get; set; } = HealthCheckProbe.Ping;

        /// <summary>
        /// The policy to use for this health check.
        /// </summary>
        public HealthCheckProbePolicy ProbePolicy { get; set; } = HealthCheckProbePolicy.AllSuccess;

        /// <summary>
        /// Create a new health check instance.
        /// </summary>
        public HealthCheck Create()
        {
            if (ProbeCount < 1) throw new ArgumentOutOfRangeException(nameof(ProbeCount), ProbeCount, "At least one probe is required; use HealthCheck.None to disable health checks.");
            if (ProbeTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ProbeTimeout), ProbeTimeout, "A positive probe timeout is required.");
            if (ProbeInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ProbeInterval), ProbeInterval, "A non-negative probe interval is required.");
            if (Probe is null) throw new ArgumentNullException(nameof(Probe));
            if (ProbePolicy is null) throw new ArgumentNullException(nameof(ProbePolicy));

            // the total budget is expressed in int milliseconds when the check runs; validate that here,
            // rather than letting it overflow into a nonsensical (or negative) timeout later
            if (!TryComputeTotalTimeoutMillis(ProbeCount, ProbeTimeout, ProbeInterval, out _))
            {
                throw new ArgumentOutOfRangeException(nameof(ProbeTimeout), "The combined probe budget (ProbeCount, ProbeTimeout, ProbeInterval) is too large.");
            }

            // prefer the shared default instance when nothing has been customized
            if (ProbeCount == DefaultProbeCount
                && ProbeTimeout == DefaultProbeTimeout
                && ProbeInterval == DefaultProbeInterval
                && ReferenceEquals(Probe, HealthCheckProbe.Ping)
                && ReferenceEquals(ProbePolicy, HealthCheckProbePolicy.AllSuccess))
            {
                return DefaultInstance;
            }

            return new HealthCheck(enabled: true, ProbeCount, ProbeTimeout, ProbeInterval, Probe, ProbePolicy);
        }

        /// <summary>
        /// Create a new health check instance.
        /// </summary>
        public static implicit operator HealthCheck(Builder builder) => builder.Create();
    }
}
