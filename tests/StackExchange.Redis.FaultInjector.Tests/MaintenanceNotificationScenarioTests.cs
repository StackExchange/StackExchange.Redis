using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The proxied-standalone shape: one database, shared by everything in this class.
/// </summary>
public sealed class ProxiedStandaloneFixture() : FaultInjectorFixture(DatabaseShape.ProxiedStandalone);

/// <summary>
/// Maintenance notifications against a real deployment: does the opt-in take, and do the frames arrive.
/// </summary>
/// <remarks>
/// This automates what was done by hand on 2026-08-27/28 - and the hand-driven version is why it is worth
/// automating: every finding that shaped the design contradicted something believed from the specification.
/// The specification is prose, the payloads were never published, so "believed" and "observed" are genuinely
/// different states and only a real deployment moves one into the other.
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "maint-notifications")]
public class MaintenanceNotificationScenarioTests(ProxiedStandaloneFixture fixture, ITestOutputHelper log)
    : IClassFixture<ProxiedStandaloneFixture>
{
    [Fact]
    public async Task OptInIsAcceptedByARealServer()
    {
        fixture.RequireAvailable();
        var database = fixture.Database!;
        log.WriteLine($"connecting to {database}");

        var config = database.GetClientConfig();
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        // Enabled means the connection is refused if the server will not give us notifications, so simply
        // getting here proves the opt-in was accepted - there is nothing weaker to assert. Note this is the
        // check that a stub "+OK" would pass, which is why the delivery test below exists as well.
        Assert.True(conn.IsConnected);
        var server = conn.GetServer(conn.GetEndPoints()[0]);
        log.WriteLine($"connected: {server.Version}, protocol {server.Protocol}");
        Assert.Equal(RedisProtocol.Resp3, server.Protocol);

        // and the connection genuinely works, rather than merely being established
        Assert.True(await conn.GetDatabase().PingAsync() > TimeSpan.Zero);
    }

    [Fact]
    public async Task DisruptionDeliversNotificationsAndRelaxesTimeouts()
    {
        fixture.RequireAvailable();
        var database = fixture.Database!;

        // the ambient token, so a run that is cancelled or times out stops promptly instead of sitting out a
        // 90-second wait or a ten-minute injector poll
        var cancellationToken = TestContext.Current.CancellationToken;

        var received = new List<PushMaintenanceEvent>();
        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig());
        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is PushMaintenanceEvent push)
            {
                lock (received) received.Add(push);
            }
        };

        // Ask the injector what it will actually accept rather than hardcoding: the effect/trigger matrix is
        // sparse - maintenance_mode only supports remove-add and remove, failover needs replication - and a
        // guessed combination fails as an opaque 400.
        var triggers = await fixture.Injector.GetValidTriggersAsync(
            "topology-change-standalone", "conn_drop", cancellationToken: cancellationToken);
        log.WriteLine($"valid triggers: {triggers}");

        var query = new Dictionary<string, string?>
        {
            ["effect"] = "conn_drop",
            ["trigger"] = "endpoint_rebind",
            ["bdb_id"] = database.BdbId.ToString(),
        };

        var setup = await fixture.Injector.PostScenarioAsync(
            "topology-change-standalone", "setup", query, cancellationToken: cancellationToken);
        log.WriteLine($"setup: {setup}");
        try
        {
            await fixture.Injector.PostScenarioAsync(
                "topology-change-standalone", leg: null, query, cancellationToken: cancellationToken);

            // Time bounds come from the worst observation, not the typical one: the same 15s grace has been
            // seen with DNS updating at +4.4s and at +18.7s, the latter *after* the socket closed. A bound set
            // from one cluster's numbers would fail on the other.
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                lock (received)
                {
                    if (received.Count > 0) break;
                }

                await Task.Delay(500, cancellationToken);
            }

            lock (received)
            {
                // dump everything before asserting: twice during the manual runs a conclusion was drawn from a
                // filtered view of the output and was wrong both times
                foreach (var evt in received) log.WriteLine($"  {evt.ReceivedTimeUtc:HH:mm:ss.fff} {evt.RawMessage}");
                Assert.NotEmpty(received);
            }
        }
        finally
        {
            // teardown even when the assertions failed: a scenario left set up holds a cluster flag and
            // poisons the next run. Note setup_id lives in the injector's memory, so bdb_id is the fallback
            // that survives it restarting.
            // Deliberately *not* the ambient token, which is why the analyzer is suppressed here: teardown has
            // to run when the test itself was cancelled, or a cancelled run leaves a cluster flag set and
            // poisons the next one. It gets its own budget rather than none, so a wedged injector cannot hang
            // the run either.
            using var teardown = new CancellationTokenSource(TimeSpan.FromMinutes(5));
#pragma warning disable xUnit1051 // ambient cancellation is exactly what must not apply to cleanup
            await fixture.Injector.PostScenarioAsync("topology-change-standalone", "teardown", query, cancellationToken: teardown.Token);
#pragma warning restore xUnit1051
        }
    }
}
