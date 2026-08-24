using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The server half of the maintenance-notification contract, exercised directly. The client does not opt in
/// yet, so these drive the command as a caller would - which is also how any other test will be able to opt
/// in once the client does, since this is ordinary server functionality rather than a special test server.
/// </summary>
public class MaintenanceOptInServerTests(ITestOutputHelper log)
{
    private static InProcessTestServer CreateServer(ITestOutputHelper log) => new(log);

    private static async Task<RedisResult> OptInAsync(IConnectionMultiplexer conn, InProcessTestServer server, params object[] args)
        => await conn.GetServer(server.DefaultEndPoint).ExecuteAsync("client", args.Prepend("maint_notifications").ToArray());

    [Fact]
    public async Task BareOnIsAcceptedAndRecorded()
    {
        // "CLIENT MAINT_NOTIFICATIONS ON" with no parameters is explicitly valid, and means "server defaults"
        using var server = CreateServer(log);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        Assert.Equal("OK", (string?)await OptInAsync(conn, server, "on"));

        var client = Assert.Single(OptedIn(server));
        Assert.Null(client.MovingEndpointType); // server defaults, not a value we invented
        Assert.Equal(1, client.MaintenanceNotificationOptInCount);
    }

    [Theory]
    [InlineData("internal-ip")]
    [InlineData("internal-fqdn")]
    [InlineData("external-ip")]
    [InlineData("external-fqdn")]
    [InlineData("none")]
    public async Task EveryDefinedEndpointTypeIsAccepted(string endpointType)
    {
        using var server = CreateServer(log);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        Assert.Equal("OK", (string?)await OptInAsync(conn, server, "on", "moving-endpoint-type", endpointType));
        Assert.Equal(endpointType, Assert.Single(OptedIn(server)).MovingEndpointType);
    }

    [Fact]
    public async Task OffClearsTheOptIn()
    {
        using var server = CreateServer(log);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        await OptInAsync(conn, server, "on", "moving-endpoint-type", "external-fqdn");
        Assert.Single(OptedIn(server));

        Assert.Equal("OK", (string?)await OptInAsync(conn, server, "off"));
        Assert.Empty(OptedIn(server));
    }

    [Theory]
    [InlineData("sideways")]                              // not on/off
    [InlineData("on", "moving-endpoint-type", "sideways")] // undefined endpoint type
    [InlineData("on", "not-a-parameter", "value")]         // unknown parameter
    [InlineData("on", "moving-endpoint-type")]             // parameter with no value
    public async Task MalformedOptInIsRejected(params string[] args)
    {
        using var server = CreateServer(log);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var ex = await Assert.ThrowsAsync<RedisServerException>(
            async () => await OptInAsync(conn, server, args.Cast<object>().ToArray()));
        log.WriteLine(ex.Message);
        Assert.Empty(OptedIn(server));
    }

    [Theory]
    [InlineData(MaintenanceNotificationSupport.UnknownSubcommand)]
    [InlineData(MaintenanceNotificationSupport.Disabled)]
    public async Task UnsupportingServerRejectsTheOptIn(MaintenanceNotificationSupport support)
    {
        // the two ways a real server refuses: it has never heard of the subcommand (OSS, Valkey, Garnet), or
        // it knows it and has the feature flag off. A client has to survive both
        using var server = CreateServer(log);
        server.MaintenanceNotifications = support;
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var ex = await Assert.ThrowsAsync<RedisServerException>(
            async () => await OptInAsync(conn, server, "on"));
        log.WriteLine($"{support}: {ex.Message}");
        Assert.Empty(OptedIn(server));
    }

    [Fact]
    public async Task NotificationsGoOnlyToConnectionsThatOptedIn()
    {
        // a real server sends to the connections that asked; sending to everything would let a client pass a
        // test it should fail, by receiving notifications it never subscribed to
        using var server = CreateServer(log);
        await using var optedIn = await server.ConnectAsync(defaultOnly: true);
        await using var notOptedIn = await server.ConnectAsync(defaultOnly: true);

        await OptInAsync(optedIn, server, "on", "moving-endpoint-type", "external-fqdn");
        var subscribed = OptedIn(server).Count();
        log.WriteLine($"{subscribed} of {server.ClientCount} connections opted in");

        var sent = server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 5);
        Assert.Equal(subscribed, sent);
        Assert.NotEqual(server.ClientCount, sent);
    }

    [Fact]
    public async Task SequenceIdsAdvanceAndCanBeRepeated()
    {
        // the contract never defines these, so a client's use of them is its own invention - which means being
        // able to repeat one deliberately is part of what the fake owes us
        using var server = CreateServer(log);
        await using var conn = await server.ConnectAsync(defaultOnly: true);
        await OptInAsync(conn, server, "on");

        var first = server.NextMaintenanceSequenceId;
        Assert.Equal(1, server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, 5));
        Assert.Equal(first + 1, server.NextMaintenanceSequenceId);

        // and an explicit id does not advance the counter, so a replay stays a replay
        Assert.Equal(1, server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, 5, sequenceId: first));
        Assert.Equal(first + 1, server.NextMaintenanceSequenceId);
    }

    [Fact]
    public async Task RawPushIsNotGatedByOptIn()
    {
        // the malformed-payload hook, and the contrast is the point: with nobody opted in, a notification
        // reaches no one while a raw push still lands. Asserted as gated-versus-not rather than against
        // ClientCount, which is read at a different instant - under RESP2 the subscription connection can
        // register in between, so comparing totals is a race rather than a property
        using var server = CreateServer(log);
        await using var conn = await server.ConnectAsync(defaultOnly: true);

        var gated = server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 5);
        var ungated = server.SendRawPush(null, "NOT_A_REAL_KIND", "nonsense");
        log.WriteLine($"notification reached {gated}, raw push reached {ungated}");

        Assert.Equal(0, gated);
        Assert.True(ungated > 0, "a raw push should reach connections that never opted in");
    }

    private static System.Collections.Generic.IEnumerable<Server.RedisClient> OptedIn(InProcessTestServer server)
    {
        var found = new System.Collections.Generic.List<Server.RedisClient>();
        server.ForAllClients(c =>
        {
            if (c.MaintenanceNotifications) found.Add(c);
        });
        return found;
    }
}
