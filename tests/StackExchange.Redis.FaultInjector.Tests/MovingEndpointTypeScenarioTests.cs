using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Does asking for a <c>moving-endpoint-type</c> make the server name a replacement?
/// </summary>
/// <remarks>
/// The experiment behind an open question. Eleven <c>MOVING</c> notifications observed on Redis Enterprise
/// 8.0.22 all carried an explicit null for the replacement address, including cases where the server had
/// already chosen the replacement node - and every one of those was requested with a bare
/// <c>CLIENT MAINT_NOTIFICATIONS ON</c>. So either this build never populates the field, or the server default
/// amounts to <c>none</c> and we were being given exactly what we asked for.
/// <para>
/// This distinguishes those two, which matters for more than tidiness: a named successor would let a handoff
/// skip the DNS wait entirely, and DNS has been measured trailing the notification by up to 18.7s against a
/// socket that closed at 15.7s. It also decides whether the named-successor code path is reachable at all, or
/// exists only because the contract mentions it.
/// </para>
/// <para>
/// Deliberately reports rather than asserts a populated address: "this build does not populate it" is a
/// legitimate answer, and the test's job is to record which answer we got, per endpoint type.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "moving-endpoint-type")]
public class MovingEndpointTypeScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ExistingDatabaseFixture>
{
    [Theory]
    [InlineData(MaintenanceEndpointType.ServerDefault)]
    [InlineData(MaintenanceEndpointType.ExternalFqdn)]
    [InlineData(MaintenanceEndpointType.ExternalIp)]
    [InlineData(MaintenanceEndpointType.InternalFqdn)]
    [InlineData(MaintenanceEndpointType.InternalIp)]
    public async Task DoesTheServerNameAReplacement(MaintenanceEndpointType type)
    {
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scenario = await ScenarioRun.SetupAsync(
            fixture.Injector, "topology-change-standalone", "conn_drop", "endpoint_rebind", log.WriteLine,
            cancellationToken: cancellationToken);

        var database = scenario.Database;
        Assert.NotNull(database);

        var config = database.GetClientConfig(fixture.Environment, MaintenanceNotificationMode.Auto);
        config.MaintenanceMovingEndpointType = type;

        var clock = Stopwatch.StartNew();
        var moving = new List<PushMaintenanceEvent>();

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(conn.GetEndPoints()[0]);

        // Auto rather than Enabled, because a server that rejects an endpoint type we asked for is one of the
        // outcomes being measured - and it should not fail the run.
        log.WriteLine($"requested {type}; opt-in active = {endpoint.MaintenanceNotificationsActive}");
        if (!endpoint.MaintenanceNotificationsActive)
        {
            log.WriteLine("=> the server refused this endpoint type; nothing further to observe");
            return;
        }

        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is PushMaintenanceEvent push)
            {
                log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {push.NotificationType} {push.RawMessage}");
                if (push.NotificationType == MaintenanceNotificationType.Moving)
                {
                    lock (moving) moving.Add(push);
                }
            }
        };

        clock.Restart();
        await scenario.FireAsync(cancellationToken);

        var deadline = clock.Elapsed + TimeSpan.FromSeconds(45);
        while (clock.Elapsed < deadline)
        {
            lock (moving)
            {
                if (moving.Count > 0) break;
            }

            await Task.Delay(500, cancellationToken);
        }

        lock (moving)
        {
            if (moving.Count == 0)
            {
                log.WriteLine("=> no MOVING arrived at all");
                return;
            }

            foreach (var push in moving)
            {
                log.WriteLine($"=> {type}: NewEndPoint = {push.NewEndPoint?.ToString() ?? "(null)"}; payload = {push.Payload ?? "(null)"}");
            }

            // The one thing worth asserting either way: whatever the server sent, we understood the frame.
            Assert.All(moving, push => Assert.Equal(MaintenanceNotificationType.Moving, push.NotificationType));
        }
    }
}
