using System.Threading.Tasks;

namespace StackExchange.Redis.Availability;

public abstract partial class HealthCheckProbe
{
    /// <summary>
    /// Performs no test at all, always reporting <see cref="HealthCheckResult.Inconclusive"/>; this is the
    /// probe used by <see cref="HealthCheck.None"/>, and leaves member selection driven purely by the
    /// observed connectivity of each member.
    /// </summary>
    public static HealthCheckProbe None => NoneProbe.Instance;

    private sealed class NoneProbe : HealthCheckProbe
    {
        public static NoneProbe Instance { get; } = new();
        private NoneProbe() { }

        public override Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context) => InconclusiveTask;
    }
}
