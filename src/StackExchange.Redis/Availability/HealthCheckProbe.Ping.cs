using System.Threading.Tasks;

namespace StackExchange.Redis.Availability;

public abstract partial class HealthCheckProbe
{
    /// <summary>
    /// Verify that the server is responsive by sending a PING command.
    /// </summary>
    public static HealthCheckProbe Ping => PingProbe.Instance;

    private sealed class PingProbe : HealthCheckProbe
    {
        public static PingProbe Instance { get; } = new();
        private PingProbe() { }

        public override async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context)
        {
            await context.Server.PingAsync();
            return HealthCheckResult.Healthy;
        }
    }
}
