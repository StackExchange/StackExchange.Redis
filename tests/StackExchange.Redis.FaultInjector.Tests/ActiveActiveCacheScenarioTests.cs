using System;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Caching;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// <para>
/// Client-side caching across an Active-Active switch and back, against a real deployment: the one place the
/// group's cache rules meet real replication. Needs the same <c>re-active-active</c> entry as
/// <see cref="ActiveActiveFailoverScenarioTests"/>, but no fault: the switch is manual
/// (<see cref="IConnectionGroup.TryFailoverTo"/>), because what is under test is what the cache does across a
/// switch, not how a switch is detected.
/// </para>
/// <para>
/// Two properties. <b>Read-your-writes on failback:</b> a write made through member1 must not be answered from
/// member0's cache afterwards, however far replication is behind - the in-process
/// <c>GroupCacheTests</c> prove the mechanism with servers that never replicate. <b>An inactive member still
/// hears invalidations:</b> member0's tracking connection stays registered while member1 is active, so a write
/// replicated to member0 from elsewhere reaches its cache - which the in-process servers cannot show.
/// </para>
/// </summary>
[Trait("tier", "fault-injector")]
[Trait("scenario", "active-active")]
public class ActiveActiveCacheScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper output)
    : IClassFixture<ExistingDatabaseFixture>
{
    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < limit)
        {
            if (await condition()) return true;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return await condition();
    }

    [Fact]
    public async Task CachedReadsSurviveASwitchAndBackWithoutGoingStale()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = fixture.Require("re-active-active");
        Assert.SkipWhen(
            database.MemberCount < 2,
            $"'{database.Key}' lists {database.MemberCount} endpoint(s); an Active-Active cache test needs two member databases");

        ConfigurationOptions MemberConfig(int index)
        {
            // Disabled for the reason ActiveActiveFailoverScenarioTests gives: one mechanism under test at a time
            var config = database.GetClientConfig(fixture.Environment, MaintenanceNotificationMode.Disabled, index);
            config.ClientCache = new CacheOptions { Admission = CacheAdmission.OnFirstMiss };
            return config; // RESP3, as GetClientConfig pins it; the cache requires it
        }

        var member0 = new ConnectionGroupMember(MemberConfig(0), "member0") { Weight = 1.0 };
        var member1 = new ConnectionGroupMember(MemberConfig(1), "member1") { Weight = 0.5 };

        using var writer = new TestOutputWriter(output);
        var group = await ConnectionMultiplexer.ConnectGroupAsync([member0, member1], log: writer);
        try
        {
            Assert.True(group.IsConnected, "unable to connect to the re-active-active deployment");
            Assert.Same(member0, group.ActiveMember);

            var typed = Assert.IsType<MultiGroupMultiplexer>(group);
            var cache0 = typed.GetMembers()[0].Multiplexer.ClientCache;
            Assert.NotNull(cache0);

            var db = group.GetDatabase();
            RedisKey key = "fi-aa-cache-" + Guid.NewGuid().ToString("N");
            await db.StringSetAsync(key, "v1", TimeSpan.FromMinutes(5));

            // cached on member0: a second read is served locally
            Assert.True(
                await WaitForAsync(async () => (string?)await db.Strings.GetAsync(key, cancellationToken: ct) == "v1" && cache0.Count > 0, TimeSpan.FromSeconds(10)),
                "the key never stayed in member0's cache");
            var stored = cache0.Stored;
            Assert.Equal("v1", (string?)await db.Strings.GetAsync(key, cancellationToken: ct));
            Assert.Equal(stored, cache0.Stored);

            // switch, write through member1, and come straight back - inside the replication lag, if there is one
            Assert.True(group.TryFailoverTo(member1));
            await db.StringSetAsync(key, "v2", TimeSpan.FromMinutes(5));
            Assert.True(group.TryFailoverTo(null));
            Assert.Same(member0, group.ActiveMember);

            // read-your-writes: member0's entry must not answer. member0's server may not have v2 yet; that is
            // replication, and an uncached read would see the same - but the CACHE must have asked
            var first = (string?)await db.Strings.GetAsync(key, cancellationToken: ct);
            output.WriteLine($"first read after failback: {first}");
            Assert.NotEqual(stored, cache0.Stored);

            // and member0 converges: replication reaches its server, its tracking connection is told, and the
            // cache stops serving v1 even if the read above cached it
            Assert.True(
                await WaitForAsync(async () => (string?)await db.Strings.GetAsync(key, cancellationToken: ct) == "v2", TimeSpan.FromSeconds(30)),
                "member0 never served v2");

            // an inactive member's cache hears invalidations too: cache v2 on member0, switch away, and write
            // through a separate multiplexer on member1 - not through the group, so no peer stamping reaches
            // member0's cache, and only replication plus member0's own tracking connection can invalidate it
            Assert.True(
                await WaitForAsync(async () => (string?)await db.Strings.GetAsync(key, cancellationToken: ct) == "v2" && cache0.Count > 0, TimeSpan.FromSeconds(10)),
                "v2 never stayed in member0's cache");
            Assert.True(group.TryFailoverTo(member1));
            await using (var direct = await ConnectionMultiplexer.ConnectAsync(MemberConfigWithoutCache(1)))
            {
                await direct.GetDatabase().StringSetAsync(key, "v3", TimeSpan.FromMinutes(5));
            }

            Assert.True(group.TryFailoverTo(null));
            Assert.True(
                await WaitForAsync(async () => (string?)await db.Strings.GetAsync(key, cancellationToken: ct) == "v3", TimeSpan.FromSeconds(30)),
                "member0 kept serving v2 after a write it could only learn of through replication");

            await db.KeyDeleteAsync(key);
        }
        finally
        {
            group.Dispose();
        }

        ConfigurationOptions MemberConfigWithoutCache(int index)
            => database.GetClientConfig(fixture.Environment, MaintenanceNotificationMode.Disabled, index);
    }
}
