using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Cluster discovery registers a node it has no reason to dial <em>inert</em>: known and addressable via
/// <see cref="IConnectionMultiplexer.GetServer(EndPoint, object)"/>, but with no connection opened, since
/// first use creates the bridge anyway. A node discovery goes on to <em>wait</em> for must not be left in
/// that state - nobody is dialling it, so the wait cannot end. See #3232.
/// </summary>
/// <remarks>
/// The node at risk is a replica of a shard we have not connected to yet. <c>UpdateClusterRange</c> only
/// materialises primaries, and a replica is otherwise created when its own primary autoconfigures and
/// resolves its genealogy - which for such a shard has not happened. Discovery meets it first, in the
/// <c>CLUSTER NODES</c> sweep, and the test that sweep applied was "do we have a server for it yet?" - which
/// such a node fails, so it was classed as serving no slots. The <c>CLUSTER SLOTS</c> view does list it, a
/// slot range naming its primary and then its replicas, so it was also in the set the connect waited on.
/// </remarks>
public class InertClusterNodeUnitTests(ITestOutputHelper log)
{
    private const string Key = "inert-node-key";

    /// <summary>A cluster in which one shard never answers <c>CLUSTER NODES</c>, so nothing resolves its genealogy.</summary>
    private sealed class QuietShardServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        /// <summary>The port of the node that stays quiet, or -1 for none.</summary>
        public int QuietPort { get; set; } = -1;

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            if (client.Node is { } node && node.Port == QuietPort
                && request.Count >= 2 && request.IsString(0, "CLUSTER"u8) && request.IsString(1, "NODES"u8))
            {
                // this is what makes the test deterministic rather than a race: without it, this shard
                // autoconfigures and creates its own replica, usually before discovery gets there
                return TypedRedisValue.Error("ERR deliberately quiet");
            }
            return base.Execute(client, in request);
        }
    }

    [Fact]
    public async Task ASlotMappedReplicaIsDialledRatherThanClassedAsServingNothing()
    {
        using var server = new QuietShardServer(log) { ServerType = ServerType.Cluster };
        GetHost(server.DefaultEndPoint, out var port);

        var otherPrimary = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        var otherReplica = server.AddReplicaNode(new IPEndPoint(IPAddress.Loopback, port + 2), otherPrimary);
        server.Migrate((RedisKey)Key, otherPrimary);
        server.QuietPort = port + 1;

        var text = new StringWriter();
        var watch = Stopwatch.StartNew();
        await using var conn = await server.ConnectAsync(defaultOnly: true, log: text);
        watch.Stop();

        var connectLog = text.ToString();
        log.WriteLine(connectLog);

        // the precondition: discovery, not the shard's own genealogy pass, is what introduced the replica.
        // If that stops being true this fails rather than passing for the wrong reason
        Assert.Contains($"Registering {otherReplica} from the slot map", connectLog);

        // ...and it was not written off as serving nothing, which is what left it undialled
        Assert.DoesNotContain($"Registering {otherReplica} without connecting", connectLog);

        // before the fix this waited out the whole ConnectTimeout on a connection nobody was opening,
        // and then reported the node as unresponsive
        var timeout = conn.RawConfig.ConnectTimeout;
        log.WriteLine($"connected in {watch.ElapsedMilliseconds}ms, connect timeout is {timeout}ms");
        Assert.True(watch.ElapsedMilliseconds < timeout / 2, $"took {watch.ElapsedMilliseconds}ms");
        Assert.True(conn.GetServer(otherReplica).IsConnected);
    }

    [Fact]
    public async Task ANodeThatServesNothingIsStillLeftUndialled()
    {
        // the complement: only what the connect is going to wait for may be dialled. A node that serves no
        // slots and replicates nothing is in CLUSTER NODES but not in CLUSTER SLOTS, so it is not part of
        // that set, and registering it inert is the whole point - it stays that way
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        GetHost(server.DefaultEndPoint, out var port);
        var idle = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));

        var text = new StringWriter();
        await using var conn = await server.ConnectAsync(defaultOnly: true, log: text);

        var connectLog = text.ToString();
        log.WriteLine(connectLog);

        Assert.Contains($"Registering {idle} without connecting", connectLog);

        // asserted as "no bridge was ever created", not "not connected yet": the latter is also true of a
        // node that is being dialled right now, so it would not notice the regression it exists to catch
        var mux = (ConnectionMultiplexer)conn;
        var server2 = mux.GetServerEndPoint(idle, ServerProvenance.ClusterTopology, activate: false);
        Assert.Null(server2.GetBridge(ConnectionType.Interactive, create: false));
    }

    [Fact]
    public async Task WaitingOnAnUndialledServerCannotComplete()
    {
        // why the connect loop activates whatever it is about to await, rather than trusting discovery to
        // have done it: an inert server can still reach that loop legitimately - a node registered inert on
        // one connect attempt may be listed in CLUSTER SLOTS on the retry - and waiting on one is a stall,
        // not a slow success. There is nothing to complete the wait
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        await using var conn = await server.ConnectAsync(defaultOnly: true);
        GetHost(server.DefaultEndPoint, out var port);
        var idle = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));

        var mux = (ConnectionMultiplexer)conn;
        var inert = mux.GetServerEndPoint(idle, ServerProvenance.ClusterTopology, activate: false);
        Assert.Null(inert.GetBridge(ConnectionType.Interactive, create: false));

        var connected = inert.OnConnectedAsync();
        Assert.NotSame(connected, await Task.WhenAny(connected, Task.Delay(250)));
        Assert.False(connected.IsCompleted);

        // ...and activating it is what lets the wait end; Activate is idempotent, so doing this to a server
        // that was already dialled costs nothing
        // ...and dialling it is what lets the wait end. Both legs, as ActivateServer does: under RESP2 the
        // monitors are only completed once the subscription connection is up too ("the second leg"), so
        // activating the interactive bridge alone would leave this hanging just the same
        inert.Activate(ConnectionType.Interactive, null);
        if (inert.SupportsSubscriptions && !inert.KnowOrAssumeResp3())
        {
            inert.Activate(ConnectionType.Subscription, null);
        }

        var settled = await Task.WhenAny(connected, Task.Delay(TimeSpan.FromSeconds(10)));
        log.WriteLine($"IsConnected={inert.IsConnected}, state={inert.GetBridge(ConnectionType.Interactive, create: false)?.ConnectionState}");
        Assert.Same(connected, settled);
        log.WriteLine($"completed: {await connected}");
    }
}
