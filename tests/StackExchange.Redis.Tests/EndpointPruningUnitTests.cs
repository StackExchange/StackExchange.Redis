using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Ageing endpoints out of the topology, and merging a node that arrived under two names. Both are policies
/// over the retirement primitive covered by <see cref="ServerRetirementUnitTests"/>.
/// </summary>
public class EndpointPruningUnitTests(ITestOutputHelper log)
{
    private const string Hostname = "host-1.redis.example.com";

    private static InProcessTestServer CreateServer(ITestOutputHelper log, ClusterEndpointType preferred = ClusterEndpointType.Ip)
        => new(log) { ServerType = ServerType.Cluster, PreferredEndpointType = preferred };

    /// <summary>
    /// Applies the topology generations that pruning is measured in - slot map *and* generation, as the
    /// production path does. Applying only the generation would leave the slot map pointing at a node the
    /// topology no longer lists, so it would never look idle and could never be pruned.
    /// </summary>
    private static async Task ApplyGenerationsAsync(IConnectionMultiplexer conn, EndPoint askOf, int count)
    {
        var mux = (ConnectionMultiplexer)conn;
        for (int i = 0; i < count; i++)
        {
            var slots = await conn.GetServer(askOf).ClusterSlotsAsync();
            var topology = ClusterTopology.From(slots);
            Assert.NotNull(topology);
            mux.UpdateClusterRange(topology);
            await mux.ApplyTopologyGenerationAsync(topology);
        }
    }

    /// <summary>
    /// The threshold itself, driven directly - deterministic, because it does not depend on how many
    /// generations anything else applied.
    /// </summary>
    /// <remarks>
    /// This is deliberately not asserted end-to-end. <c>OnMissingFromTopology</c> returns
    /// <c>generation - absentSince + 1</c>, which is the number of generations *elapsed* rather than the
    /// number of times this server was observed absent - so any topology application, from anywhere,
    /// advances it. A test that applies two generations and then asserts "not pruned yet" is asserting that
    /// nothing else applied one, which it cannot control: it passes on a fast machine and fails on a
    /// two-core runner. So the threshold is pinned here, and the end-to-end test below asserts only what it
    /// can honestly own - that an absent, idle node is eventually pruned.
    /// </remarks>
    [Fact]
    public async Task AbsenceIsCountedAsGenerationsElapsed()
    {
        using var server = CreateServer(log);
        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var target = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);

        // generation numbers are arbitrary here; what matters is the delta from the first absence
        Assert.Equal(1, target.OnMissingFromTopology(41));
        Assert.Equal(2, target.OnMissingFromTopology(42));
        Assert.Equal(3, target.OnMissingFromTopology(43));

        // being seen again clears it, so absences have to be consecutive to accumulate
        target.OnSeenInTopology(44);
        Assert.Equal(1, target.OnMissingFromTopology(45));

        // and a gap in generation numbers counts as the distance, not as one more absence - which is exactly
        // what an end-to-end test cannot control, since it does not own the generation counter
        target.OnSeenInTopology(49);
        Assert.Equal(1, target.OnMissingFromTopology(50));
        Assert.Equal(3, target.OnMissingFromTopology(52)); // two generations later, not two absences later
    }

    [Fact]
    public async Task NodeAbsentFromTopologyIsEventuallyPruned()
    {
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var doomed = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"prune-key", doomed);

        // defaultOnly, so the doomed node is *discovered* rather than configured - a configured endpoint is
        // exempt by design, and connecting to every toy node would make this test vacuous
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        // discovery is prompt but not synchronous with ConnectAsync returning: the node is learned from the
        // CLUSTER SLOTS reply during handshake, and registering it does not have to have finished on the
        // connecting thread. Asserting instantly passes on a fast machine and fails on a two-core runner
        Assert.True(await Poll.UntilAsync(() => conn.GetEndPoints().Contains(doomed)), $"{doomed} was never discovered");

        // hand its slot back, so the topology stops listing it - and it owns nothing, so it is prunable
        server.Migrate((RedisKey)"prune-key", server.DefaultEndPoint);

        // the *threshold* is pinned by AbsenceIsCountedAsGenerationsElapsed; what this owns is that applying
        // topology generations does eventually remove it. Bounded so a regression fails rather than hangs
        for (int i = 0; i < 10 && conn.GetEndPoints().Contains(doomed); i++)
        {
            await ApplyGenerationsAsync(conn, server.DefaultEndPoint, 1);
        }

        log.WriteLine(string.Join(", ", conn.GetEndPoints().Select(x => x.ToString())));
        Assert.DoesNotContain(doomed, conn.GetEndPoints());
    }

    [Fact]
    public async Task ConfiguredEndpointIsNeverPruned()
    {
        // the seed we need to bootstrap after a full rotation; absence must never remove it
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var second = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));

        var config = server.GetClientConfig();
        Assert.Contains(second, config.EndPoints); // explicitly configured, and serves no slots

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        await ApplyGenerationsAsync(conn, server.DefaultEndPoint, 5);

        Assert.Contains(second, conn.GetEndPoints());
    }

    [Fact]
    public async Task NodeCarryingSubscriptionsIsNotPruned()
    {
        // "not idle" is what protects a server that is still doing something for someone - see the remarks on
        // OnMissingFromTopology for why this is the test rather than a use-recency one
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var subscribed = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"sub-slot-key", subscribed);

        await using var conn = await server.ConnectAsync();
        var sub = conn.GetSubscriber();
        await sub.SubscribeAsync(RedisChannel.Literal(nameof(NodeCarryingSubscriptionsIsNotPruned)), (_, _) => { });

        server.Migrate((RedisKey)"sub-slot-key", server.DefaultEndPoint); // absent from the topology now
        await ApplyGenerationsAsync(conn, server.DefaultEndPoint, 5);

        // whichever server carries the subscription must survive; the other may legitimately go
        var survivors = conn.GetEndPoints();
        log.WriteLine(string.Join(", ", survivors.Select(x => x.ToString())));
        Assert.Contains(server.DefaultEndPoint, survivors);
    }

    [Fact]
    public async Task NodeStillOwningSlotsIsNotPruned()
    {
        // belt and braces: even if it somehow went missing from a reply, retiring a slot owner would leave
        // part of the keyspace unroutable
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var owner = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"owned-key", owner);

        await using var conn = await server.ConnectAsync();
        await ApplyGenerationsAsync(conn, server.DefaultEndPoint, 5);

        Assert.Contains(owner, conn.GetEndPoints());
        Assert.Equal("value", await Set(conn));

        static async Task<string?> Set(IConnectionMultiplexer conn)
        {
            var db = conn.GetDatabase();
            await db.StringSetAsync("owned-key", "value");
            return await db.StringGetAsync("owned-key");
        }
    }

    [Fact]
    public async Task SentinelDiscoveredServerIsNotPrunedByClusterAbsence()
    {
        // the catastrophic case a single rule would produce: in a sentinel deployment no cluster topology runs
        // at all, so everything looks absent. Provenance is what prevents it
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);

        // deliberately *not* a node of this cluster: sentinel knows of it, the cluster topology does not,
        // which is precisely the shape that a single prune rule would destroy
        var viaSentinel = new IPEndPoint(IPAddress.Loopback, port + 500);

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var mux = (ConnectionMultiplexer)conn;

        // register it as sentinel-discovered, exactly as the sentinel path does
        var sentinelServer = mux.GetServerEndPoint(viaSentinel, activate: false, provenance: ServerProvenance.Sentinel);
        Assert.Equal(ServerProvenance.Sentinel, sentinelServer.Provenance);

        await ApplyGenerationsAsync(conn, server.DefaultEndPoint, 5);
        Assert.Contains(viaSentinel, conn.GetEndPoints());
    }

    [Fact]
    public async Task ConfiguredEndpointSurvivesResolveDns()
    {
        // regression: ResolveDns rewrites the working set at startup, replacing configured names with the
        // addresses they resolved to, while the configuration keeps the names. Testing only the configuration
        // for "was this configured?" classifies a configured endpoint as discovered - and so prunable
        using var server = CreateServer(log);
        var config = server.GetClientConfig(defaultOnly: true);
        config.ResolveDns = true;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var endpoint = conn.GetEndPoints().Single();

        var sep = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(endpoint);
        log.WriteLine($"{endpoint} provenance={sep.Provenance}");
        Assert.Equal(ServerProvenance.Configured, sep.Provenance);

        await ApplyGenerationsAsync(conn, endpoint, 5);
        Assert.Contains(endpoint, conn.GetEndPoints());
    }

    [Fact]
    public async Task NewNodeIsDialledByTheAdvertisedForm()
    {
        // a certificate validates against a name, and where hostnames are preferred the advertised address may
        // not be routable at all - so a node we have never seen is dialled by the form the answering node
        // advertised, not by an address we happen to have been given alongside it
        using var server = CreateServer(log, ClusterEndpointType.Hostname);
        GetHost(server.DefaultEndPoint, out var port);
        var newcomer = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.SetHostname(newcomer, "host-2.redis.example.com");
        server.Migrate((RedisKey)"advertised-key", newcomer);

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        await ApplyGenerationsAsync(conn, server.DefaultEndPoint, 1);

        foreach (var ep in conn.GetEndPoints())
        {
            log.WriteLine($"endpoint: {ep}");
        }

        // reached by name, since that is what this cluster advertises (polled for the same reason as above:
        // the newcomer is discovered, not configured)
        Assert.True(
            await Poll.UntilAsync(() => conn.GetEndPoints().Any(ep => ep is DnsEndPoint { Host: "host-2.redis.example.com" })),
            "the newcomer was never discovered by name");
        Assert.DoesNotContain(conn.GetEndPoints(), ep => ep is IPEndPoint { Port: var p } && p == port + 1);
    }

    [Fact]
    public async Task DuplicateUnderTwoNamesIsMergedIntoOne()
    {
        // one node, two ServerEndPoints - what happens when something creates by a name we had not yet linked.
        // The merge must retire one and leave the retired name resolving to the survivor
        using var server = CreateServer(log, ClusterEndpointType.Ip);
        GetHost(server.DefaultEndPoint, out var port);

        // connect *before* the hostname is announced, so no alias for it is registered yet - which is the
        // state in which something can create a second server for a node we already hold
        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var mux = (ConnectionMultiplexer)conn;

        server.SetHostname(server.DefaultEndPoint, Hostname);
        var byName = new DnsEndPoint(Hostname, port);
        mux.GetServerEndPoint(byName, ServerProvenance.ClusterTopology, activate: false);
        Assert.Equal(2, conn.GetEndPoints().Length); // one node, two endpoints

        await ApplyGenerationsAsync(conn, server.DefaultEndPoint, 1);

        log.WriteLine(string.Join(", ", conn.GetEndPoints().Select(x => x.ToString())));
        Assert.Single(conn.GetEndPoints());

        // ...and the retired name still resolves, so a caller holding it is not broken
        Assert.NotNull(conn.GetServer(byName));
        Assert.Equal(server.DefaultEndPoint, conn.GetServer(byName).EndPoint);
    }
}
