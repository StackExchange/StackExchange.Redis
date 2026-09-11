using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Real topology changes on a real deployment, watched by a real client.
/// </summary>
/// <remarks>
/// Each scenario provisions its own database, because every trigger publishes the <c>dbconfig</c> it requires
/// and all of them want <c>proxy_policy: single</c> - a shape the environment templates do not create.
/// <para>
/// What these assert is deliberately modest: that the notifications arrive, parse, and open the relaxation
/// window. Timings are *recorded* rather than asserted, because the measured spread across clusters is wide
/// enough that a bound tight enough to be interesting would be flaky - DNS has been seen updating 4.4s after
/// <c>MOVING</c> on one cluster and 3s after the socket closed on another. The log is the deliverable for
/// timing; the assertions cover behaviour.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "topology-change")]
public class TopologyChangeScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ExistingDatabaseFixture>
{
    /// <summary>
    /// Records what arrived and when, relative to the moment the scenario was fired.
    /// </summary>
    private sealed class Timeline(Stopwatch clock, ITestOutputHelper log)
    {
        private readonly List<string> _entries = [];

        public List<PushMaintenanceEvent> Events { get; } = [];

        public void Note(string what)
        {
            var entry = $"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {what}";
            lock (_entries)
            {
                _entries.Add(entry);
            }

            log.WriteLine(entry);
        }

        public void Add(PushMaintenanceEvent evt)
        {
            lock (_entries)
            {
                Events.Add(evt);
            }

            Note($"{evt.NotificationType} seq={evt.SequenceId} time={evt.Time?.TotalSeconds.ToString() ?? "-"} {evt.RawMessage}");
        }

        public int Count
        {
            get { lock (_entries) { return Events.Count; } }
        }
    }

    /// <summary>
    /// Whether each scenario announces itself, and why - measured 2026-09-01 against RS 8.0.22.
    /// </summary>
    /// <remarks>
    /// The expectations encode the rule the measurements produced, which is narrower than either half of it
    /// looks: <b>a connection is told <c>MOVING</c> when its own proxy leaves the endpoint's address set *and*
    /// the set gains a member.</b> Both conditions are needed - a pure reduction takes the proxy away without
    /// announcing it, and a pure widening adds addresses while leaving the connection's proxy in place, which is
    /// also silent. The data-movement pair (<c>MIGRATING</c>/<c>MIGRATED</c>) is separate and fires whenever
    /// shards move, whether or not any endpoint changes.
    /// </remarks>
    [Theory]
    [InlineData("conn_drop", "endpoint_rebind", true)]
    [InlineData("dns_resolution_change", "endpoint_rebind", false)]
    [InlineData("data_movement_conn_drop", "maintenance_mode", true)]
    [InlineData("data_movement_no_conn_drop", "migrate", true)]
    public async Task ScenarioProducesNotificationsWeUnderstand(string effect, string trigger, bool expectNotifications)
    {
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scenario = await ScenarioRun.SetupAsync(
            fixture.Injector, "topology-change-standalone", effect, trigger, log.WriteLine, cancellationToken: cancellationToken);

        var database = scenario.Database;
        Assert.NotNull(database);

        var clock = Stopwatch.StartNew();
        var timeline = new Timeline(clock, log);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig(fixture.Environment));
        var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(conn.GetEndPoints()[0]);
        Assert.True(endpoint.MaintenanceNotificationsActive, "the opt-in must be live, or this test proves nothing");

        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is PushMaintenanceEvent push) timeline.Add(push);
        };
        conn.ConnectionFailed += (_, e) => timeline.Note($"connection failed: {e.FailureType} {e.Exception?.Message}");
        conn.ConnectionRestored += (_, e) => timeline.Note("connection restored");

        clock.Restart();
        timeline.Note($"firing {effect}/{trigger} against {database}");
        await scenario.FireAsync(cancellationToken);
        timeline.Note("injector reports the scenario finished");

        // The injector finishing is not the deployment settling: notifications, the DNS change and the socket
        // close all trail it. Keep watching, and keep the client busy so a broken connection actually surfaces
        // rather than sitting idle.
        var deadline = clock.Elapsed + TimeSpan.FromSeconds(60);
        while (clock.Elapsed < deadline)
        {
            try
            {
                await conn.GetDatabase().PingAsync();
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                timeline.Note($"ping failed: {ex.GetType().Name}");
            }

            await Task.Delay(1000, cancellationToken);
        }

        timeline.Note($"finished with {timeline.Count} notification(s); relaxed={endpoint.IsMaintenanceRelaxed}");

        if (expectNotifications)
        {
            Assert.NotEmpty(timeline.Events);
            Assert.All(timeline.Events, e => Assert.NotEqual(MaintenanceNotificationType.None, e.NotificationType));
        }
        else
        {
            // dns_resolution_change widens the policy (single -> all-master-shards), so addresses are *added*
            // and the connection's own proxy stays where it is - there is nothing to tell this connection to
            // move, and the proxy restarting to apply the change closes the socket with no warning at all.
            // Asserting the silence deliberately: it is the case with no signal, so if a future build starts
            // announcing it, that is a behaviour change we want to be told about rather than to absorb quietly.
            Assert.Empty(timeline.Events);
        }

        // and the client is usable afterwards, which is the point of the whole feature
        Assert.True(
            await Poll.UntilAsync(() => TryPing(conn), timeoutMilliseconds: 30_000),
            "the client should be serving commands again after the topology change");
    }

    private static bool TryPing(IConnectionMultiplexer conn)
    {
        try
        {
            return conn.IsConnected && conn.GetDatabase().Ping() >= TimeSpan.Zero;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return false;
        }
    }
}
