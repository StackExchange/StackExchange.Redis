using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// A replicated database, provisioned by us - the only way to reach the failover family.
/// </summary>
/// <remarks>
/// The environment templates create databases with <c>replication: false</c>, and a failover needs a replica to
/// promote, so this is the one case where <c>create_database</c> earns its keep rather than being a
/// less-convenient alternative to the scenario setup legs.
/// </remarks>
public sealed class ReplicatedDatabaseFixture()
    : FaultInjectorFixture(new DatabaseShape("replicated", ProxyPolicy: "single", Replication: true, ShardCount: 2));

/// <summary>
/// The proxy and failover families: <c>FAILING_OVER</c>/<c>FAILED_OVER</c>, and a proxy restarting underneath us.
/// </summary>
[Trait("tier", "fault-injector")]
[Trait("scenario", "failover")]
public class FailoverScenarioTests(ReplicatedDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ReplicatedDatabaseFixture>
{
    [Fact]
    public async Task FailoverIsAnnouncedAndSurvived()
    {
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = fixture.Database;
        Assert.NotNull(database);
        log.WriteLine($"provisioned {database}");

        var clock = Stopwatch.StartNew();
        var events = new List<PushMaintenanceEvent>();

        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig());
        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is PushMaintenanceEvent push)
            {
                lock (events) events.Add(push);
                log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {push.NotificationType} seq={push.SequenceId} {push.RawMessage}");
            }
        };

        var db = conn.GetDatabase();
        await db.StringSetAsync("fi-failover", "before");

        clock.Restart();
        try
        {
            await fixture.Injector.RunActionAsync(
                "failover",
                new Dictionary<string, object?> { ["bdb_id"] = database.BdbId.ToString() },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // The failover action's parameters are untyped in the schema, so a rejection here is a harness
            // problem rather than a client finding; say so plainly instead of reporting it as a product failure.
            Assert.Skip($"the injector would not run 'failover' against bdb {database.BdbId}: {ScenarioSupport.Summarize(ex.Message)}");
        }

        log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  injector reports the failover finished");

        var deadline = clock.Elapsed + TimeSpan.FromSeconds(45);
        while (clock.Elapsed < deadline)
        {
            try
            {
                await db.PingAsync();
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  ping failed: {ex.GetType().Name}");
            }

            await Task.Delay(1000, cancellationToken);
        }

        lock (events)
        {
            log.WriteLine($"  {events.Count} notification(s): {string.Join(", ", events.Select(e => e.NotificationType))}");

            // A failover is the one event whose *pair* we have never observed end to end from a client: Marc
            // captured the frames by hand, but nothing has watched SE.Redis receive them.
            Assert.NotEmpty(events);
            Assert.Contains(events, e => e.NotificationType is MaintenanceNotificationType.FailingOver
                or MaintenanceNotificationType.FailedOver
                or MaintenanceNotificationType.Migrating   // a failover on a proxied database moves shards too
                or MaintenanceNotificationType.Migrated
                or MaintenanceNotificationType.Moving);
        }

        // the data survived, which is the point of replication
        Assert.Equal("before", await db.StringGetAsync("fi-failover"));
    }
}

/// <summary>
/// The proxy process restarting: no topology change, no notification, just the socket going away.
/// </summary>
/// <remarks>
/// Included because it is the one disruption that is *purely* a connection event - nothing moves, nothing is
/// announced, and recovery is entirely the client's ordinary reconnect path. A useful control: if this fails,
/// the failures in the announced scenarios are not about notifications at all.
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "proxy-restart")]
public class ProxyRestartScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ExistingDatabaseFixture>
{
    [Theory]
    [InlineData("standalone")]
    [InlineData("cluster")]
    public async Task ProxyRestartIsSurvived(string key)
    {
        var database = fixture.Require(key);
        var cancellationToken = TestContext.Current.CancellationToken;

        var clock = Stopwatch.StartNew();
        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig(fixture.Environment));
        var db = conn.GetDatabase();
        await db.StringSetAsync($"fi-dmc-{key}", "before");

        int drops = 0;
        conn.ConnectionFailed += (_, e) =>
        {
            drops++;
            log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  connection failed: {e.FailureType}");
        };
        conn.ConnectionRestored += (_, _) => log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  connection restored");

        clock.Restart();
        // the one action with a typed parameter object in the injector's schema: RestartDmcParams { bdb_id }
        await fixture.Injector.RunActionAsync(
            "dmc_restart",
            new Dictionary<string, object?> { ["bdb_id"] = database.BdbId.ToString() },
            cancellationToken: cancellationToken);
        log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  DMC restart reported finished");

        // Recovery is asserted by polling rather than by waiting a fixed time, because a proxy restart is quick
        // and the interesting failure is "never comes back", not "takes a moment".
        Assert.True(
            await Poll.UntilAsync(() => TryRead(db, $"fi-dmc-{key}"), timeoutMilliseconds: 60_000),
            "the client should recover from the proxy restarting");

        log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  recovered after {drops} drop(s)");
        Assert.Equal("before", await db.StringGetAsync($"fi-dmc-{key}"));
    }

    private static bool TryRead(IDatabase db, string key)
    {
        try
        {
            return db.StringGet(key) == "before";
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return false;
        }
    }
}
