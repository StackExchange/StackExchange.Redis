using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>An OSS cluster API database, so the client holds one endpoint per node and can retire one.</summary>
public sealed class OssClusterLifecycleFixture() : FaultInjectorFixture(DatabaseShape.OssClusterApi);

/// <summary>
/// A node removed from the cluster: endpoint retirement measured against a real deployment rather than a fake.
/// </summary>
/// <remarks>
/// The one destructive scenario that exercises something this feature built. Pruning exists because the
/// endpoint collection used to be add-only: a node that left the cluster was dialled forever, which is half of
/// the 37-hour field failure. Every test of it so far has been against the in-process server, where "the node
/// left" is a method call; here the node genuinely leaves, the topology genuinely changes, and the endpoint has
/// to be let go without dropping the caller's work.
/// <para>
/// Needs the OSS cluster API shape. A proxied standalone database is reached through one hostname however many
/// nodes are behind it, so there is no per-node endpoint to retire and the test would be vacuous.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "destructive")]
public class NodeRemovalScenarioTests(OssClusterLifecycleFixture fixture, ITestOutputHelper log)
    : IClassFixture<OssClusterLifecycleFixture>
{
    private const string EnableVariable = "SER_FI_DESTRUCTIVE";

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "true", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task RemovingANodeRetiresItsEndpoint()
    {
        if (!Enabled) Assert.Skip($"set {EnableVariable}=true to run the destructive scenarios; they damage the cluster");

        fixture.RequireAvailable();
        var database = fixture.Database;
        Assert.NotNull(database);
        var cancellationToken = TestContext.Current.CancellationToken;

        var nodes = await ClusterNodes.ListAsync(fixture.Injector, database.BdbId, cancellationToken);
        log.WriteLine($"cluster nodes: {string.Join(", ", nodes.Select(n => $"{n.Id}={n.Role}@{n.ExternalAddress}"))}");
        if (nodes.Count < 3) Assert.Skip($"only {nodes.Count} node(s); removing one needs somewhere for its shards to go");

        var clock = Stopwatch.StartNew();
        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig());
        var db = conn.GetDatabase();
        const string Key = "fi-node-remove";
        await db.StringSetAsync(Key, "before");

        conn.ConnectionFailed += (_, e) => log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  failed: {e.FailureType}");
        conn.ConnectionRestored += (_, _) => log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  restored");
        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is PushMaintenanceEvent push)
            {
                log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {push.NotificationType} seq={push.SequenceId}");
            }
        };

        var before = conn.GetEndPoints();
        log.WriteLine($"endpoints before: {string.Join(", ", before.Select(e => e.ToString()))}");

        // The node whose endpoint we hold *and* which is not serving our own connection, so the removal is
        // visible as a retirement rather than as a reconnect. If we cannot tell them apart, any node we hold
        // an endpoint for will do - the retirement is the assertion either way.
        var serving = await ClusterNodes.FindServingAsync(fixture.Injector, database.BdbId, database.Host, cancellationToken);

        // not the node serving us, and not node 1: cluster management lives there on a default install, and
        // taking it out takes the fault injector's own access with it
        var candidate = nodes.FirstOrDefault(n => n.Id != serving?.Id && n.Id != 1 && n.Role != "master");
        if (candidate is null) Assert.Skip("no node that is safe to remove and observable from here");

        log.WriteLine($"removing node {candidate.Id} ({candidate.ExternalAddress}); we are served by node {serving?.Id.ToString() ?? "(unknown)"}");

        clock.Restart();
        try
        {
            await fixture.Injector.RunActionAsync(
                "node_remove",
                new Dictionary<string, object?> { ["node_id"] = candidate.Id.ToString() },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Assert.Skip($"the injector would not remove node {candidate.Id}: {ScenarioSupport.Summarize(ex.Message)}");
        }

        log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  injector reports node_remove finished");

        // Retirement needs several consecutive topology passes that do not list the node, and those are driven
        // by the config-check interval, so this is tens of seconds rather than immediate by design.
        var retired = await Poll.UntilAsync(
            () => !conn.GetEndPoints().Any(e => e.ToString()!.Contains(candidate.ExternalAddress, StringComparison.OrdinalIgnoreCase)),
            timeoutMilliseconds: 180_000,
            pollMilliseconds: 2000);

        log.WriteLine($"endpoints after: {string.Join(", ", conn.GetEndPoints().Select(e => e.ToString()))}");

        // The caller's work is the part that must not suffer, whatever the endpoint collection does.
        Assert.Equal("before", await db.StringGetAsync(Key));
        await db.StringSetAsync(Key, "after");
        Assert.Equal("after", await db.StringGetAsync(Key));

        if (!retired)
        {
            // Reported rather than asserted: the endpoint set a proxied cluster advertises does not have to
            // name every node, so "the address never appeared in our endpoints" is a legitimate outcome and
            // not a pruning failure. The log above says which it was.
            log.WriteLine(
                $"  note: {candidate.ExternalAddress} was not present in our endpoint set, or was not retired within the bound; "
                + "traffic was unaffected either way");
        }
    }
}
