using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A sentinel-managed connection whose primary is demoted while every connection stays up. Sentinel's own
/// recovery levers - <c>+switch-master</c>, and killing the demoted node's clients - are exactly what is
/// missing here, so the only signal the client gets is the demoted node reporting itself as a replica.
/// </summary>
public class SentinelPrimaryDemotionUnitTests(ITestOutputHelper log)
{
    private const string ServiceName = "mymaster";

    // documentation-range addresses (RFC 5737): a node that is not in-process can never be reached by accident
    private static readonly EndPoint PrimaryA = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 6379);
    private static readonly EndPoint ReplicaB = new IPEndPoint(IPAddress.Parse("192.0.2.2"), 6379);
    private static readonly EndPoint NewNodeC = new IPEndPoint(IPAddress.Parse("192.0.2.3"), 6379);
    private static readonly EndPoint SentinelEndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 26379);

    [Fact]
    public async Task DemotedPrimaryFollowsSentinelToPrimaryAtUnknownAddress()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        Assert.Equal([PrimaryA], ConnectedPrimaries(conn));

        // the primary moves to a node the client has never been told about - a pod that came back on a new
        // address - and the client misses the announcement; no connection is dropped
        FailOverToNewNode(data);
        sentinel.Primary = NewNodeC;

        await AssertPrimaryBecomesAsync(conn, NewNodeC);
    }

    [Fact]
    public async Task DemotedPrimaryDoesNotReconfigureWhileSentinelStillNamesIt()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        int configurationChanges = 0;
        conn.ConfigurationChanged += (_, _) => Interlocked.Increment(ref configurationChanges);

        // sentinel has not caught up: it still names the node that is now a replica
        FailOverToNewNode(data);
        await UntilAsync(() => ConnectedPrimaries(conn).Length == 0, "the demotion was not noticed");

        // The property under test: the client keeps asking sentinel, but does not reconfigure every node to
        // elect a primary that does not exist. Anchor on the sentinel polls themselves rather than wall-clock
        // quiet: require a stretch of several polls with zero configuration changes. A straggler reconfigure
        // from the demotion itself re-baselines the stretch; the per-heartbeat reconfigure storm this guards
        // against would raise a change on every poll, so no clean stretch would ever complete.
        int changesBaseline = Volatile.Read(ref configurationChanges);
        int queriesBaseline = sentinel.PrimaryQueries;
        await UntilAsync(
            () =>
            {
                int changesNow = Volatile.Read(ref configurationChanges);
                if (changesNow != changesBaseline)
                {
                    changesBaseline = changesNow;
                    queriesBaseline = sentinel.PrimaryQueries;
                    return false;
                }
                return sentinel.PrimaryQueries >= queriesBaseline + 3;
            },
            "no stretch of sentinel polls without a reconfigure");
        log.WriteLine($"clean stretch: {sentinel.PrimaryQueries - queriesBaseline} sentinel polls, configuration changes still {changesBaseline}");

        // and once sentinel does catch up, the connection follows it
        sentinel.Primary = NewNodeC;
        await AssertPrimaryBecomesAsync(conn, NewNodeC);
    }

    private static ConfigurationOptions CreateConfig(InProcessTestServer data, InProcessSentinelServer sentinel)
    {
        var config = data.GetClientConfig();
        config.EndPoints.Clear();
        config.EndPoints.Add(SentinelEndPoint);
        config.ServiceName = ServiceName;
        config.Tunnel = InProcessSentinelServer.CreateTunnel(data, sentinel);
        config.ConfigCheckSeconds = 1; // how soon the demoted node's role is re-read
        return config;
    }

    // C is promoted, and A and B follow it; nothing is announced and no connection drops
    private static void FailOverToNewNode(InProcessTestServer data)
    {
        data.AddEmptyNode(NewNodeC);
        data.SetReplicaOf(PrimaryA, NewNodeC);
        data.SetReplicaOf(ReplicaB, NewNodeC);
    }

    private async Task AssertPrimaryBecomesAsync(IConnectionMultiplexer conn, EndPoint expected)
    {
        var timer = Stopwatch.StartNew();
        await UntilAsync(() => ConnectedPrimaries(conn).SequenceEqual([expected]), $"primary did not become {Format.ToString(expected)}");
        log.WriteLine($"primary is {Format.ToString(expected)} after {timer.ElapsedMilliseconds}ms");

        Assert.Equal([expected], ConnectedPrimaries(conn));
        Assert.Equal(expected, conn.GetDatabase().IdentifyEndpoint("demotion-key", CommandFlags.DemandMaster));
    }

    private static async Task UntilAsync(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) Assert.Fail($"{failure} within {timer.Elapsed.TotalSeconds:0}s");
            await Task.Delay(100);
        }
    }

    private static EndPoint[] ConnectedPrimaries(IConnectionMultiplexer conn)
        => conn.GetServers().Where(static s => s.IsConnected && !s.IsReplica).Select(static s => s.EndPoint).ToArray();
}
