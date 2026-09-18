using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Describes an operation to perform as part of a health check.
/// </summary>
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public abstract partial class HealthCheckProbe
{
    /// <summary>
    /// Check the health of the specified endpoint.
    /// </summary>
    public abstract Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context);

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
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public abstract class KeyWriteHealthCheckProbe : HealthCheckProbe
{
    /// <inheritdoc/>
    public sealed override Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context)
    {
        var server = context.Server;
        if (server.IsReplica) return InconclusiveTask;

        RedisKey key = server.InventKey("health-check/");
        if (key.IsNull) return InconclusiveTask;
        Debug.Assert(server.Multiplexer.GetServer(key).EndPoint == server.EndPoint, "Key was not routed to the correct endpoint");
        return CheckHealthAsync(context, server.Multiplexer.GetDatabase(), key);
    }

    /// <summary>
    /// Check the health of the specified database using the provided key.
    /// </summary>
    public abstract Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, IDatabaseAsync database, RedisKey key);
}
