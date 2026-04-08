using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    /// <summary>
    /// Evaluate the health of an endpoint.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(IConnectionMultiplexer multiplexer, EndPoint endpoint)
    {
        try
        {
            int timeout = (int)ProbeTimeout.TotalMilliseconds, success = 0, failure = 0, remaining = ProbeCount;
            while (remaining > 0)
            {
                HealthCheckResult probeResult;
                try
                {
                    var pendingProbe = Probe.CheckHealthAsync(this, multiplexer, endpoint);
                    probeResult = await pendingProbe.TimeoutAfter(timeout).ForAwait()
                        ? await pendingProbe.ForAwait() // completed
                        : HealthCheckResult.Unhealthy; // timeout
                }
                catch
                {
                    probeResult = HealthCheckResult.Unhealthy;
                }

                // update success/failure counts
                switch (probeResult)
                {
                    case HealthCheckResult.Healthy: success++; break;
                    case HealthCheckResult.Unhealthy: failure++; break;
                }
                HealthCheckProbeContext ctx = new(success, failure, --remaining);

                // evaluate the policy
                var policyResult = ProbePolicy.Evaluate(ctx);
                if (policyResult != HealthCheckResult.Inconclusive) return policyResult;

                if (probeResult is HealthCheckResult.Unhealthy && remaining > 0)
                {
                    // delay if appropriate
                    await Task.Delay(ProbeInterval).ConfigureAwait(false);
                }
            }

            // we got here without a result
            return HealthCheckResult.Inconclusive;
        }
        catch (Exception ex)
        {
            // if the health check utterly fails: that isn't a good sign
            Debug.WriteLine(ex.Message);
            return HealthCheckResult.Unhealthy;
        }
    }
}
