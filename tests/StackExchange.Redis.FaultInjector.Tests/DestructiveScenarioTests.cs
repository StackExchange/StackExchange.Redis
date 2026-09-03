using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The scenarios that break things rather than move them: a shard dies, a node dies, a proxy dies.
/// </summary>
/// <remarks>
/// Held back from every unattended run until now, deliberately - these damage the cluster, and leaving a
/// broken environment behind costs more than the coverage is worth when nobody is watching. Run them at the
/// end of a cluster's life, supervised, which is what this is.
/// <para>
/// The client has nothing feature-specific to do here: a shard or node failing is not announced, so there is no
/// notification to act on and no handoff to perform. That is exactly why they are worth running. Everything
/// this feature adds - relaxed windows, handoffs, endpoint retirement - sits on top of ordinary reconnect and
/// topology handling, so a *silent* failure is the control: if recovery from an unannounced death regressed,
/// the announced paths are resting on sand.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "destructive")]
public class DestructiveScenarioTests(ReplicatedDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ReplicatedDatabaseFixture>
{
    private const string EnableVariable = "SER_FI_DESTRUCTIVE";

    /// <summary>
    /// Opt-in beyond the tier's own gate, because these cannot be undone.
    /// </summary>
    /// <remarks>
    /// <c>E2E_SCENARIO_TESTS</c> says "you may create and delete databases"; it does not say "you may kill
    /// nodes". A cluster that has to be re-provisioned is 10-15 minutes of somebody's afternoon, so the second
    /// gate is the difference between a deliberate session and an expensive surprise.
    /// </remarks>
    /// <summary>
    /// Which node <c>node_failure</c> kills; overridable, because which node matters and only the operator knows.
    /// </summary>
    /// <remarks>
    /// Not node 1: that is where the cluster's own management sits on a default install, so killing it takes
    /// the fault injector's access with it and the test cannot observe its own outcome.
    /// </remarks>
    private static int NodeToKill =>
        int.TryParse(Environment.GetEnvironmentVariable("SER_FI_NODE_TO_KILL"), out var node) && node > 0 ? node : 2;

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "true", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("shard_failure", "bdb_id")]
    [InlineData("proxy_failure", "bdb_id")]
    [InlineData("node_failure", "node_id")] // measured: this one is scoped to a *node*, and rejects bdb_id
    public async Task AnUnannouncedFailureIsSurvived(string action, string scope)
    {
        if (!Enabled) Assert.Skip($"set {EnableVariable}=true to run the destructive scenarios; they damage the cluster");

        // Provisioned rather than a template database: a shard dying wants replication behind it, and the
        // environment's own databases are created without it (and this cluster has none at all).
        fixture.RequireAvailable();
        var database = fixture.Database;
        Assert.NotNull(database);
        var cancellationToken = TestContext.Current.CancellationToken;
        log.WriteLine($"{action} against {database}");

        var clock = Stopwatch.StartNew();
        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig());
        var db = conn.GetDatabase();
        var key = $"fi-{action}";
        await db.StringSetAsync(key, "before");

        var drops = new List<ConnectionFailureType>();
        conn.ConnectionFailed += (_, e) =>
        {
            lock (drops) drops.Add(e.FailureType);
            log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  failed: {e.FailureType}");
        };
        conn.ConnectionRestored += (_, _) => log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  restored");
        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is PushMaintenanceEvent push)
            {
                log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {push.NotificationType} seq={push.SequenceId}");
            }
        };

        clock.Restart();
        try
        {
            await fixture.Injector.RunActionAsync(
                action,
                new Dictionary<string, object?>
                {
                    // The scope differs by action and the schema does not say so: shard_failure and
                    // proxy_failure take the database, node_failure takes a node - discovered by being told
                    // "Invalid parameter 'node_id': got None, expected valid node ID".
                    [scope] = scope == "bdb_id" ? database.BdbId.ToString() : NodeToKill.ToString(),
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // The parameters for this family are untyped in the injector's schema, so a rejection is a harness
            // finding rather than a client one - and recording the message is the point, since it is the only
            // documentation of what these actions want.
            Assert.Skip($"the injector would not run '{action}' with {scope}: {ScenarioSupport.Summarize(ex.Message)}");
        }

        log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  injector reports {action} finished");

        // Recovery is polled rather than timed: what matters is that the client gets there on its own, and how
        // long a real cluster takes to bring a shard back is not ours to assert.
        var recovered = await Poll.UntilAsync(
            () =>
            {
                try
                {
                    return db.StringGet(key) == "before";
                }
                catch (Exception ex) when (ex is RedisException or TimeoutException)
                {
                    return false;
                }
            },
            timeoutMilliseconds: 120_000,
            pollMilliseconds: 1000);

        lock (drops)
        {
            log.WriteLine(
                $"  +{clock.Elapsed.TotalSeconds,6:0.0}s  recovered={recovered} after {drops.Count} drop(s): "
                + (drops.Count == 0 ? "(none)" : string.Join(", ", drops.Distinct())));

            // Read the drop count before reading anything into a pass. Measured 2026-09-03: only
            // proxy_failure was visible to the client at all (one SocketClosed, restored ~8s later);
            // shard_failure on a replicated database and node_failure against a node we were not connected
            // through both produced *zero* drops. A run with no drops has proved that the deployment absorbed
            // the failure, which is worth knowing - but it has not exercised our recovery path, so do not
            // count it as coverage of one. Making node_failure bite needs the node that actually serves this
            // database, which means resolving the endpoint's address and matching it against the cluster's
            // node list; SER_FI_NODE_TO_KILL is the manual version of that.
            if (drops.Count == 0)
            {
                log.WriteLine("  note: the client never lost a connection, so this run tested the deployment rather than the client");
            }
        }

        Assert.True(recovered, $"the client should recover on its own from {action} without being told");

        // and the data survived, which is the deployment's promise rather than ours - stated because a
        // "recovery" that silently lost the key would otherwise pass
        Assert.Equal("before", await db.StringGetAsync(key));
    }
}
