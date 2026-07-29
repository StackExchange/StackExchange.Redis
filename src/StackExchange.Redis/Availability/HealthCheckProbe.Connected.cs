using System.Threading.Tasks;

namespace StackExchange.Redis.Availability;

public abstract partial class HealthCheckProbe
{
    /// <summary>
    /// Report health using the <see cref="IServer.IsConnected"/> property, without any additional tests.
    /// </summary>
    public static HealthCheckProbe IsConnected => ConnectedProbe.Instance;

    private sealed class ConnectedProbe : HealthCheckProbe
    {
        public static ConnectedProbe Instance { get; } = new();
        private ConnectedProbe() { }

        public override Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context)
            => context.Server.IsConnected ? HealthyTask : UnhealthyTask;
    }
}
