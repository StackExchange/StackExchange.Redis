using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Re-reading the topology on a timer, as a backstop for a change nothing reported.
/// </summary>
/// <remarks>
/// The last of the three findings from the 37-hour field failure. The other two are closed: an endpoint that
/// only ever refuses now provokes a refresh after three consecutive connect failures, and a node that has left
/// the topology is retired rather than dialled forever. What neither covers is an endpoint that is
/// *reachable*, completes a handshake, and is no longer part of the deployment - it produces no failure, no
/// redirect and no announcement, so nothing asks the question.
/// <para>
/// Non-parallel: these drive the multiplexer's own heartbeat and assert on what the server received, which a
/// neighbouring test's traffic would confuse.
/// </para>
/// </remarks>
[Collection(NonParallelCollection.Name)]
public class PeriodicTopologyRefreshTests(ITestOutputHelper log)
{
    /// <summary>Counts inbound <c>CLUSTER</c> commands, so a refresh is visible from the server's side.</summary>
    private sealed class CountingServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        private int _clusterCommands;

        public int ClusterCommands => Volatile.Read(ref _clusterCommands);

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            if (request.Count > 0 && string.Equals(request.GetString(0), "cluster", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _clusterCommands);
            }

            return base.Execute(client, in request);
        }
    }

    private static async Task<(CountingServer Server, ConnectionMultiplexer Connection)> ConnectAsync(
        ITestOutputHelper log,
        int topologyRefreshSeconds)
    {
        var server = new CountingServer(log) { ServerType = ServerType.Cluster };
        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.TopologyRefreshSeconds = topologyRefreshSeconds;

        var conn = await ConnectionMultiplexer.ConnectAsync(config);
        return (server, conn);
    }

    [Fact]
    public async Task TheTopologyIsReReadOnTheInterval()
    {
        // One second plus up to 30 of jitter, so this waits out the jitter rather than the interval: the
        // interval is the part under test, and the jitter is what stops a fleet moving in step.
        var (server, conn) = await ConnectAsync(log, topologyRefreshSeconds: 1);
        using (server)
        await using (conn)
        {
            var before = server.ClusterCommands;
            log.WriteLine($"cluster commands after connect: {before}");

            Assert.True(
                await Poll.UntilAsync(() => server.ClusterCommands > before, timeoutMilliseconds: 40_000, pollMilliseconds: 250),
                "the topology should have been re-read without anything going wrong");

            log.WriteLine($"cluster commands after the interval: {server.ClusterCommands}");
        }
    }

    [Fact]
    public async Task ZeroTurnsItOff()
    {
        // The escape hatch has to work, because this is the one refresh path whose cost is paid on a schedule
        // rather than in response to something. Anybody who does not want it must be able to say so.
        var (server, conn) = await ConnectAsync(log, topologyRefreshSeconds: 0);
        using (server)
        await using (conn)
        {
            var before = server.ClusterCommands;
            await Task.Delay(3000);

            log.WriteLine($"cluster commands: {before} -> {server.ClusterCommands}");
            Assert.Equal(before, server.ClusterCommands);
        }
    }

    [Fact]
    public async Task NothingIsReadBeforeTheFirstIntervalElapses()
    {
        // The interval runs from the first heartbeat, not from construction, so a multiplexer that is created,
        // used and disposed inside it costs nothing. Asserted because the natural implementation - schedule at
        // construction - makes short-lived multiplexers pay for a feature they never benefit from.
        var (server, conn) = await ConnectAsync(log, topologyRefreshSeconds: 3600);
        using (server)
        await using (conn)
        {
            var before = server.ClusterCommands;
            await Task.Delay(3000);

            log.WriteLine($"cluster commands: {before} -> {server.ClusterCommands}");
            Assert.Equal(before, server.ClusterCommands);
        }
    }

    [Fact]
    public void TheDefaultIsLongAndSurvivesTheConfigurationString()
    {
        // 30 minutes: long enough that the fleet cost is negligible, short enough to bound how long a stale
        // view can persist when nothing else notices.
        Assert.Equal(1800, new ConfigurationOptions().TopologyRefreshSeconds);

        var parsed = ConfigurationOptions.Parse("localhost,topologyRefreshSeconds=120");
        Assert.Equal(120, parsed.TopologyRefreshSeconds);
        Assert.Contains("topologyRefreshSeconds=120", parsed.ToString());

        // ...and an explicit zero has to round-trip, or turning it off in a configuration string would look
        // like it worked while silently reverting to the default
        var disabled = ConfigurationOptions.Parse("localhost,topologyRefreshSeconds=0");
        Assert.Equal(0, disabled.TopologyRefreshSeconds);
        Assert.Contains("topologyRefreshSeconds=0", disabled.ToString());

        // Clone has to carry it too. ConfigTests.ExpectedFields is the "have you considered?" guard for this
        // and it did its job - it caught the field being added without Clone knowing about it - but it checks
        // that somebody looked, not that they got it right, so assert the behaviour as well.
        Assert.Equal(0, disabled.Clone().TopologyRefreshSeconds);
        Assert.Equal(120, parsed.Clone().TopologyRefreshSeconds);
    }
}
