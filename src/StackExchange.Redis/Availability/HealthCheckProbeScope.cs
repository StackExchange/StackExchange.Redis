using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// How often a <see cref="HealthCheckProbe"/> is invoked when a whole member is checked.
/// </summary>
[Experimental(Experiments.LagAwareFailover, UrlFormat = Experiments.UrlFormat)]
public enum HealthCheckProbeScope
{
    /// <summary>
    /// Once per server endpoint of the member; the default, and right for probes that test the connection
    /// itself (such as <see cref="HealthCheckProbe.Ping"/>).
    /// </summary>
    Endpoint = 0,

    /// <summary>
    /// Once per member, against one connected endpoint; for probes whose answer is about the database as a
    /// whole rather than any single endpoint, such as a call to a management API.
    /// </summary>
    Member = 1,
}
