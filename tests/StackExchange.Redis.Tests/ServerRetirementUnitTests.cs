using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The retirement primitive: drain, then close, then forget. Driven directly through internals here - the
/// policies that decide *when* to retire (topology pruning, duplicate merging, and later the maintenance
/// handoffs) all call the same operation, so it is worth pinning on its own.
/// </summary>
public class ServerRetirementUnitTests(ITestOutputHelper log)
{
    private const string Hostname = "host-1.redis.example.com";

    private static InProcessTestServer CreateServer(ITestOutputHelper log)
        => new(log) { ServerType = ServerType.Cluster };

    [Fact]
    public async Task RetiredServerIsForgotten()
    {
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"retire-key", other);

        await using var conn = await server.ConnectAsync();
        var mux = (ConnectionMultiplexer)conn;
        Assert.Contains(other, conn.GetEndPoints());

        var target = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(other);
        await mux.RetireServerAsync(target, "test");

        // gone from the collection, and no longer resolvable
        Assert.DoesNotContain(other, conn.GetEndPoints());
        Assert.Throws<ArgumentException>(() => conn.GetServer(other));
    }

    [Fact]
    public async Task RetirementDropsSecondaryIdentities()
    {
        // the trap: an alias outliving its server would resolve to something disposed
        using var server = CreateServer(log);
        server.SetHostname(server.DefaultEndPoint, Hostname);
        GetHost(server.DefaultEndPoint, out var port);

        await using var conn = await server.ConnectAsync(defaultOnly: true);
        var byName = new DnsEndPoint(Hostname, port);
        Assert.NotNull(conn.GetServer(byName)); // resolvable via the alias

        var mux = (ConnectionMultiplexer)conn;
        var target = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
        await mux.RetireServerAsync(target, "test");

        Assert.Throws<ArgumentException>(() => conn.GetServer(byName));
        Assert.Throws<ArgumentException>(() => conn.GetServer(server.DefaultEndPoint));
    }

    [Fact]
    public async Task RetirementCompletesInFlightWork()
    {
        // the point of draining rather than disposing: work already written must still get its answer
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"retire-inflight", other);

        await using var conn = await server.ConnectAsync();
        var db = conn.GetDatabase();
        await db.StringSetAsync("retire-inflight", "value");

        // issue without awaiting, then retire underneath it
        var pending = Enumerable.Range(0, 50)
            .Select(_ => db.StringGetAsync("retire-inflight"))
            .ToArray();

        var mux = (ConnectionMultiplexer)conn;
        var target = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(other);
        await mux.RetireServerAsync(target, "test");

        var results = await Task.WhenAll(pending);
        Assert.All(results, x => Assert.Equal("value", x));
        log.WriteLine($"{results.Length} operations completed across the retirement");
    }

    [Fact]
    public async Task RetiredServerIsNotSelectedForNewWork()
    {
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"retire-select", other);

        await using var conn = await server.ConnectAsync();
        var mux = (ConnectionMultiplexer)conn;
        var target = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(other);

        await mux.RetireServerAsync(target, "test");

        // the slot it served has no owner now, so this must fail rather than reach a dead server
        var ex = await Record.ExceptionAsync(() => conn.GetDatabase().StringGetAsync("retire-select", CommandFlags.NoRedirect));
        log.WriteLine($"{ex?.GetType().Name}: {ex?.Message}");
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task RetiringTwiceIsHarmless()
    {
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"retire-twice", other);

        await using var conn = await server.ConnectAsync();
        var mux = (ConnectionMultiplexer)conn;
        var target = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(other);

        await mux.RetireServerAsync(target, "first");
        await mux.RetireServerAsync(target, "second"); // must not throw, must not resurrect
        Assert.DoesNotContain(other, conn.GetEndPoints());
    }

    [Fact]
    public async Task SnapshotRemovalLeavesOtherServersIntact()
    {
        // ServerSnapshot.Remove has to copy rather than compact in place; this is the shape that would break
        using var server = CreateServer(log);
        GetHost(server.DefaultEndPoint, out var port);
        var second = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        var third = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 2));
        server.Migrate((RedisKey)"snap-a", second);
        server.Migrate((RedisKey)"snap-b", third);

        await using var conn = await server.ConnectAsync();
        Assert.Equal(3, conn.GetEndPoints().Length);

        var mux = (ConnectionMultiplexer)conn;
        await mux.RetireServerAsync(((IInternalConnectionMultiplexer)conn).GetServerEndPoint(second), "test");

        var remaining = conn.GetEndPoints();
        log.WriteLine(string.Join(", ", remaining.Select(x => x.ToString())));
        Assert.Equal(2, remaining.Length);
        Assert.Contains(server.DefaultEndPoint, remaining);
        Assert.Contains(third, remaining);

        // and the survivors still work
        Assert.True(conn.GetServer(third).IsConnected);
    }
}
