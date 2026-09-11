using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Configuration;
using StackExchange.Redis.Server;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Retiring a node that refuses every connection while a sharded subscription was pinned to it, with a
/// maintenance window open - the shape the field ticket suggested could starve pruning indefinitely.
/// </summary>
/// <remarks>
/// The worry, from the ticket: the customer's exception was on the <b>Subscription</b> bridge with
/// <c>last: SSUBSCRIBE</c>. Retirement requires the endpoint to be idle, idleness counts <i>caller</i> work,
/// and a sharded subscribe backlogged on a bridge that can never write it would be caller work - so the
/// topology could say the node is gone, pruning could want to retire it, and <c>IsIdle()</c> could veto it,
/// for longer while a maintenance window is open (relaxation raises the timeout that
/// <c>CheckBacklogForTimeouts</c> purges against).
/// <para>
/// <b>Measured, and the answer is reassuring</b>: it does not happen, for a reason worth pinning down. Server
/// selection will not pick a disconnected node, so within a heartbeat the caller's subscribe is re-aimed at a
/// reachable sibling. What piles up on the refusing node is only <i>our own</i> traffic - autoconfigure
/// probes, keep-alives - which is exactly what <c>HasCallerWork()</c> was narrowed to exclude, so idleness is
/// true and the retirement proceeds. Both halves are asserted below, because the second is what makes the
/// first safe.
/// </para>
/// <para>
/// This also corrects the reading of the ticket: <c>last: SSUBSCRIBE</c> names the last command <i>written</i>
/// on that bridge, not a queued caller subscribe. The veto it implied needs a stronger precondition - no
/// reachable candidate for the slot at all - and at that point one endpoint's retirement is not the
/// interesting question.
/// </para>
/// </remarks>
[Collection(NonParallelCollection.Name)]
public class RetirementUnderMaintenanceTests(ITestOutputHelper log)
{
    [Fact]
    public async Task ARefusingNodeAccumulatesOnlyOurOwnTrafficAndIsRetired()
    {
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        var doomed = BlackHoleTunnel.GetRefusingEndPoint();
        server.AddEmptyNode(doomed);

        // the channel has to live on the doomed node, so pin it there by slot rather than by hope
        var channel = RedisChannel.Sharded("retire-me");
        var asKey = (RedisKey)(byte[])channel!;
        Assert.True(server.Migrate(asKey, doomed), "the fake should have moved the channel's slot");

        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.MaintenanceNotifications = MaintenanceNotificationMode.Enabled;
        config.AbortOnConnectFail = false;
        config.ReconnectRetryPolicy = new LinearRetry(500);
        var tunnel = new BlackHoleTunnel(server.Tunnel);
        config.Tunnel = tunnel;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var mux = (ConnectionMultiplexer)conn;

        Assert.True(
            await Poll.UntilAsync(() => conn.GetEndPoints().Contains(doomed), timeoutMilliseconds: 10_000),
            $"{doomed} was never discovered, so this test would prove nothing");

        var subscriber = conn.GetSubscriber();
        await subscriber.SubscribeAsync(channel, (_, _) => { });
        Assert.True(
            await Poll.UntilAsync(() => Equals(subscriber.SubscribedEndpoint(channel), doomed), timeoutMilliseconds: 5000),
            $"the subscription should be pinned to {doomed}, but went to {subscriber.SubscribedEndpoint(channel)}");

        var endpoint = mux.GetServerEndPoint(doomed, ServerProvenance.ClusterTopology);

        // A long window, opened while the node still answers - which is the only way it can be opened, and is
        // the ordering the ticket had: the disruption is announced, and *then* the node goes away.
        server.SendShardNotification(null, MaintenanceNotificationKind.FailingOver, timeSeconds: 60, shardIds: "[\"1\"]");
        Assert.True(
            await Poll.UntilAsync(() => endpoint.IsMaintenanceRelaxed, timeoutMilliseconds: 5000),
            "the notification should have opened a relaxed window on the doomed server");

        // ...and now it goes away for good, refusing every connection. No notification of any of that, which
        // is the point.
        tunnel.BlackHole(doomed);
        conn.GetServer(doomed).SimulateConnectionFailure(SimulatedFailureType.All);

        // Then *let the pressure build*, before the topology is allowed to catch up. This ordering is the
        // whole test: while our slot map still says the channel lives on the doomed node, the resubscribe
        // machinery keeps aiming SSUBSCRIBE at a bridge that can never write it, and those pile into the
        // backlog as caller work. Move the slot first - as the first version of this test did - and the
        // resubscribe goes to the survivor instead, nothing accumulates, and the test passes having exercised
        // nothing. The ticket's client was in exactly this state: dead endpoint, stale map, pending SSUBSCRIBE.
        // Let it sit *before* the topology is allowed to catch up. This ordering is the whole test: while our
        // slot map still says the channel lives on the doomed node, anything aimed at that slot is aimed at a
        // bridge that can never write it. Move the slot first and the question never arises.
        var survivor = mux.GetServerEndPoint(server.DefaultEndPoint, ServerProvenance.ClusterTopology);
        await Task.Delay(3000);
        log.WriteLine(
            $"under pressure - doomed: callerWork={endpoint.HasCallerWork()} outstanding={endpoint.GetOutstandingCount()} "
            + $"relaxed={endpoint.IsMaintenanceRelaxed} idle={endpoint.IsIdle()}; "
            + $"subscribed to {subscriber.SubscribedEndpoint(channel)?.ToString() ?? "(nowhere yet)"}");

        // The refusing node *is* accumulating work - if it were not, the rest of this proves nothing, because
        // the distinction being tested would be vacuous...
        Assert.True(
            endpoint.GetOutstandingCount() > 0,
            "the refusing node should be accumulating our own probe traffic; with nothing outstanding this test "
            + "is not exercising the distinction that lets retirement proceed");

        // ...and none of it is a caller's, which is what keeps idleness true. Selection will not pick a
        // disconnected node, so the caller's subscribe was re-aimed at the reachable sibling within a
        // heartbeat rather than queueing here for the relaxed timeout to eventually purge.
        Assert.False(
            endpoint.HasCallerWork(),
            "a caller's work should not be queued on a node that cannot be written to while a reachable "
            + "candidate for the slot exists");
        Assert.NotEqual(doomed, subscriber.SubscribedEndpoint(channel));
        GC.KeepAlive(survivor);

        // only now does the cluster admit it has gone
        Assert.True(server.Migrate(asKey, server.DefaultEndPoint));
        Assert.True(server.RemoveNode(doomed), "the node should have been removed from the fake");

        // Generations are driven rather than waited for, as in the quiet case: what is under test is the
        // retirement, not the refresh trigger. Bounded so a regression fails rather than hangs.
        for (int i = 0; i < 40 && conn.GetEndPoints().Contains(doomed); i++)
        {
            await mux.ReconfigureAsync(first: false, reconfigureAll: true, log: null, blame: null, cause: $"test-generation-{i}");
            if (i % 8 == 0)
            {
                log.WriteLine(
                    $"generation {i}: idle={endpoint.IsIdle()} callerWork={endpoint.HasCallerWork()} "
                    + $"outstanding={endpoint.GetOutstandingCount()} relaxed={endpoint.IsMaintenanceRelaxed} "
                    + $"subs={endpoint.GetCounters().Subscription.Subscriptions}");
            }

            await Task.Delay(100);
        }

        log.WriteLine($"endpoints: {string.Join(", ", conn.GetEndPoints().Select(x => x.ToString()))}");
        log.WriteLine(
            $"final: idle={endpoint.IsIdle()} callerWork={endpoint.HasCallerWork()} "
            + $"outstanding={endpoint.GetOutstandingCount()} relaxed={endpoint.IsMaintenanceRelaxed}");

        Assert.DoesNotContain(doomed, conn.GetEndPoints());

        // and the caller's subscription is not collateral damage: it belongs to the surviving node now
        Assert.True(
            await Poll.UntilAsync(() => Equals(subscriber.SubscribedEndpoint(channel), server.DefaultEndPoint), timeoutMilliseconds: 10_000),
            $"the subscription should have moved to the surviving node, but is on {subscriber.SubscribedEndpoint(channel)}");
    }
}
