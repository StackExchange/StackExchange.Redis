using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;

namespace StackExchange.Redis.Tests;

// A health-check probe whose verdict is driven by the test: nominated endpoints report unhealthy on
// demand, everything else is healthy. This keeps a specific member deselected deterministically, even
// after its physical connection reconnects underneath us.
internal sealed class ControllableProbe : HealthCheck.HealthCheckProbe
{
    private volatile EndPoint? _down;

    public void MarkDown(EndPoint endpoint) => _down = endpoint;

    public override Task<HealthCheck.HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IServer server)
        => Equals(server.EndPoint, _down) ? UnhealthyTask : HealthyTask;
}
