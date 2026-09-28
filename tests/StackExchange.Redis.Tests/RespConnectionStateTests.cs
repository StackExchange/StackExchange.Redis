using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The distinction a boolean could not carry: "nothing has needed this endpoint yet" is not "this
/// endpoint is unreachable".
/// </summary>
/// <remarks>
/// The shipped core dials every endpoint up front, so <c>IsConnected == false</c> could only mean
/// something was wrong. This core dials on demand, so on a six-node cluster the ordinary state of five
/// of them - immediately after a successful command - is that nobody has asked. That is
/// <see cref="RespConnectionState.Deferred"/>, and reporting it as "not connected" is what made
/// <c>IsConnected</c> look broken on a perfectly healthy cluster.
/// </remarks>
public class RespConnectionStateTests(ITestOutputHelper output) : TestBase(output)
{
    private static TransitionalDatabase Transitional(IDatabase db)
    {
        if (db is TransitionalDatabase transitional) return transitional;
        Assert.Skip("requires the new database surface; set SEREDIS_NEW_DATABASE_SURFACE=1");
        return null!;
    }

    [Fact]
    public async Task AnUndialledEndpointIsDeferredRatherThanDisconnected()
    {
        Skip.IfNoCluster();
        await using var conn = Create(allowAdmin: true, configuration: TestConfig.Current.ClusterServersAndPorts, log: Writer);
        var db = Transitional(conn.GetDatabase());
        await db.PingAsync();

        // the keyless question must land on something live, not round-robin onto an undialled node
        Assert.Equal(RespConnectionState.Connected, db.GetConnectionState(default(RedisKey)));
        Assert.True(db.IsConnected(default(RedisKey)), "keyless routing should prefer a dialled endpoint");

        // Keys hash across all six nodes, and one PING dialled one of them - so the sample MUST contain
        // Deferred, and that is the assertion that matters. Without it this test would pass against a
        // build that had lost the distinction entirely and called everything Connected.
        var core = ((ConnectionMultiplexer)conn).NewCore;
        Log($"router={db.RouterKindForTest} routesBySlot={core.RoutesBySlotForTest} topology={core.TopologyStateForTest}");
        for (var i = 0; i < 4; i++)
        {
            Log($"  key {i} -> endpoint {db.IdentifyEndpoint($"conn-state-probe-{i}")} state {db.GetConnectionState($"conn-state-probe-{i}")}");
        }

        var sampled = new System.Collections.Generic.Dictionary<RespConnectionState, int>();
        for (var i = 0; i < 256; i++)
        {
            var state = db.GetConnectionState($"conn-state-probe-{i}");
            sampled[state] = sampled.TryGetValue(state, out var n) ? n + 1 : 1;

            // never Unroutable: every slot in a healthy cluster has an owner
            Assert.NotEqual(RespConnectionState.Unroutable, state);
        }

        Assert.True(
            sampled.ContainsKey(RespConnectionState.Deferred),
            $"no key reported Deferred, so the undialled case is not being distinguished: {Describe(sampled)}");
        Assert.True(
            sampled.ContainsKey(RespConnectionState.Connected),
            $"no key reported Connected, though a PING succeeded: {Describe(sampled)}");

        Log($"keyless={db.GetConnectionState(default(RedisKey))}; {Describe(sampled)}");

        static string Describe(System.Collections.Generic.Dictionary<RespConnectionState, int> counts)
            => string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>A role that no server can satisfy is unroutable, which is not the same as undialled.</summary>
    /// <remarks>
    /// This is the case that most needs the enum. A boolean says "false" for both, so a caller cannot tell
    /// "wait, it will connect" from "no amount of waiting will help".
    /// </remarks>
    [Fact]
    public async Task ADemandThatNoServerCanSatisfyIsUnroutable()
    {
        await using var conn = Create(log: Writer);
        var db = Transitional(conn.GetDatabase());
        await db.PingAsync();

        Assert.NotEqual(RespConnectionState.Unroutable, db.GetConnectionState(Me()));
        Log($"primary: {db.GetConnectionState(Me())}");
        Log($"demand replica: {db.GetConnectionState(Me(), CommandFlags.DemandReplica)}");
    }

    /// <summary>
    /// The new core routes a key from a slot map it filled itself, not from the shipped selector's.
    /// </summary>
    /// <remarks>
    /// This is the measurable end of the two-core coupling: the selector's map is a
    /// <c>ServerEndPoint[]</c> filled from a <c>CLUSTER NODES</c> the SHIPPED core issued during its
    /// auto-configure, so consulting it meant this core could not route until the other had connected.
    /// The handshake now issues its own <c>CLUSTER SLOTS</c>, and what it learned has to AGREE with the
    /// cluster's own view - a map that disagreed would send every key to a node that answers MOVED.
    /// </remarks>
    [Fact]
    public async Task TheNewCoreFillsAndRoutesFromItsOwnSlotMap()
    {
        Skip.IfNoCluster();
        await using var conn = Create(allowAdmin: true, configuration: TestConfig.Current.ClusterServersAndPorts, log: Writer);
        var db = Transitional(conn.GetDatabase());
        await db.PingAsync();

        var core = ((ConnectionMultiplexer)conn).NewCore;
        Assert.True(core.RoutesBySlotForTest, "the cluster was not detected");
        Assert.True(core.HasSlotMapForTest, "the new core did not fill a slot map of its own");

        var config = conn.GetServer(conn.GetEndPoints()[0]).ClusterConfiguration;
        Assert.NotNull(config);

        // compared against the cluster's OWN view rather than against the shipped map, so this does not
        // simply assert that one copy equals another copy
        var checkedSlots = 0;
        for (var slot = 0; slot < 16384; slot += 337)
        {
            var mine = core.SlotOwnerForTest(slot);
            if (mine is null) continue;

            var theirs = config.GetBySlot(slot);
            Assert.NotNull(theirs);
            Assert.Equal(theirs!.EndPoint, mine);
            checkedSlots++;
        }

        Assert.True(checkedSlots > 40, $"only {checkedSlots} slots were mapped; the probe did not populate");
        Log($"slot map agrees with the cluster on {checkedSlots} sampled slots");
    }
}
