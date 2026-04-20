using System;
using System.Runtime.CompilerServices;

namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    /// <summary>
    /// Represents the context of a health check probe.
    /// </summary>
    public readonly struct HealthCheckProbeContext(HealthCheckResult result, int success, int failure, int remaining, TimeSpan probeInterval)
    {
        /// <inheritdoc/>
        public override string ToString() => $"Result: {Result}, Success: {Success}, Failure: {Failure}, Remaining: {Remaining}, ProbeInterval: {ProbeInterval}";

        /// <summary>
        /// Gets the most recent result.
        /// </summary>
        public HealthCheckResult Result => result;

        /// <summary>
        /// Gets the number of successful health checks.
        /// </summary>
        public int Success => success;

        /// <summary>
        /// Gets the number of failed health checks.
        /// </summary>
        public int Failure => failure;

        /// <summary>
        /// Gets the number of remaining health checks.
        /// </summary>
        public int Remaining => remaining;

        /// <summary>
        /// Gets the interval to wait before the next probe attempt.
        /// </summary>
        public TimeSpan ProbeInterval => probeInterval;
    }
}
