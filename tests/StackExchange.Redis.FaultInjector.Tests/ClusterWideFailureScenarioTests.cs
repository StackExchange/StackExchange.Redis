using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The whole cluster goes away, and does not come back.
/// </summary>
/// <remarks>
/// Gated separately from the other destructive scenarios, and deliberately so: killing one node damages one
/// node, while this takes out every database on the cluster including anybody else's. It is the last thing you
/// run against a deployment, and **it ends the deployment** - measured 2026-09-03: <c>cluster_failure</c>
/// takes a <c>node_ids</c> list, stops those nodes, and restores nothing. With every node in the list the
/// cluster stays down; <c>rladmin</c> stops answering, and the environment needs re-provisioning.
/// <para>
/// So recovery is not assertable here, and the first version of this test was wrong to try: it asserted the
/// client would come back, which it cannot do when there is nothing to come back to. What *is* assertable is
/// that a total outage degrades cleanly - one reported failure, then commands that fail as Redis exceptions
/// rather than hanging, crashing, or throwing something unrelated - and that the multiplexer stays in a state
/// where it would recover if the deployment did.
/// </para>
/// <para>
/// <c>reset_cluster</c> is deliberately *not* here. It rebuilds the cluster from scratch, so the databases a
/// client was using cease to exist and there is no recovery to observe: it is a lifecycle operation for
/// whoever owns the environment, not a client test, and writing it as one would only produce a test that
/// asserts nothing.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "destructive")]
public class ClusterWideFailureScenarioTests(ReplicatedDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ReplicatedDatabaseFixture>
{
    private const string EnableVariable = "SER_FI_CLUSTER_FAILURE";

    [Fact]
    public async Task ATotalOutageFailsCleanlyRatherThanWedging()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip($"set {EnableVariable}=true to run this; it takes out every database on the cluster, not just ours");
        }

        fixture.RequireAvailable();
        var database = fixture.Database;
        Assert.NotNull(database);
        var cancellationToken = TestContext.Current.CancellationToken;
        log.WriteLine($"cluster_failure against {database}");

        var clock = Stopwatch.StartNew();
        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig());
        var db = conn.GetDatabase();
        const string Key = "fi-cluster-failure";
        await db.StringSetAsync(Key, "before");

        var drops = new List<ConnectionFailureType>();
        conn.ConnectionFailed += (_, e) =>
        {
            lock (drops) drops.Add(e.FailureType);
            log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  failed: {e.FailureType}");
        };
        conn.ConnectionRestored += (_, _) => log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  restored");

        // Every node, together: the action is "fail these nodes", not "fail the cluster" - it wants a
        // `node_ids` list, which the schema does not say and which an exception was kind enough to tell us.
        var nodes = await ClusterNodes.ListAsync(fixture.Injector, database.BdbId, cancellationToken);
        log.WriteLine($"cluster nodes: {string.Join(", ", nodes.Select(n => $"{n.Id}={n.Role}@{n.ExternalAddress}"))}");
        if (nodes.Count == 0) Assert.Skip("no nodes could be listed, so there is nothing to fail");

        clock.Restart();
        try
        {
            await fixture.Injector.RunActionAsync(
                "cluster_failure",
                new Dictionary<string, object?>
                {
                    ["bdb_id"] = database.BdbId.ToString(),
                    ["node_ids"] = nodes.Select(n => n.Id).ToArray(),
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Assert.Skip($"the injector would not run 'cluster_failure': {ScenarioSupport.Summarize(ex.Message)}");
        }

        log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  injector reports cluster_failure finished");

        // Probe for a while and record *how* it fails. Recovery is not expected - see the remarks - so the
        // interesting question is whether every failure is a Redis-family one, which is what a caller can
        // write a catch block for.
        var faults = new List<string>();
        var recovered = false;
        var deadline = clock.Elapsed + TimeSpan.FromSeconds(120);
        while (clock.Elapsed < deadline && !recovered)
        {
            try
            {
                recovered = db.StringGet(Key) == "before";
            }
            catch (Exception ex)
            {
                lock (faults) faults.Add(ex.GetType().Name);
            }

            await Task.Delay(2000, cancellationToken);
        }

        lock (drops)
        {
            log.WriteLine(
                $"  +{clock.Elapsed.TotalSeconds,6:0.0}s  recovered={recovered} after {drops.Count} drop(s): "
                + (drops.Count == 0 ? "(none)" : string.Join(", ", drops.Distinct())));
        }

        string[] observedFaults;
        lock (faults) observedFaults = [.. faults.Distinct()];
        log.WriteLine($"  faults: {(observedFaults.Length == 0 ? "(none)" : string.Join(", ", observedFaults))}");

        if (recovered)
        {
            // If the deployment does come back - a smaller node list, or somebody restarting it - then coming
            // back with it is the requirement, so say so rather than passing silently.
            Assert.Equal("before", await db.StringGetAsync(Key));
            return;
        }

        lock (drops) Assert.NotEmpty(drops); // the outage has to have been observed, or this proves nothing
        Assert.NotEmpty(observedFaults);
        Assert.All(observedFaults, name => Assert.Contains(name, new[]
        {
            nameof(RedisConnectionException),
            nameof(RedisTimeoutException),
            nameof(RedisServerException),
            nameof(TimeoutException),
        }));
    }
}
