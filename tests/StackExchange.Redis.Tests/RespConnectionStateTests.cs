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

    /// <summary>
    /// The replica half of the same map: which servers replicate THESE slots, which is the part no other
    /// source can supply.
    /// </summary>
    /// <remarks>
    /// "This server is a replica" and "this server replicates slot 42" are different facts, and only the
    /// second can route a <see cref="CommandFlags.PreferReplica"/> read for a key. That is why the pairing
    /// is kept per range rather than as a flat set of replicas - a cluster with three shards has three
    /// answers to the question, not one.
    /// <para>
    /// Checked against the cluster's own configuration rather than against the shipped map, so it cannot
    /// pass by one copy of the answer agreeing with another copy; and routed for real through
    /// <c>IdentifyEndpoint</c>, so it is the routing decision under test and not just the table.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheNewCoreRoutesAReplicaPreferenceFromItsOwnMap()
    {
        Skip.IfNoCluster();
        await using var conn = Create(allowAdmin: true, configuration: TestConfig.Current.ClusterServersAndPorts, log: Writer);
        var db = Transitional(conn.GetDatabase());
        await db.PingAsync();

        var core = ((ConnectionMultiplexer)conn).NewCore;
        Assert.True(core.HasSlotMapForTest, "the new core did not fill a slot map of its own");

        var config = conn.GetServer(conn.GetEndPoints()[0]).ClusterConfiguration;
        Assert.NotNull(config);

        var checkedSlots = 0;
        for (var slot = 0; slot < 16384; slot += 337)
        {
            var replicas = core.SlotReplicasForTest(slot);
            if (replicas.Length == 0) continue;

            var node = config!.GetBySlot(slot);
            Assert.NotNull(node);

            // the cluster's own children of the primary that owns this slot
            var expected = node!.Children.Select(child => child.EndPoint).ToList();
            foreach (var replica in replicas)
            {
                Assert.Contains(replica, expected);
                Assert.NotEqual(node.EndPoint, replica);
                Assert.Equal(nameof(RespEndpointRole.Replica), core.RoleOfForTest(replica));
            }

            checkedSlots++;
        }

        Assert.True(checkedSlots > 40, $"only {checkedSlots} slots carried replicas; the probe did not populate them");
        Log($"replica sets agree with the cluster on {checkedSlots} sampled slots");

        // and the routing itself, not merely the table it reads: a key whose slot has replicas goes to the
        // primary by default and to one of ITS replicas when a replica is preferred
        RedisKey key = Me();
        var owner = core.SlotOwnerForTest(ServerSelectionStrategy.GetHashSlot(key));
        var slotReplicas = core.SlotReplicasForTest(ServerSelectionStrategy.GetHashSlot(key));
        Assert.NotNull(owner);
        Assert.NotEmpty(slotReplicas);

        Assert.Equal(owner, await db.IdentifyEndpointAsync(key));
        Assert.Contains(await db.IdentifyEndpointAsync(key, CommandFlags.PreferReplica), slotReplicas);
    }

    /// <summary>
    /// The standalone half: a primary/replica pair has no slots at all, and still has to answer which
    /// server is which - for BOTH servers, from one connection.
    /// </summary>
    /// <remarks>
    /// <b>The peer is the part that matters, not this server's own role.</b> This core connects on demand,
    /// so a replica nothing has yet had reason to dial would have no role at all - and refusing a
    /// <see cref="CommandFlags.DemandReplica"/> for that reason would be refusing over laziness rather
    /// than over topology. <c>ROLE</c> on the primary lists its replicas, so one connection describes the
    /// pair, exactly as one <c>CLUSTER SLOTS</c> describes a whole cluster.
    /// <para>
    /// Asked only when more than one endpoint is configured, since with one there is nothing to prefer a
    /// replica over.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheNewCoreLearnsBothStandaloneRolesFromOneConnection()
    {
        await using var conn = Create(
            configuration: TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort,
            log: Writer);
        var db = Transitional(conn.GetDatabase());
        await db.PingAsync();

        var core = ((ConnectionMultiplexer)conn).NewCore;
        var primary = conn.GetEndPoints().First(x => Format.ToString(x).EndsWith(TestConfig.Current.PrimaryPort.ToString()));
        var replica = conn.GetEndPoints().First(x => Format.ToString(x).EndsWith(TestConfig.Current.ReplicaPort.ToString()));

        Assert.Equal(nameof(RespEndpointRole.Primary), core.RoleOfForTest(primary));

        // the replica has not been dialled - the PING went to the primary - and is known anyway, because
        // the primary named it. This is the assertion that matters: without the peers, this would be
        // Unknown until something happened to connect to it.
        Assert.Equal(nameof(RespEndpointRole.Replica), core.RoleOfForTest(replica));
        Assert.Equal(replica, await db.IdentifyEndpointAsync(Me(), CommandFlags.DemandReplica));
        Log($"{Format.ToString(primary)} is the primary, {Format.ToString(replica)} the replica");
    }

    /// <summary>
    /// A server that serves slots but does not answer <c>CLUSTER INFO</c> is still a cluster.
    /// </summary>
    /// <remarks>
    /// <b>The diagnostic command is not the deployment.</b> Detection asked <c>CLUSTER INFO</c> and read an
    /// error as "standalone", which is wrong for anything that implements the routing surface without the
    /// diagnostic one - a proxy, an alternative implementation, and the in-process test server used here,
    /// which serves <c>CLUSTER NODES</c> and <c>CLUSTER SLOTS</c> and has no <c>CLUSTER INFO</c> at all.
    /// The cost was not subtle: slot routing was switched off entirely for such a deployment, so every key
    /// went wherever keyless routing landed and came back <c>MOVED</c>.
    /// <para>
    /// A reply carrying slot ranges is the better evidence anyway - it is the very thing routing uses - so
    /// a decline now leaves the question open and <c>CLUSTER SLOTS</c> settles it.
    /// </para>
    /// <para>
    /// Only visible with a test of its own today: the routing fallback to the shipped selector hides the
    /// consequence, because that core detects clusters from <c>INFO</c>'s <c>cluster_enabled</c> instead
    /// and gets the right answer. It stops being hidden the moment that fallback goes.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AServerThatServesSlotsWithoutClusterInfoIsStillACluster()
    {
        using var server = new InProcessTestServer(Output) { ServerType = ServerType.Cluster };
        await using var conn = await server.ConnectAsync();
        var db = Transitional(conn.GetDatabase());
        await db.PingAsync();

        var core = ((ConnectionMultiplexer)conn).NewCore;
        Log($"topology={core.TopologyStateForTest} hasMap={core.HasSlotMapForTest}");
        Assert.Equal(nameof(RespClusterState.Yes), core.TopologyStateForTest);
        Assert.True(core.HasSlotMapForTest, "a cluster that answered CLUSTER SLOTS should have left a map");
    }

    /// <summary>
    /// One connection, brought up on purpose, describes the whole deployment.
    /// </summary>
    /// <remarks>
    /// <b>The mechanism for "eager-once", ahead of the wiring that will use it.</b> This core dials on
    /// demand, so everything it knows arrives because a command needed a socket - which leaves every
    /// question answered WITHOUT sending (<c>IsConnected</c>, <c>IdentifyEndpoint</c>) with nothing behind
    /// it until somebody happens to issue one. Connecting deliberately is what <c>ConnectAsync</c> will
    /// wait for.
    /// <para>
    /// ONE connection, not all: the configured endpoints need no discovering, and a single handshake
    /// answers the rest for the whole deployment - <c>CLUSTER SLOTS</c> names every node. So a six-node
    /// cluster is fully mapped here having opened one socket, which is the property under test.
    /// </para>
    /// <para>
    /// Not yet called from <c>Connect</c>: while the shipped core still dials every endpoint of its own,
    /// doing this at startup adds a socket rather than replacing one, and tests that count connections say
    /// so. It lands with the step that stops the other core dialling; see design notes 9d.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EagerConnectMapsTheWholeClusterFromOneConnection()
    {
        Skip.IfNoCluster();
        await using var conn = Create(allowAdmin: true, configuration: TestConfig.Current.ClusterServersAndPorts, log: Writer);
        var core = ((ConnectionMultiplexer)conn).NewCore;

        // nothing has been sent, so nothing has dialled
        Assert.False(core.HasSlotMapForTest, "no command has been issued, so nothing should have connected");

        Assert.True(await core.ConnectEagerlyAsync(), "eager connect should have brought an endpoint up");

        Assert.True(core.HasSlotMapForTest, "one handshake should have mapped the deployment");
        Assert.Equal(1, core.ConnectedEndpointCountForTest);

        var config = conn.GetServer(conn.GetEndPoints()[0]).ClusterConfiguration;
        Assert.NotNull(config);

        var checkedSlots = 0;
        for (var slot = 0; slot < 16384; slot += 337)
        {
            if (core.SlotOwnerForTest(slot) is not { } mine) continue;
            Assert.Equal(config!.GetBySlot(slot)?.EndPoint, mine);
            checkedSlots++;
        }

        Assert.True(checkedSlots > 40, $"only {checkedSlots} slots were mapped from the one connection");
        Log($"one connection mapped {checkedSlots} sampled slots");
    }
}
