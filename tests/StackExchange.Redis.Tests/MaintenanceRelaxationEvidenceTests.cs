using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Does relaxation actually save a command, and does a timeout inside a window say why?
/// </summary>
/// <remarks>
/// The gap these close: everything about relaxed timeouts was implemented and unit-tested, but nothing had ever
/// observed one *rescue* a command - four scenario runs against a real deployment produced zero command
/// failures, which is the right product outcome and useless as evidence.
/// <para>
/// The obvious way to force failures - the fault injector's <c>network_latency</c> - turned out to be the wrong
/// tool. It applies netem to a whole node's interface, so it delays the cluster's own traffic and its DNS along
/// with the client's; 200ms across two of three nodes took a working deployment offline, and its
/// <c>duration_seconds</c> did not revert. A per-reply delay in the fake is precise, instant, and cannot break
/// anything - and it makes the difference measurable rather than anecdotal.
/// </para>
/// </remarks>
public class MaintenanceRelaxationEvidenceTests(ITestOutputHelper log)
{
    // The delays have to clear the heartbeat, not just the timeout: async timeouts are raised by the bridge
    // heartbeat sweep on roughly a one-second cadence, not at the instant the deadline passes. A 600ms delay
    // against a 200ms timeout therefore completes *successfully*, which is how the first version of this test
    // found its control case passing when it should have failed.
    private const int NormalTimeoutMs = 200, RelaxedTimeoutSeconds = 8, SlowReplySeconds = 3;

    private static async Task<ConnectionMultiplexer> ConnectAsync(InProcessTestServer server)
    {
        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.MaintenanceNotifications = MaintenanceNotificationMode.Enabled;
        config.SyncTimeout = NormalTimeoutMs;
        config.AsyncTimeout = NormalTimeoutMs;
        config.MaintenanceRelaxedTimeout = TimeSpan.FromSeconds(RelaxedTimeoutSeconds);

        return await ConnectionMultiplexer.ConnectAsync(config);
    }

    private static List<Server.RedisClient> OptedIn(InProcessTestServer server)
    {
        var found = new List<Server.RedisClient>();
        server.ForAllClients(c =>
        {
            if (c.MaintenanceNotifications) found.Add(c);
        });
        return found;
    }

    [Fact]
    public async Task ARelaxedWindowRescuesACommandThatWouldOtherwiseTimeOut()
    {
        // Two clients, one server, one delay - and the notification sent to only one of them. That is the whole
        // experiment: identical conditions, simultaneously, differing only in whether a disruption was
        // announced. Relaxation is per-server-per-multiplexer, so the client that was never told keeps its
        // ordinary timeout.
        //
        // Deliberately not "fail, then succeed" on a single connection: a timed-out command disrupts the
        // connection, and with a slow reply still configured the reconnect handshake cannot complete, so the
        // second half then fails in the backlog for an entirely unrelated reason. That is what the first
        // version of this test did.
        using var server = new InProcessTestServer(log);

        await using var told = await ConnectAsync(server);
        var toldClient = Assert.Single(OptedIn(server));

        await using var untold = await ConnectAsync(server);
        Assert.Equal(2, OptedIn(server).Count);

        await told.GetDatabase().PingAsync();
        await untold.GetDatabase().PingAsync(); // both healthy before anything is slowed

        // well past the normal timeout *and* the heartbeat, comfortably inside the relaxed one
        server.ResponseDelay = TimeSpan.FromSeconds(SlowReplySeconds);

        // announced to one connection only
        server.SendShardNotification(toldClient, MaintenanceNotificationKind.Migrating, timeSeconds: 20, shardIds: "[\"1\"]", sequenceId: 0);
        var endpoint = ((IInternalConnectionMultiplexer)told).GetServerEndPoint(server.DefaultEndPoint);
        Assert.True(
            await Poll.UntilAsync(() => endpoint.IsMaintenanceRelaxed, timeoutMilliseconds: 10_000),
            "the notification should have relaxed timeouts on the connection that received it");

        var rescued = told.GetDatabase().PingAsync();
        var doomed = untold.GetDatabase().PingAsync();

        var rtt = await rescued;
        log.WriteLine($"told about the disruption: succeeded after {rtt.TotalMilliseconds:0}ms");
        Assert.True(rtt.TotalMilliseconds > NormalTimeoutMs, "the command should have taken longer than the normal timeout allows");

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => doomed);
        log.WriteLine($"not told: {failure.GetType().Name}");
        Assert.True(failure is RedisTimeoutException or TimeoutException, $"expected a timeout, got {failure}");
    }

    [Fact]
    public async Task ATimeoutInsideAWindowSaysWhichEventCausedIt()
    {
        using var server = new InProcessTestServer(log);
        await using var conn = await ConnectAsync(server);
        {
            var db = conn.GetDatabase();
            await db.PingAsync();

            // A long window on purpose. The attribution is read when the timeout is *raised*, so a window that
            // has already expired attributes nothing - even though its disruption is what caused the delay.
            server.SendShardNotification(null, MaintenanceNotificationKind.FailingOver, timeSeconds: 20, shardIds: "[\"7\"]", sequenceId: 0);
            var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
            Assert.True(await Poll.UntilAsync(() => endpoint.IsMaintenanceRelaxed, timeoutMilliseconds: 5_000));

            // beyond even the relaxed timeout, so the command fails *during* an announced disruption
            server.ResponseDelay = TimeSpan.FromSeconds(RelaxedTimeoutSeconds * 3);

            var failure = await Assert.ThrowsAnyAsync<Exception>(() => db.PingAsync());
            log.WriteLine($"{failure.GetType().Name}: {failure.Message}");

            // This is what the attribution is for: "the deployment was failing over" rather than "your query
            // is slow". Nothing had ever observed it fire, because no real run ever failed a command.
            var maintenanceType = failure switch
            {
                RedisTimeoutException timeout => timeout.MaintenanceType,
                RedisConnectionException connection => connection.MaintenanceType,
                _ => MaintenanceNotificationType.None,
            };

            log.WriteLine($"attributed to: {maintenanceType}");
            Assert.Equal(MaintenanceNotificationType.FailingOver, maintenanceType);
        }
    }
}
