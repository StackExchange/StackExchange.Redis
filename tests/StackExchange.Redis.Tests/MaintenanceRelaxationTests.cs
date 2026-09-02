using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Timeout relaxation: a server that announces a disruption gets its command timeouts raised for the duration,
/// as a floor and never a reduction. The window arithmetic is asserted through internals rather than by waiting
/// out real seconds - the interesting properties are "does it open, extend, close, and expire correctly", and a
/// test that sleeps for 30s to watch a cap expire is a test nobody runs.
/// </summary>
public class MaintenanceRelaxationTests(ITestOutputHelper log)
{
    private static async Task<(InProcessTestServer Server, ConnectionMultiplexer Connection)> ConnectAsync(
        ITestOutputHelper log,
        Action<ConfigurationOptions>? configure = null)
    {
        var server = new InProcessTestServer(log);
        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.MaintenanceNotifications = MaintenanceNotificationMode.Enabled;
        configure?.Invoke(config);

        var conn = await ConnectionMultiplexer.ConnectAsync(config);
        return (server, conn);
    }

    private static ServerEndPoint Endpoint(IConnectionMultiplexer conn, InProcessTestServer server)
        => ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);

    /// <summary>
    /// The notification arrives on the connection's read loop, so a test that sends one has to let it land.
    /// </summary>
    private static async Task<bool> UntilRelaxedAsync(ServerEndPoint endpoint, bool expected, int timeoutMilliseconds = 5000)
    {
        for (int i = 0; i < timeoutMilliseconds / 25 && endpoint.IsMaintenanceRelaxed != expected; i++)
        {
            await Task.Delay(25);
        }
        return endpoint.IsMaintenanceRelaxed;
    }

    [Fact]
    public async Task NoWindowMeansNoChange()
    {
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            Assert.False(endpoint.IsMaintenanceRelaxed);
            Assert.Equal(1234, endpoint.GetEffectiveTimeoutMilliseconds(1234));
        }
    }

    [Fact]
    public async Task OpeningNotificationRelaxesTimeouts()
    {
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);

            Assert.True(await UntilRelaxedAsync(endpoint, true), "should be relaxed");

            // 10s relaxed against a 5s configured timeout
            Assert.Equal(10_000, endpoint.GetEffectiveTimeoutMilliseconds(5_000));
        }
    }

    [Fact]
    public async Task RelaxationIsAFloorAndNeverAReduction()
    {
        // a caller with a generous timeout keeps it; this is explicit in the contract
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
            Assert.True(await UntilRelaxedAsync(endpoint, true));

            Assert.Equal(60_000, endpoint.GetEffectiveTimeoutMilliseconds(60_000));
        }
    }

    [Fact]
    public async Task ClosingNotificationLeavesThePostEventTail()
    {
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
            Assert.True(await UntilRelaxedAsync(endpoint, true));

            server.SendShardNotification(null, MaintenanceNotificationKind.Migrated, timeSeconds: 0);
            await Task.Delay(250); // let it land; the tail means "still relaxed", so there is no flag to await

            // the server said it finished, and we are *still* relaxed - because that is when every other
            // client that got the same notification comes back
            Assert.True(endpoint.IsMaintenanceRelaxed, "the post-event tail should still be running");
            Assert.Equal(10_000, endpoint.GetEffectiveTimeoutMilliseconds(5_000));
        }
    }

    [Fact]
    public async Task ACatchUpCompletionOpensNoTail()
    {
        // Measured on a live deployment: a completion is retained and replayed to whoever opts in next, with
        // no age limit found (the same FAILED_OVER came back three hours later), and completions carry
        // no time field - so nothing in the frame says how old it is. Relaxing on one would mean every new
        // connection to a database that had ever failed over started life patient about timeouts, and
        // reporting any that expired as caused by maintenance long since finished.
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            endpoint.OnMaintenanceWindowClosed(MaintenanceNotificationType.FailedOver, sequenceId: 41, isCatchUp: true);

            Assert.False(endpoint.IsMaintenanceRelaxed, "a catch-up completion should not relax anything");
            Assert.Equal(MaintenanceNotificationType.None, endpoint.ActiveMaintenanceType);
            Assert.Equal(1234, endpoint.GetEffectiveTimeoutMilliseconds(1234));
        }
    }

    [Fact]
    public async Task ACatchUpCompletionDoesNotCancelALiveWindow()
    {
        // The reason the catch-up path declines to *open* a window rather than closing one. Relaxation belongs
        // to the ServerEndPoint and is shared by both bridges, so a subscription bridge that reconnects
        // mid-disruption - and is handed the retained completion of some *earlier* event on the way in - must
        // not cancel the window the live notification opened on the interactive bridge. Driven through the
        // internals because the transport-level version of this needs one bridge to reconnect while another
        // stays up, which is a lot of scaffolding to assert a state-machine rule.
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            server.SendShardNotification(null, MaintenanceNotificationKind.FailingOver, timeSeconds: 60);
            Assert.True(await UntilRelaxedAsync(endpoint, true), "the live notification should have opened a window");

            // an unrelated, older completion arrives as catch-up on a reconnecting bridge
            endpoint.OnMaintenanceWindowClosed(MaintenanceNotificationType.Migrated, sequenceId: 7, isCatchUp: true);

            Assert.True(endpoint.IsMaintenanceRelaxed, "the live window should have survived the catch-up");
            Assert.Equal(MaintenanceNotificationType.FailingOver, endpoint.ActiveMaintenanceType);
        }
    }

    [Fact]
    public async Task ClosingNotificationEndsItWhenThereIsNoTail()
    {
        var (server, conn) = await ConnectAsync(log, config => config.MaintenancePostEventRelaxedDuration = TimeSpan.Zero);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
            Assert.True(await UntilRelaxedAsync(endpoint, true));

            server.SendShardNotification(null, MaintenanceNotificationKind.Migrated, timeSeconds: 0);
            Assert.False(await UntilRelaxedAsync(endpoint, false), "should have ended");
        }
    }

    [Fact]
    public async Task WindowExpiresOnItsOwn()
    {
        // the cap and the ordinary deadline share a mechanism; a one-second floor lets us watch it expire
        // without a test that sleeps for half a minute
        var (server, conn) = await ConnectAsync(log, config =>
        {
            config.MaintenanceRelaxedTimeout = TimeSpan.FromSeconds(1);
            config.MaintenanceRelaxedWindowMax = TimeSpan.FromSeconds(1);
            config.MaintenancePostEventRelaxedDuration = TimeSpan.Zero;
        });
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);

            // asks for 20s, capped to 1s
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
            Assert.True(await UntilRelaxedAsync(endpoint, true));

            Assert.False(await UntilRelaxedAsync(endpoint, false), "the cap should have ended it");
        }
    }

    [Fact]
    public async Task NoTailAfterACapExpiry()
    {
        // if the window ended on the cap we never learned the event finished, so extending past the backstop
        // would defeat the backstop
        var (server, conn) = await ConnectAsync(log, config =>
        {
            config.MaintenanceRelaxedTimeout = TimeSpan.FromSeconds(1);
            config.MaintenanceRelaxedWindowMax = TimeSpan.FromSeconds(1);
            config.MaintenancePostEventRelaxedDuration = TimeSpan.FromSeconds(30); // would be very visible
        });
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
            Assert.True(await UntilRelaxedAsync(endpoint, true));

            Assert.False(await UntilRelaxedAsync(endpoint, false), "no tail should follow a capped window");
        }
    }

    [Fact]
    public async Task ShorterNotificationDoesNotCutAnOpenWindowShort()
    {
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);

            // a long window, then a short one on top: the short one must not shorten it
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 25);
            Assert.True(await UntilRelaxedAsync(endpoint, true));
            server.SendShardNotification(null, MaintenanceNotificationKind.FailingOver, timeSeconds: 1);
            await Task.Delay(1500);

            Assert.True(endpoint.IsMaintenanceRelaxed, "the longer window should still be in force");
        }
    }

    [Fact]
    public async Task ReplayedSequenceIdIsIgnored()
    {
        // our own invention, so kept conservative: an id we have already acted on cannot extend a window
        var (server, conn) = await ConnectAsync(log, config =>
        {
            config.MaintenanceRelaxedTimeout = TimeSpan.FromSeconds(1);
            config.MaintenanceRelaxedWindowMax = TimeSpan.FromSeconds(1);
            config.MaintenancePostEventRelaxedDuration = TimeSpan.Zero;
        });
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            var seq = server.NextMaintenanceSequenceId;
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
            Assert.True(await UntilRelaxedAsync(endpoint, true));
            Assert.False(await UntilRelaxedAsync(endpoint, false));

            // replaying the same id must not reopen anything
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20, sequenceId: seq);
            await Task.Delay(250);
            Assert.False(endpoint.IsMaintenanceRelaxed, "a replayed id should not reopen the window");

            // ...but a genuinely new one still does
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
            Assert.True(await UntilRelaxedAsync(endpoint, true), "a new id should still work");
        }
    }

    [Fact]
    public async Task RelaxationDoesNotSuppressConnectionFailure()
    {
        // the regression that would matter most: relaxation must touch command timeouts only. If it reached
        // keep-alive or failure detection, a server that died mid-maintenance would linger for the window -
        // turning a latency mitigation into an availability regression
        var (server, conn) = await ConnectAsync(log, config =>
        {
            config.AllowSimulateConnectionFailure = true;
            config.MaintenanceRelaxedTimeout = TimeSpan.FromMinutes(5); // absurdly generous, deliberately
            config.MaintenanceRelaxedWindowMax = TimeSpan.FromMinutes(5);
        });
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            server.SendShardNotification(null, MaintenanceNotificationKind.FailingOver, timeSeconds: 300);
            Assert.True(await UntilRelaxedAsync(endpoint, true));

            var failures = 0;
            conn.ConnectionFailed += (_, _) => Interlocked.Increment(ref failures);
            conn.GetServer(server.DefaultEndPoint).SimulateConnectionFailure(SimulatedFailureType.All);

            for (int i = 0; i < 100 && Volatile.Read(ref failures) == 0; i++)
            {
                await Task.Delay(25);
            }

            log.WriteLine($"connection failures observed: {Volatile.Read(ref failures)}");
            Assert.True(Volatile.Read(ref failures) > 0, "a dead connection must still be noticed during a relaxed window");
        }
    }

    [Theory]
    [InlineData(true, MaintenanceNotificationType.Migrating)]
    [InlineData(false, MaintenanceNotificationType.None)]
    public async Task TimeoutReportsWhetherMaintenanceWasInForce(bool announce, MaintenanceNotificationType expected)
    {
        // "timeout" and "timeout during an announced migration" call for very different reactions from
        // whoever reads the log, so the fault says which it was
        var (server, conn) = await ConnectAsync(log, config =>
        {
            config.AsyncTimeout = 200;
            config.SyncTimeout = 200;
            config.MaintenanceRelaxedTimeout = TimeSpan.FromSeconds(1);
            config.MaintenanceRelaxedWindowMax = TimeSpan.FromSeconds(30);
            config.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
        });
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            if (announce)
            {
                server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
                Assert.True(await UntilRelaxedAsync(endpoint, true));
            }

            server.SetLatency(TimeSpan.FromSeconds(5)); // longer than any timeout in play
            var ex = await Assert.ThrowsAsync<RedisTimeoutException>(() => conn.GetDatabase().StringGetAsync("maint-fault"));
            log.WriteLine($"{ex.MaintenanceType}: {ex.Message}");
            Assert.Equal(expected, ex.MaintenanceType);

            // and it reaches the circuit-breaker/retry surface the same way
            var context = new Availability.FaultContext(ex);
            Assert.Equal(expected, context.MaintenanceType);

            server.SetLatency(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task CommandsStillWorkThroughAWindow()
    {
        // relaxation is invisible to anything that is not timing out
        var (server, conn) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var endpoint = Endpoint(conn, server);
            server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 20);
            Assert.True(await UntilRelaxedAsync(endpoint, true));

            var db = conn.GetDatabase();
            for (int i = 0; i < 20; i++)
            {
                await db.StringSetAsync($"relax-{i}", i);
            }
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(i, (int)await db.StringGetAsync($"relax-{i}"));
            }

            // including the synchronous path, which has its own re-wait loop
            Assert.Equal(7, (int)db.StringGet("relax-7"));
        }
    }
}
