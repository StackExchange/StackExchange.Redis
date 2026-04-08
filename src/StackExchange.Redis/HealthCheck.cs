using System;

namespace StackExchange.Redis;

/// <summary>
/// Describes a health check to perform against instances.
/// </summary>
public sealed partial class HealthCheck
{
    internal HealthCheck(
        TimeSpan interval,
        int probeCount,
        TimeSpan probeTimeout,
        TimeSpan probeInterval,
        HealthCheckProbe probe,
        HealthCheckProbePolicy healthCheckProbePolicy)
    {
        Interval = interval;
        ProbeCount = probeCount;
        ProbeTimeout = probeTimeout;
        ProbeInterval = probeInterval;
        Probe = probe;
        ProbePolicy = healthCheckProbePolicy;
    }

    /// <summary>
    /// Gets the interval at which health checks should be performed.
    /// </summary>
    public TimeSpan Interval { get; }

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
    /// Create a builder base on this health check.
    /// </summary>
    public HealthCheckBuilder Builder() => new()
    {
        Interval = Interval,
        ProbeCount = ProbeCount,
        ProbeTimeout = ProbeTimeout,
        ProbeInterval = ProbeInterval,
        Probe = Probe,
        ProbePolicy = ProbePolicy,
    };

    /// <summary>
    /// Allows configuration of a <see cref="HealthCheck"/>.
    /// </summary>
    public class HealthCheckBuilder
    {
        /// <summary>
        /// Create a <see cref="HealthCheck"/> from this builder.
        /// </summary>
        public HealthCheck Build() => new(
            Interval,
            ProbeCount,
            ProbeTimeout,
            ProbeInterval,
            Probe,
            ProbePolicy);

        /// <inheritdoc cref="HealthCheck.Interval"/>
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);

        /// <inheritdoc cref="HealthCheck.ProbeCount"/>
        public int ProbeCount { get; set; } = 3;

        /// <inheritdoc cref="HealthCheck.ProbeTimeout"/>
        public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(2);

        /// <inheritdoc cref="HealthCheck.ProbeInterval"/>
        public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <inheritdoc cref="HealthCheck.Probe"/>
        public HealthCheckProbe Probe { get; set; } = HealthCheckProbe.Ping;

        /// <inheritdoc cref="HealthCheck.ProbePolicy"/>
        public HealthCheckProbePolicy ProbePolicy { get; set; } = HealthCheckProbePolicy.AnySuccess;
    }
}
