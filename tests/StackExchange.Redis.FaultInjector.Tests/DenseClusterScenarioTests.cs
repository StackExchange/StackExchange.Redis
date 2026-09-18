using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// An OSS cluster with packed shards, so <c>add</c> and <c>slot-shuffle</c> have something to move.
/// </summary>
public sealed class DenseClusterFixture() : FaultInjectorFixture(DatabaseShape.OssClusterDense);

/// <summary>
/// The slot migrations the scenario setup legs cannot provision for.
/// </summary>
/// <remarks>
/// These looked like an environment limitation - the injector refuses them with "No node with multiple shards
/// found" - and they are not: the setup leg provisions one shard per node, so there is never a node to take a
/// shard *from*. Six shards with <c>dense</c> placement over three nodes gives two per node, and both effects
/// then run. The lesson generalises: a scenario that setup cannot arrange is still reachable by provisioning
/// the database ourselves and driving the run leg by <c>bdb_id</c>.
/// <para>
/// No scenario teardown here, deliberately. Teardown deletes the database, and this database belongs to the
/// fixture, which deletes it itself; and <c>migrate</c> excludes no nodes, so there is nothing to restore.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "cluster-family")]
public class DenseClusterScenarioTests(DenseClusterFixture fixture, ITestOutputHelper log)
    : IClassFixture<DenseClusterFixture>
{
    [Theory]
    [InlineData("add")]
    [InlineData("slot-shuffle")]
    [InlineData("remove-add")]
    public async Task SlotMigrationIsAnnouncedAndActedOn(string effect)
    {
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = fixture.Database;
        Assert.NotNull(database);
        log.WriteLine($"{effect} against {database}");

        var clock = Stopwatch.StartNew();
        var events = new List<PushMaintenanceEvent>();

        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig());
        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is PushMaintenanceEvent push)
            {
                lock (events) events.Add(push);
                log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {push.NotificationType} seq={push.SequenceId} {push.RawMessage}");
                foreach (var migration in push.SlotMigrations)
                {
                    log.WriteLine($"           slots {migration.RawSlots}: {migration.Source} -> {migration.Target}");
                }
            }
        };

        var db = conn.GetDatabase();
        var keys = Enumerable.Range(0, 32).Select(i => (RedisKey)$"fi-dense-{effect}-{i}").ToArray();
        foreach (var key in keys) await db.StringSetAsync(key, "before");

        clock.Restart();
        var query = new Dictionary<string, string?>
        {
            ["effect"] = effect,
            ["trigger"] = "migrate",
            ["bdb_id"] = database.BdbId.ToString(),
        };
        await fixture.Injector.PostScenarioAsync("slot-migrate", leg: null, query, cancellationToken: cancellationToken);
        log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  injector reports finished");

        int reads = 0, failures = 0;
        var deadline = clock.Elapsed + TimeSpan.FromSeconds(20);
        while (clock.Elapsed < deadline)
        {
            foreach (var key in keys)
            {
                try
                {
                    await db.StringGetAsync(key);
                    reads++;
                }
                catch (Exception ex) when (ex is RedisException or TimeoutException)
                {
                    failures++;
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        lock (events)
        {
            log.WriteLine($"  {events.Count} notification(s); {reads} reads, {failures} failures");
            Assert.NotEmpty(events);
            Assert.Contains(events, e => e.NotificationType is MaintenanceNotificationType.SlotMigrating
                or MaintenanceNotificationType.SlotMigrated);
        }

        Assert.True(reads > 0, "no reads succeeded during the migration");
        foreach (var key in keys) Assert.Equal("before", await db.StringGetAsync(key));
    }
}
