using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// D6 against a real deployment: do we get off the connection before the server takes it away?
/// </summary>
/// <remarks>
/// The one thing the in-process harness structurally cannot test. The handoff's hostname branch needs two
/// things a fake cannot supply - an endpoint that is a *name*, and a real socket whose remote address tells us
/// where we currently are - and its decision turns on DNS changing underneath us.
/// <para>
/// The discriminator is the failure type of the disconnect. <c>ConnectionDisposed</c> means *we* replaced the
/// connection; <c>SocketClosed</c> means the server did. Both outcomes are legitimate, because DNS is not
/// guaranteed to win the race - measured on one cluster at +18.7s against a socket closing at +15.7s - so this
/// records which happened rather than demanding the good one.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "moving-handoff")]
public class MovingHandoffScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ExistingDatabaseFixture>
{
    [Theory]
    [InlineData("conn_drop", "endpoint_rebind", MaintenanceEndpointType.ServerDefault)]
    [InlineData("data_movement_conn_drop", "maintenance_mode", MaintenanceEndpointType.ServerDefault)]
    [InlineData("conn_drop", "endpoint_rebind", MaintenanceEndpointType.ExternalFqdn)]
    public async Task HandoffBeatsTheServerToTheClose(string effect, string trigger, MaintenanceEndpointType endpointType)
    {
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scenario = await ScenarioRun.SetupAsync(
            fixture.Injector, "topology-change-standalone", effect, trigger, log.WriteLine,
            cancellationToken: cancellationToken);

        var database = scenario.Database;
        Assert.NotNull(database);

        var clock = Stopwatch.StartNew();
        var timeline = new List<string>();
        void Note(string what)
        {
            var entry = $"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {what}";
            lock (timeline) timeline.Add(entry);
            log.WriteLine(entry);
        }

        var config = database.GetClientConfig(fixture.Environment);
        config.MaintenanceMovingEndpointType = endpointType;
        log.WriteLine($"moving-endpoint-type: {endpointType}");

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var muxer = (IInternalConnectionMultiplexer)conn;
        var endpoint = muxer.GetServerEndPoint(conn.GetEndPoints()[0]);

        // The precondition the fake could not meet: a name to resolve, and a socket that says where we are.
        Assert.IsType<System.Net.DnsEndPoint>(conn.GetEndPoints()[0]);
        log.WriteLine($"dialled {conn.GetEndPoints()[0]} (a name, so the probe can engage)");

        var failures = new List<ConnectionFailureType>();
        conn.ConnectionFailed += (_, e) =>
        {
            lock (failures) failures.Add(e.FailureType);
            Note($"disconnect: {e.FailureType}");
        };
        conn.ConnectionRestored += (_, _) => Note("reconnected");
        var movingSequences = new HashSet<long>();
        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is not PushMaintenanceEvent push) return;
            Note($"{push.NotificationType} seq={push.SequenceId} time={push.Time?.TotalSeconds.ToString() ?? "-"}");
            if (push.NotificationType == MaintenanceNotificationType.Moving)
            {
                lock (movingSequences) movingSequences.Add(push.SequenceId);
            }
        };

        clock.Restart();
        await scenario.FireAsync(cancellationToken);
        Note("injector reports the scenario finished");

        // long enough to cover the whole announced window plus the observed overshoot
        var deadline = clock.Elapsed + TimeSpan.FromSeconds(75);
        while (clock.Elapsed < deadline)
        {
            try
            {
                await conn.GetDatabase().PingAsync();
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                Note($"ping failed: {ex.GetType().Name}");
            }

            await Task.Delay(1000, cancellationToken);
        }

        Note($"handoff outcome: {endpoint.LastHandoffOutcome ?? "(none recorded)"}; recycles={endpoint.HandoffRecycles}");

        lock (failures)
        {
            var order = string.Join(" -> ", failures);
            log.WriteLine($"  disconnect sequence: {(order.Length == 0 ? "(none)" : order)}");

            // Note what is *not* asserted: that a ConnectionDisposed appears here. Our own recycle does not
            // raise ConnectionFailed - disposal is not reported as a failure - so from the outside a handoff is
            // invisible, which is worth knowing in its own right and is why HandoffRecycles exists.
        }

        // Exactly one handoff per distinct notification - not one overall, which is what this asserted until a
        // live run proved the scenario can announce twice. The `maintenance_mode` trigger reports
        // `automatically_clean_mm: false`, so the node stays in maintenance mode after the effect lands and
        // coming back out of it moves the shard again: MOVING seq=2 at +14.9s and seq=3 at +97.0s, one recycle
        // each, and the client was right both times.
        //
        // What the count still catches is the feedback loop this test found on its first live run: a server
        // re-sends MOVING to a connection that opts in while the window is still open, and since the handoff
        // replaces the connection, acting on the repeat produces another one - twelve recycles from *one*
        // event. A replay repeats the sequence number, so keying on the sequence keeps that sharp while
        // letting a genuine second event through.
        int announced;
        lock (movingSequences) announced = movingSequences.Count;
        Assert.True(announced > 0, "no MOVING was announced, so this test would prove nothing");
        Assert.Equal(announced, endpoint.HandoffRecycles);
        Assert.NotNull(endpoint.LastHandoffOutcome);

        // Which outcome depends on whether the server named a replacement, and it only does that when we asked
        // for an endpoint type: MoveTo when it did, Recycle when we had to find the new address via DNS.
        var expectedOutcome = endpointType == MaintenanceEndpointType.ServerDefault ? "Recycle" : "MoveTo";
        Assert.Contains(expectedOutcome, endpoint.LastHandoffOutcome);

        if (endpointType != MaintenanceEndpointType.ServerDefault)
        {
            // The payoff, and the reason to ask for an endpoint type at all: with somewhere named to go we move
            // immediately instead of waiting on DNS, so the server never has to close the connection out from
            // under us. Measured before this worked: the handoff happened but landed back on the node being
            // retired, and a SocketClosed followed ~15s later.
            lock (failures)
            {
                Assert.DoesNotContain(ConnectionFailureType.SocketClosed, failures);
            }
        }

        Assert.True(
            await Poll.UntilAsync(
                () =>
                {
                    try
                    {
                        return conn.IsConnected && conn.GetDatabase().Ping() >= TimeSpan.Zero;
                    }
                    catch (Exception ex) when (ex is RedisException or TimeoutException)
                    {
                        return false;
                    }
                },
                timeoutMilliseconds: 30_000),
            "the client should be serving commands after the handoff");
    }
}
