using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    public partial class HealthCheckProbe
    {
        /// <summary>
        /// Verify that the server is responsive by sending a PING command.
        /// </summary>
        public static HealthCheckProbe Ping => PingProbe.Instance;
    }

    private sealed class PingProbe : HealthCheckProbe
    {
        public static PingProbe Instance { get; } = new();
        private PingProbe() { }

        public override async Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IServer server)
        {
            await server.PingAsync();
            return HealthCheckResult.Healthy;
        }
    }
}
