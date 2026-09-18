using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The OSS cluster notification family against a real cluster: <c>SMIGRATING</c>, <c>SMIGRATED</c>, and what
/// the client is supposed to do about them.
/// </summary>
/// <remarks>
/// Every <c>/slot-migrate</c> trigger requires <c>oss_cluster: true</c> with <c>all-master-shards</c>, so this
/// scenario family is the cluster half of the feature - the half whose reactions (re-reading the slot map,
/// re-subscribing stranded sharded channels) have until now only been exercised against the in-process fake.
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "cluster-family")]
public class ClusterFamilyScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ExistingDatabaseFixture>
{
    /// <summary>
    /// Slot migrations, in the four shapes the injector can produce.
    /// </summary>
    /// <remarks>
    /// The effects differ in what happens to the *endpoint list*, which is the thing a client has to keep up
    /// with: <c>remove-add</c> retires a node and introduces one, <c>remove</c> only retires, <c>add</c> only
    /// introduces, and <c>slot-shuffle</c> moves slots between nodes that both stay. So they cover the four
    /// ways a slot map can go stale, and only one of them (shuffle) leaves the endpoint set alone.
    /// </remarks>
    /// <remarks>
    /// Only <c>remove</c> here. The other effects need a database the setup leg will not provision, and get
    /// their own fixture below rather than skipping: <c>add</c> and <c>slot-shuffle</c> need a node holding
    /// several shards, and <c>remove-add</c> is not in <c>/slot-migrate/setup</c>'s effect enum at all.
    /// </remarks>
    [Theory]
    [InlineData("remove", "migrate")]
    public async Task SlotMigrationIsAnnouncedAndActedOn(string effect, string trigger)
    {
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scenario = await ScenarioRun.SetupAsync(
            fixture.Injector, "slot-migrate", effect, trigger, log.WriteLine,
            setupTrigger: "reshard", cancellationToken: cancellationToken);

        var database = scenario.Database;
        Assert.NotNull(database);
        ScenarioSupport.RequireEffectIsAchievable(scenario, effect);

        var clock = Stopwatch.StartNew();
        var events = new List<PushMaintenanceEvent>();

        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig(fixture.Environment));
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

        var endpointsBefore = conn.GetEndPoints().Length;
        log.WriteLine($"connected as {conn.GetServer(conn.GetEndPoints()[0]).ServerType} across {endpointsBefore} endpoint(s)");

        // keys spread across slots, so a migration of any shard moves something we are actually using
        var db = conn.GetDatabase();
        var keys = Enumerable.Range(0, 32).Select(i => (RedisKey)$"fi-{effect}-{i}").ToArray();
        foreach (var key in keys) await db.StringSetAsync(key, "before");

        clock.Restart();
        await ScenarioSupport.FireOrSkipAsync(scenario, effect, cancellationToken);
        log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  injector reports finished");

        // Keep using the connection while things move: a slot map that has gone stale shows up as MOVED
        // redirections, and the point of the feature is that we learn the new one rather than eating them.
        var deadline = clock.Elapsed + TimeSpan.FromSeconds(45);
        int reads = 0, failures = 0;
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
                    if (failures <= 3) log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  read failed: {ex.Message}");
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        lock (events)
        {
            log.WriteLine($"  {events.Count} notification(s); {reads} reads, {failures} failures; "
                + $"endpoints {endpointsBefore} -> {conn.GetEndPoints().Length}");

            // A slot migration is announced to every proxy, so any connection should see it. What is asserted is
            // only that we were told and understood it - the *reaction* is asserted separately below, because a
            // migration that moves no slot we hold would legitimately change nothing here.
            Assert.NotEmpty(events);
            Assert.All(events, e => Assert.NotEqual(MaintenanceNotificationType.None, e.NotificationType));
            Assert.Contains(events, e => e.NotificationType is MaintenanceNotificationType.SlotMigrating or MaintenanceNotificationType.SlotMigrated);

            // Any SMIGRATED that carried a payload must have parsed into something usable; an empty list would
            // mean we saw the frame and threw the contents away.
            foreach (var migrated in events.Where(e => e.NotificationType == MaintenanceNotificationType.SlotMigrated))
            {
                if (!string.IsNullOrEmpty(migrated.Payload) || migrated.SlotMigrations.Count > 0)
                {
                    Assert.NotEmpty(migrated.SlotMigrations);
                    Assert.All(migrated.SlotMigrations, m => Assert.NotNull(m.Source));
                }
            }
        }

        // and the client is still serving keys across the new topology
        Assert.True(reads > 0, "no reads succeeded at all during the migration");
        foreach (var key in keys) Assert.Equal("before", await db.StringGetAsync(key));
    }

    [Fact]
    public async Task ShardedSubscriptionSurvivesASlotMigration()
    {
        // D5's resubscription, against a real cluster. A sharded channel is bound to the node owning its slot,
        // so a migration strands the subscription: the server sends an unsolicited SUNSUBSCRIBE and stops
        // delivering. Recovering that is the client's job, and until now only the fake has tested it.
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scenario = await ScenarioRun.SetupAsync(
            fixture.Injector, "slot-migrate", "remove", "migrate", log.WriteLine,
            setupTrigger: "reshard", cancellationToken: cancellationToken);
        Assert.NotNull(scenario.Database);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(scenario.Database.GetClientConfig(fixture.Environment));
        var subscriber = conn.GetSubscriber();

        // several channels, because the migration moves the slots of one node: with one channel the test would
        // usually be asserting that nothing happened to it
        var channels = Enumerable.Range(0, 16).Select(i => RedisChannel.Sharded($"fi-shard-{i}")).ToArray();
        var received = new int[channels.Length];
        for (int i = 0; i < channels.Length; i++)
        {
            var index = i;
            await subscriber.SubscribeAsync(channels[i], (_, _) => Interlocked.Increment(ref received[index]));
        }

        Assert.True(await DeliversEverywhereAsync(subscriber, channels, received, cancellationToken),
            "every sharded channel should deliver before the migration, or the test proves nothing");
        log.WriteLine($"all {channels.Length} sharded channels delivering before the migration");

        await ScenarioSupport.FireOrSkipAsync(scenario, "remove", cancellationToken);

        // The recovery is deliberately allowed to be slow: it is driven by a jittered topology refresh and, as a
        // fallback, by a delayed resubscription sweep. What matters is that it happens without the caller doing
        // anything, not that it is instant.
        var recovered = await DeliversEverywhereAsync(subscriber, channels, received, cancellationToken, timeoutSeconds: 90);
        log.WriteLine($"delivery after migration: {(recovered ? "all channels" : "INCOMPLETE")}");
        Assert.True(recovered, "sharded subscriptions should deliver again once the topology settles");
    }

    /// <summary>
    /// Publishes to every channel and waits until each one has delivered at least one more message.
    /// </summary>
    /// <remarks>
    /// Publishing repeatedly rather than once: the publish itself routes by slot, so during a migration an
    /// individual publish can land on a node that no longer owns the slot. Retrying is what a real caller would
    /// do, and it keeps the test measuring *delivery* rather than the timing of one message.
    /// </remarks>
    private async Task<bool> DeliversEverywhereAsync(
        ISubscriber subscriber,
        RedisChannel[] channels,
        int[] received,
        CancellationToken cancellationToken,
        int timeoutSeconds = 20)
    {
        var baseline = channels.Select((_, i) => Volatile.Read(ref received[i])).ToArray();
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                if (Volatile.Read(ref received[i]) > baseline[i]) continue;
                try
                {
                    await subscriber.PublishAsync(channels[i], "ping");
                }
                catch (Exception ex) when (ex is RedisException or TimeoutException)
                {
                    // expected mid-migration; the retry is the point
                }
            }

            await Task.Delay(500, cancellationToken);
            if (channels.Select((_, i) => Volatile.Read(ref received[i]) > baseline[i]).All(x => x)) return true;
        }

        var missing = channels.Where((_, i) => Volatile.Read(ref received[i]) <= baseline[i]).Select(c => c.ToString());
        log.WriteLine($"  not delivering: {string.Join(", ", missing)}");
        return false;
    }
}
