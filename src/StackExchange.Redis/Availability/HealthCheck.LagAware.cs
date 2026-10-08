using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

public sealed partial class HealthCheck
{
    /// <summary>
    /// A health check that asks the Redis Enterprise database availability API, once per member and once per
    /// pass, whether the database is available and (for an Active-Active member) caught up; intended as a
    /// <see cref="ConnectionGroupMember.FailbackHealthCheck"/>. See <see cref="HealthCheckProbe.LagAware"/>.
    /// </summary>
    /// <remarks>
    /// For different probe counts or timeouts, use
    /// <c>new HealthCheck.Builder { Probe = HealthCheckProbe.LagAware(options), ... }</c>.
    /// </remarks>
    [Experimental(Experiments.LagAwareFailover, UrlFormat = Experiments.UrlFormat)]
    public static HealthCheck LagAware(LagAwareOptions options)
        => new Builder { Probe = HealthCheckProbe.LagAware(options), ProbeCount = 1 }.Create();
}
