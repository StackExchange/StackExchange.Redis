using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// <para>
/// The lag-aware failback check against a real Redis Enterprise Active-Active deployment: a group must not fail
/// back onto a member that is alive but behind, and must fail back once it has caught up.
/// </para>
/// <para>
/// No network fault is injected. The member is made to fall behind by pausing sync <em>into</em> it
/// (<c>crdt_sync: paused</c> through its cluster's REST API) while writes go to the other member - measured as
/// narrow and reliably undone, unlike <c>network_latency</c>, whose cleanup can fail and leave a cluster's DNS
/// and REST API unreachable (see notes/lag-aware/findings.md, section 10).
/// </para>
/// </summary>
[Trait("tier", "fault-injector")]
[Trait("scenario", "lag-aware")]
public class LagAwareFailbackScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper output)
    : IClassFixture<ExistingDatabaseFixture>
{
    private const string DatabaseName = "re-active-active";
    private static readonly TimeSpan HoldOff = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task RefusesToFailBackOntoAStaleMemberAndReturnsOnceCaughtUp()
    {
        var database = fixture.Require(DatabaseName);
        Assert.SkipWhen(database.MemberCount < 2, $"'{database.Key}' lists {database.MemberCount} endpoint(s); this needs two member databases");
        var environment = fixture.Environment;
        Assert.SkipWhen(environment.Clusters.Count < 2, $"env_output.json carries credentials for {environment.Clusters.Count} cluster(s); this needs both");

        // each member's own cluster: the one whose name its database host ends with ("redis-<port>.<cluster>")
        FaultInjectorEnvironment.ClusterCredentials ClusterOf(DatabaseMember member)
            => environment.Clusters.Single(c => member.Host.EndsWith("." + c.ClusterName, StringComparison.OrdinalIgnoreCase));
        var clusters = database.Members.Select(ClusterOf).ToArray();
        output.WriteLine($"member0={database.Members[0]} ({clusters[0]}), member1={database.Members[1]} ({clusters[1]})");

        ConnectionGroupMember Member(int index, double weight) => new(
            database.GetClientConfig(environment, MaintenanceNotificationMode.Disabled, index), $"member{index}")
        {
            Weight = weight,
            FailbackHealthCheck = HealthCheck.LagAware(new LagAwareOptions
            {
                Credentials = _ => new(new NetworkCredential(clusters[index].Username, clusters[index].Password)),
                CertificateValidation = ClusterRestClient.PinnedCertificateValidation(clusters[index], environment.ConfigDirectory),
            }),
        };

        // member1 is preferred, so "not returning to it" is the observable refusal
        var member0 = Member(0, weight: 1);
        var member1 = Member(1, weight: 9);
        MultiGroupOptions options = new MultiGroupOptions.Builder
        {
            HealthCheckInterval = TimeSpan.FromSeconds(1),
            FailbackDelay = TimeSpan.Zero, // only the lag check stands between member1 and a failback
        };

        using var rest = new ClusterRestClient(clusters[1], environment.ConfigDirectory);
        var uid = await rest.FindDatabaseUidAsync(DatabaseName);

        using var writer = new TestOutputWriter(output);
        await using var group = await ConnectionMultiplexer.ConnectGroupAsync([member0, member1], options, writer);
        await WaitForActiveAsync(group, member1, TimeSpan.FromSeconds(30), "the preferred, caught-up member should be selected first");

        var db = group.GetDatabase();
        RedisKey key = "fi-lag-aware-counter";
        await db.KeyDeleteAsync(key);

        bool paused = false;
        try
        {
            await rest.SetCrdtSyncAsync(uid, "paused");
            paused = true;
            output.WriteLine($"{Now()} sync into member1 (bdb {uid} on {clusters[1]}) paused");

            // leave member1 for member0, then release the override: member1 is the higher weight, alive and
            // connected, so the only thing that can keep the group off it is its failback check
            Assert.True(group.TryFailoverTo(member0));
            Assert.True(group.TryFailoverTo(null));

            var watch = Stopwatch.StartNew();
            long written = 0;
            while (watch.Elapsed < HoldOff)
            {
                written = await db.StringIncrementAsync(key); // on member0; member1 cannot see these
                Assert.True(member1.IsConnected, "member1 should stay connected: the refusal must be lag, not liveness");
                Assert.True(
                    group.ActiveMember == member0,
                    $"{Now()} the group moved to {group.ActiveMember} while member1 was behind (failback verified: {member1.FailbackVerified})");
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }

            output.WriteLine($"{Now()} held on member0 for {HoldOff.TotalSeconds:0}s, {written} writes member1 has not seen");
        }
        finally
        {
            if (paused)
            {
                await rest.SetCrdtSyncAsync(uid, "enabled");
                output.WriteLine($"{Now()} sync into member1 resumed");
            }
        }

        await WaitForActiveAsync(group, member1, TimeSpan.FromSeconds(60), "the group should fail back once member1 has caught up");
        var seen = (long)await member1.Multiplexer.GetDatabase().StringGetAsync(key);
        output.WriteLine($"{Now()} failed back to member1, which now has the counter at {seen}");
        Assert.True(seen > 0, "member1 should have received member0's writes by the time it was selected");
        await db.KeyDeleteAsync(key);
    }

    private async Task WaitForActiveAsync(IConnectionGroup group, ConnectionGroupMember expected, TimeSpan timeout, string because)
    {
        var watch = Stopwatch.StartNew();
        while (group.ActiveMember != expected && watch.Elapsed < timeout)
        {
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        output.WriteLine($"{Now()} active: {group.ActiveMember} after {watch.Elapsed.TotalSeconds:0.0}s (wanted {expected})");
        Assert.True(group.ActiveMember == expected, $"{because}; active is {group.ActiveMember} after {timeout.TotalSeconds:0}s");
    }

    private static string Now() => DateTime.UtcNow.ToString("HH:mm:ss.f");
}
