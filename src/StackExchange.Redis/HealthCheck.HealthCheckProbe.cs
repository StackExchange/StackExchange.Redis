using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    /// <summary>
    /// Describes an operation to perform as part of a health check.
    /// </summary>
    public abstract partial class HealthCheckProbe
    {
        /// <summary>
        /// Check the health of the specified endpoint.
        /// </summary>
        public abstract Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IConnectionMultiplexer multiplexer, EndPoint endpoint);

        private static Task<HealthCheckResult>? _inconclusive;

        /// <summary>
        /// Reports a probe that was skipped without being evaluated.
        /// </summary>
        protected static Task<HealthCheckResult> Inconclusive => _inconclusive ??= Task.FromResult(HealthCheckResult.Inconclusive);
    }

    /// <summary>
    /// Describes a key-based (write) operation to perform as part of a health check.
    /// </summary>
    public abstract class KeyWriteHealthCheckProbe : HealthCheckProbe
    {
        /// <inheritdoc/>
        public override Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IConnectionMultiplexer multiplexer, EndPoint endpoint)
        {
            var server = multiplexer.GetServer(endpoint);
            if (server.IsReplica) return Inconclusive;

            RedisKey key = server.InventKey("health-check/"u8);
            if (key.IsNull) return Inconclusive;
            Debug.Assert(multiplexer.GetServer(key).EndPoint == endpoint, "Key was not routed to the correct endpoint");
            return CheckHealthAsync(healthCheck, multiplexer.GetDatabase(), key);
        }

        /// <summary>
        /// Check the health of the specified database using the provided key.
        /// </summary>
        public abstract Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IDatabaseAsync database, RedisKey key);
    }

    /// <summary>
    /// Indicates the result of a health check.
    /// </summary>
    public enum HealthCheckResult
    {
        /// <summary>
        /// The health check was skipped or could not be determined.
        /// </summary>
        Inconclusive,

        /// <summary>
        /// The health check was successful.
        /// </summary>
        Healthy,

        /// <summary>
        ///  The health check failed.
        /// </summary>
        Unhealthy,
    }
}
