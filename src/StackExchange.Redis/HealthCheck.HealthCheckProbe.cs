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
        public abstract Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IServer server);

        private static Task<HealthCheckResult>? _inconclusive, _healthy, _unhealthy;

        /// <summary>
        /// Reports a memoized probe that was skipped without being evaluated.
        /// </summary>
        protected internal static Task<HealthCheckResult> InconclusiveTask => _inconclusive ??= Task.FromResult(HealthCheckResult.Inconclusive);

        /// <summary>
        /// Reports a memoized probe that was healthy.
        /// </summary>
        protected internal static Task<HealthCheckResult> HealthyTask => _healthy ??= Task.FromResult(HealthCheckResult.Healthy);

        /// <summary>
        /// Reports a memoized probe that was unhealthy.
        /// </summary>
        protected internal static Task<HealthCheckResult> UnhealthyTask => _unhealthy ??= Task.FromResult(HealthCheckResult.Unhealthy);
    }

    /// <summary>
    /// Describes a key-based (write) operation to perform as part of a health check.
    /// </summary>
    public abstract class KeyWriteHealthCheckProbe : HealthCheckProbe
    {
        /// <inheritdoc/>
        public override Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IServer server)
        {
            if (server.IsReplica) return InconclusiveTask;

            RedisKey key = server.InventKey("health-check/");
            if (key.IsNull) return InconclusiveTask;
            Debug.Assert(server.Multiplexer.GetServer(key).EndPoint == server.EndPoint, "Key was not routed to the correct endpoint");
            return CheckHealthAsync(healthCheck, server.Multiplexer.GetDatabase(), key);
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
