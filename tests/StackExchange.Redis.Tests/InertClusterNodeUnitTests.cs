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
/// first use creates the bridge anyway. A node that discovery then goes on to <em>wait</em> for must not be
/// left in that state - nobody is dialling it, so the wait cannot end. See #3232.
/// </summary>
/// <remarks>
/// The node at risk is a replica of a shard we have not connected to yet. <c>UpdateClusterRange</c> only
/// materialises primaries, and a replica is otherwise created when its own primary autoconfigures and
/// resolves its genealogy - which for such a shard has not happened. Discovery meets it first, in the
/// <c>CLUSTER NODES</c> sweep, and registers it inert; the <c>CLUSTER SLOTS</c> view lists it (a slot range
/// names its primary and then its replicas), so it is also in the set the connect then waits on.
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
    public async Task ADiscoveredReplicaIsDialledRatherThanStallingTheConnect()
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

        // the test only means anything if the replica really did arrive inert; if discovery stops taking
        // that route, this fails loudly rather than passing for the wrong reason
        Assert.Contains($"Registering {otherReplica} without connecting", connectLog);

        // before the fix this waited out the whole ConnectTimeout on a connection nobody was opening
        var timeout = conn.RawConfig.ConnectTimeout;
        log.WriteLine($"connected in {watch.ElapsedMilliseconds}ms, connect timeout is {timeout}ms");
        Assert.True(watch.ElapsedMilliseconds < timeout / 2, $"took {watch.ElapsedMilliseconds}ms");

        // ...and reported the node as unresponsive, having never dialled it
        Assert.True(conn.GetServer(otherReplica).IsConnected);
    }

    [Fact]
    public async Task ANodeThatServesNothingIsStillLeftUndialled()
    {
        // the complement: the fix must only dial what the connect is going to wait for. A node that serves
        // no slots and replicates nothing is in CLUSTER NODES but not in CLUSTER SLOTS, so it is not part of
        // that set, and registering it inert is the whole point - it stays that way
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        GetHost(server.DefaultEndPoint, out var port);
        var idle = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));

        var text = new StringWriter();
        await using var conn = await server.ConnectAsync(defaultOnly: true, log: text);

        var connectLog = text.ToString();
        log.WriteLine(connectLog);

        Assert.Contains($"Registering {idle} without connecting", connectLog);
        Assert.False(conn.GetServer(idle).IsConnected);
    }
}
