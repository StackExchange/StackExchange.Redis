using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Proxied deployments can refuse some or all <c>CLUSTER</c> subcommands while still identifying as a cluster
/// (Redis Enterprise exposes <c>CLUSTER SLOTS</c> only under the OSS cluster API, and its <c>HELLO</c>/<c>INFO</c>
/// can say "cluster" while <c>CLUSTER</c> stays blocked entirely). A refusal is a posture, not a fault: the
/// topology pass must fall back to whatever view it can get, quietly - not log a server error on every
/// reconfigure. See #3274.
/// </summary>
[RunPerProtocol]
public class ClusterCommandsRefusedUnitTests(ITestOutputHelper log)
{
    private sealed class RefusingServer(ITestOutputHelper log, bool refuseNodes) : InProcessTestServer(log)
    {
        protected override TypedRedisValue ClusterSlots(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Error("ERR command is not allowed");

        protected override TypedRedisValue ClusterNodes(RedisClient client, in RedisRequest request)
            => refuseNodes ? TypedRedisValue.Error("ERR command is not allowed") : base.ClusterNodes(client, request);
    }

    [Fact]
    public async Task SlotsRefusedFallsBackToNodes()
    {
        using var server = new RefusingServer(log, refuseNodes: false) { ServerType = ServerType.Cluster };
        var a = server.DefaultEndPoint;
        var b = server.AddEmptyNode();

        var config = server.GetClientConfig(defaultOnly: true); // only connect to a initially
        var logger = new CapturingLogger();
        config.LoggerFactory = logger;
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        // discovery still happened, from the NODES view alone
        var endpoints = conn.GetEndPoints();
        Assert.Contains(a, endpoints);
        Assert.Contains(b, endpoints);

        log.WriteLine(logger.All);
        Assert.Empty(logger.Matching("Encountered error while updating cluster config"));
        Assert.NotEmpty(logger.Matching("CLUSTER SLOTS was refused"));

        await conn.GetDatabase().PingAsync();
    }

    [Fact]
    public async Task AllClusterCommandsRefusedIsQuiet()
    {
        // the server still *identifies* as a cluster (HELLO "mode", INFO "redis_mode"), so the client
        // interrogates it; both commands being refused must leave a working connection, not a logged error
        using var server = new RefusingServer(log, refuseNodes: true) { ServerType = ServerType.Cluster };

        var config = server.GetClientConfig(defaultOnly: true);
        var logger = new CapturingLogger();
        config.LoggerFactory = logger;
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        log.WriteLine(logger.All);
        Assert.Empty(logger.Matching("Encountered error while updating cluster config"));
        Assert.NotEmpty(logger.Matching("CLUSTER NODES was refused"));

        await conn.GetDatabase().PingAsync();

        // ...and a later explicit reconfigure stays quiet too
        Assert.True(await conn.ReconfigureAsync("test"));
        Assert.Empty(logger.Matching("Encountered error while updating cluster config"));
    }
}
