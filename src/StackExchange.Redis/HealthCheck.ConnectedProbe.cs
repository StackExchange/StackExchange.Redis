using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    public partial class HealthCheckProbe
    {
        /// <summary>
        /// Report health using the <see cref="IServer.IsConnected"/> property, without any additional tests.
        /// </summary>
        public static HealthCheckProbe IsConnected => ConnectedProbe.Instance;
    }

    private sealed class ConnectedProbe : HealthCheckProbe
    {
        public static ConnectedProbe Instance { get; } = new();
        private ConnectedProbe() { }

        public override Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IServer server)
            => server.IsConnected ? HealthyTask : UnhealthyTask;
    }
}
