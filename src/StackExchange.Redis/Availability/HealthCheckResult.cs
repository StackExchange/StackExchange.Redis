using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Indicates the result of a health check.
/// </summary>
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
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
