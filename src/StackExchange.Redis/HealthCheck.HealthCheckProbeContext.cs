namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    /// <summary>
    /// Represents the context of a health check probe.
    /// </summary>
    public readonly struct HealthCheckProbeContext(int success, int failure, int remaining)
    {
        /// <inheritdoc/>
        public override string ToString() => $"Success: {Success}, Failure: {Failure}, Remaining: {Remaining}";

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
    }
}
