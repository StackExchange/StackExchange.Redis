using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    /// <summary>
    /// Evaluate the health of the specified multiplexer, by evaluating all endpoints.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(IConnectionMultiplexer multiplexer)
        => multiplexer.IsConnected ? CheckHealthCoreAsync(multiplexer) : HealthCheckProbe.UnhealthyTask;

    private async Task<HealthCheckResult> CheckHealthCoreAsync(IConnectionMultiplexer multiplexer)
    {
        try
        {
            Task<HealthCheckResult>[] pending;
            if (multiplexer is IInternalConnectionMultiplexer internalMultiplexer)
            {
                var snapshot = internalMultiplexer.GetServerSnapshot();
                pending = GetReusablePending(ref _reusablePending, snapshot.Length);
                for (int i = 0; i < pending.Length; i++)
                {
                    pending[i] = CheckHealthAsync(snapshot[i].GetRedisServer(null));
                }
            }
            else
            {
                var servers = multiplexer.GetServers();
                pending = GetReusablePending(ref _reusablePending, servers.Length);
                for (int i = 0; i < pending.Length; i++)
                {
                    pending[i] = CheckHealthAsync(servers[i]);
                }
            }
            var result = await CollateAsync(pending, TotalTimeoutMillis()).ForAwait();

            // on successful completion (regardless of outcome), we can reuse the pending array
            PutReusablePending(ref _reusablePending, ref pending);
            return result;
        }
        catch
        {
            // definitely unhappy
            return HealthCheckResult.Unhealthy;
        }
    }

    internal int TotalTimeoutMillis()
    {
        int count = ProbeCount;
        if (count <= 0)
        {
            Debug.Fail("We shouldn't get as far as calculating timeouts with a non-positive probe count.");
            return 0;
        }

        TimeSpan probeTimeout = ProbeTimeout, probeInterval = ProbeInterval;

        // the first probe doesn't have an interval before it, the rest do
        var totalTicks = probeTimeout.Ticks
            + ((probeTimeout.Ticks + probeInterval.Ticks) * (count - 1));
        var millis = (int)TimeSpan.FromTicks(totalTicks).TotalMilliseconds;
        Debug.Assert(millis > 0, "Total timeout should be positive");
        return millis;
    }

    // apply timeout and collation logic to a group of probes
    internal static async Task<HealthCheckResult> CollateAsync(Task<HealthCheckResult>[] probes, int timeoutMilliseconds)
    {
        var pendingAll = Task.WhenAll(probes).ObserveErrors();
        int success = 0, failure = 0;

        if (await pendingAll.TimeoutAfter(timeoutMilliseconds).ForAwait())
        {
            // all completed inside timeout; all results should now be available
            for (int i = 0; i < probes.Length; i++)
            {
                var individualResult = await probes[i].ForAwait();
                switch (individualResult)
                {
                    case HealthCheckResult.Healthy: success++; break;
                    case HealthCheckResult.Unhealthy: failure++; break;
                }
            }
        }
        else
        {
            // timeout
            for (int i = 0; i < probes.Length; i++)
            {
                _ = probes[i].ObserveErrors();
            }
            throw new TimeoutException();
        }

        if (failure > 0) return HealthCheckResult.Unhealthy;
        if (success > 0) return HealthCheckResult.Healthy;
        return HealthCheckResult.Inconclusive;
    }

    private Task<HealthCheckResult>[]? _reusablePending;

    // The number of pending tasks is determined by the number of endpoints, which doesn't change frequently
    // (if at all); consequently, we can often re-use this buffer between health-checks, as long as we're careful.
    internal static Task<HealthCheckResult>[] GetReusablePending(ref Task<HealthCheckResult>[]? field, int count)
    {
        var result = Interlocked.Exchange(ref field, null);
        if (result is null || result.Length != count)
        {
            result = count == 0 ? [] : new Task<HealthCheckResult>[count];
        }
        return result;
    }

    internal static void PutReusablePending(ref Task<HealthCheckResult>[]? field, ref Task<HealthCheckResult>[] value)
    {
        if (value is { Length: > 0 })
        {
            Array.Clear(value, 0, value.Length);
            Interlocked.Exchange(ref field, value);
            value = [];
        }
    }

    /// <summary>
    /// Evaluate the health of an endpoint.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(IServer server)
        => server.IsConnected ? CheckHealthCoreAsync(server) : HealthCheckProbe.UnhealthyTask;

    private async Task<HealthCheckResult> CheckHealthCoreAsync(IServer server)
    {
        try
        {
            int timeout = (int)ProbeTimeout.TotalMilliseconds, success = 0, failure = 0, remaining = ProbeCount;
            while (remaining > 0)
            {
                HealthCheckResult probeResult;
                try
                {
                    var pendingProbe = Probe.CheckHealthAsync(this, server);
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
