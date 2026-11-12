using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Sentinel names an unreachable primary when the client connects (<c>AbortOnConnectFail=false</c>), and later corrects
/// itself without a failover - so without <c>+switch-master</c> and without any connection being dropped. The client
/// has to find the corrected primary on its own, through the public <c>Connect</c>/<c>ConnectAsync</c>.
/// </summary>
public class SentinelInitialUnavailablePrimaryUnitTests(ITestOutputHelper log)
{
    private const string ServiceName = "mymaster";

    // documentation-range addresses (RFC 5737): a node that is not in-process can never be reached by accident;
    // Unreachable is named by sentinel but served by nothing
    private static readonly EndPoint Unreachable = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 6379);
    private static readonly EndPoint Primary = new IPEndPoint(IPAddress.Parse("192.0.2.2"), 6379);
    private static readonly EndPoint SentinelEndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 26379);

    // the sentinel host as a connection string usually names it, without a port: the sentinel connection fills in the
    // sentinel port, but anything connecting to it as a data node fills in the data port - where some other server listens
    private const string PortlessSentinelHost = "192.0.2.10";
    private static readonly EndPoint ColocatedRedis = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 6379);
    private const string HeldDown = "master,s_down,disconnected";

    // how long a command queued while the client is stuck may wait: far beyond the recovery, as the reporter's 60s
    private const int PendingTimeoutMilliseconds = 60_000;

    [Theory]
    [InlineData(false, "master")]
    [InlineData(true, "master")]
    [InlineData(false, HeldDown)]
    [InlineData(true, HeldDown)]
    public async Task CorrectedSentinelPrimaryIsFollowedWithoutSwitchMaster(bool asynchronous, string initialFlags)
    {
        using var data = new InProcessTestServer(log, Primary);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, Unreachable, SentinelEndPoint, log)
        {
            PrimaryFlags = initialFlags,
        };
        await using var conn = await ConnectAsync(CreateConfig(data, sentinel), asynchronous);
        // the client waits at the address sentinel names, held down or not, which nothing serves: connected to no data node
        Assert.Contains(Unreachable, conn.GetEndPoints());
        Assert.Empty(ConnectedDataNodes(conn));
        Assert.DoesNotContain(Primary, conn.GetEndPoints());

        CorrectSentinel(sentinel);
        await AssertFollowsPrimaryAsync(conn);

        var db = conn.GetDatabase();
        Assert.True(await db.StringSetAsync("initial:recovered", "ok"));
        Assert.Equal("ok", (string?)await db.StringGetAsync("initial:recovered"));

        // pub/sub works on the corrected primary too, not only commands
        var subscriber = conn.GetSubscriber();
        var channel = RedisChannel.Literal("initial:fresh");
        var queue = await subscriber.SubscribeAsync(channel);
        await subscriber.PublishAsync(channel, "fresh");
        var read = queue.ReadAsync().AsTask();
        Assert.Same(read, await Task.WhenAny(read, Task.Delay(5000)));
        Assert.Equal("fresh", (string?)(await read).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrimaryHeldDownIsAwaitedAtItsAddressNotOnTheSentinelHost(bool asynchronous)
    {
        using var data = new InProcessTestServer(log, Primary);
        using var colocated = new InProcessTestServer(log, ColocatedRedis);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, Unreachable, SentinelEndPoint, log) { PrimaryFlags = HeldDown };
        await using var conn = await ConnectAsync(CreatePortlessConfig(data, sentinel, colocated), asynchronous);

        var write = await Record.ExceptionAsync(() => conn.GetDatabase().StringSetAsync("held-down:key", "misplaced"));
        log.WriteLine($"write while held down: {write?.GetType().Name ?? "succeeded"}");
        await using (var direct = await ConnectionMultiplexer.ConnectAsync(colocated.GetClientConfig()))
        {
            Assert.False(await direct.GetDatabase().KeyExistsAsync("held-down:key"), "the write landed on the server on the sentinel host");
        }
        Assert.Contains(Unreachable, conn.GetEndPoints());
        Assert.DoesNotContain(ColocatedRedis, conn.GetEndPoints());

        CorrectSentinel(sentinel);
        await AssertFollowsPrimaryAsync(conn);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SeededClientLeavesTheSentinelHostOnceSentinelNamesAPrimaryHeldDown(bool asynchronous, bool replicaQueryFails)
    {
        using var data = new InProcessTestServer(log, Primary);
        using var colocated = new InProcessTestServer(log, ColocatedRedis);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, Unreachable, SentinelEndPoint, log) { PrimaryFlags = HeldDown };
        await using var conn = await ConnectSeededOnTheSentinelHostAsync(data, sentinel, colocated, asynchronous);

        // sentinel is back, but holds the primary down - and may not list its replicas
        if (replicaQueryFails) sentinel.ReplicaError = "NOPERM User client has no permissions to run the 'sentinel|replicas' command";
        sentinel.AddEmptyNode(SentinelEndPoint);
        await AssertLeavesTheSentinelHostAsync(conn);

        CorrectSentinel(sentinel);
        await AssertFollowsPrimaryAsync(conn);
    }

    [Fact]
    public async Task SeededClientLeavesTheSentinelHostAfterAnAttemptThatLostTheReconfigureGate()
    {
        using var data = new InProcessTestServer(log, Primary);
        using var colocated = new InProcessTestServer(log, ColocatedRedis);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, Unreachable, SentinelEndPoint, log) { PrimaryFlags = HeldDown };
        await using var conn = await ConnectSeededOnTheSentinelHostAsync(data, sentinel, colocated, asynchronous: true);

        // another reconfigure holds the gate when sentinel comes back, holding the primary down
        await UntilAsync(() => conn.TryClaimReconfigure("unit holds the claim"), "could not claim reconfigure");
        try
        {
            sentinel.AddEmptyNode(SentinelEndPoint);
            await UntilAsync(() => sentinel.ReplicaQueries > 0, "no attempt to leave the seed reached the gate");
            // a later attempt reaching the gate again means the first one has lost it
            int reached = sentinel.ReplicaQueries;
            await UntilAsync(() => sentinel.ReplicaQueries > reached, "the attempt that lost the gate was not repeated");
            Assert.Contains(ColocatedRedis, conn.GetEndPoints());
        }
        finally
        {
            conn.ReleaseReconfigureClaim();
        }

        await AssertLeavesTheSentinelHostAsync(conn);
        CorrectSentinel(sentinel);
        await AssertFollowsPrimaryAsync(conn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientSeededWithTheSentinelItselfFollowsThePrimaryOnceSentinelKnowsIt(bool asynchronous)
    {
        using var data = new InProcessTestServer(log, Primary);
        // sentinel does not know the service yet: neither query gives any address
        using var sentinel = new InProcessSentinelServer(data, ServiceName, Unreachable, SentinelEndPoint, log)
        {
            SupportsSentinelMaster = false,
            LegacyAnswersNothing = true,
        };
        await using var conn = await ConnectAsync(CreateConfig(data, sentinel), asynchronous);
        // seeded with the configured endpoint, which has the sentinel port: connected, but to no data node
        Assert.Contains(SentinelEndPoint, conn.GetEndPoints());
        Assert.Empty(ConnectedDataNodes(conn));

        sentinel.SupportsSentinelMaster = true;
        sentinel.LegacyAnswersNothing = false;
        CorrectSentinel(sentinel);
        await AssertFollowsPrimaryAsync(conn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerSubscribedWhileStuckReceivesMessagesAfterRecovery(bool asynchronous)
    {
        using var data = new InProcessTestServer(log, Primary);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, Unreachable, SentinelEndPoint, log);
        await using var conn = await ConnectAsync(CreateConfig(data, sentinel), asynchronous);
        Assert.False(conn.IsConnected);
        KeepPendingWorkQueued(conn);

        var subscriber = conn.GetSubscriber();
        var pattern = RedisChannel.Pattern("initial:handler:*");
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscribing = subscriber.SubscribeAsync(pattern, (_, message) => received.TrySetResult(message));
        Assert.False(subscribing.IsCompleted);

        CorrectSentinel(sentinel);
        await AssertFollowsPrimaryAsync(conn);

        // what happens to the subscribe call itself is not the point here; the handler is
        try { await subscribing; } catch (Exception ex) { log.WriteLine($"pending subscribe completed with {ex.GetType().Name}"); }
        await UntilAsync(() => Equals(subscriber.SubscribedEndpoint(pattern), Primary), "the pattern was not subscribed on the corrected primary");

        await subscriber.PublishAsync(RedisChannel.Literal("initial:handler:message"), "handler");
        Assert.Same(received.Task, await Task.WhenAny(received.Task, Task.Delay(5000)));
        Assert.Equal("handler", await received.Task);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandQueuedWhileStuckDoesNotWaitForItsTimeoutAfterRecovery(bool asynchronous)
    {
        using var data = new InProcessTestServer(log, Primary);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, Unreachable, SentinelEndPoint, log);
        await using var conn = await ConnectAsync(CreateConfig(data, sentinel), asynchronous);
        Assert.False(conn.IsConnected);
        KeepPendingWorkQueued(conn);

        var queued = conn.GetDatabase().StringSetAsync("initial:queued", "value");
        Assert.False(queued.IsCompleted);

        CorrectSentinel(sentinel);
        await AssertFollowsPrimaryAsync(conn);

        // the command may fail (it is not moved to the new primary today) or succeed (if it ever is), but it must not
        // sit out its timeout on a node the client has already left
        var sinceRecovery = Stopwatch.StartNew();
        Assert.Same(queued, await Task.WhenAny(queued, Task.Delay(5000)));
        try
        {
            await queued;
        }
        catch (Exception ex)
        {
            log.WriteLine($"queued command failed {sinceRecovery.ElapsedMilliseconds}ms after recovery with {ex.GetType().Name}: {ex.Message}");
            Assert.IsNotType<RedisTimeoutException>(ex);
        }
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(ConfigurationOptions config, bool asynchronous)
        => asynchronous ? await ConnectionMultiplexer.ConnectAsync(config) : ConnectionMultiplexer.Connect(config);

    // SyncTimeout also bounds the connect itself, so the long budget for queued work is set only once connected
    private static void KeepPendingWorkQueued(ConnectionMultiplexer conn)
        => conn.RawConfig.SyncTimeout = PendingTimeoutMilliseconds;

    // the equivalent of SENTINEL REMOVE + SENTINEL MONITOR <real primary>: no failover, no event, no socket killed
    private static void CorrectSentinel(InProcessSentinelServer sentinel)
    {
        sentinel.PrimaryFlags = "master";
        sentinel.Primary = Primary;
    }

    private async Task AssertFollowsPrimaryAsync(ConnectionMultiplexer conn)
    {
        var timer = Stopwatch.StartNew();
        await UntilAsync(
            () => conn.GetServers().Where(static s => s.IsConnected && !s.IsReplica && s.ServerType != ServerType.Sentinel)
                .Select(static s => s.EndPoint).SequenceEqual([Primary]),
            $"primary did not become {Format.ToString(Primary)}");
        log.WriteLine($"primary is {Format.ToString(Primary)} after {timer.ElapsedMilliseconds}ms");
        Assert.Equal(Primary, conn.GetDatabase().IdentifyEndpoint("initial:key", CommandFlags.DemandMaster));
    }

    private static EndPoint[] ConnectedDataNodes(ConnectionMultiplexer conn)
        => conn.GetServers().Where(static s => s.IsConnected && s.ServerType != ServerType.Sentinel).Select(static s => s.EndPoint).ToArray();

    private static ConfigurationOptions CreateConfig(InProcessTestServer data, InProcessSentinelServer sentinel)
    {
        var config = data.GetClientConfig();
        config.EndPoints.Clear();
        config.EndPoints.Add(SentinelEndPoint);
        config.ServiceName = ServiceName;
        config.Tunnel = InProcessSentinelServer.CreateTunnel(data, sentinel);
        config.AbortOnConnectFail = false;
        config.ConnectRetry = 1;
        config.ConnectTimeout = 250;
        config.SyncTimeout = 500;
        config.ConfigCheckSeconds = 1;
        return config;
    }

    // no sentinel answers at connect, so nothing names any address and the client is seeded with its configured
    // endpoint - which, without a port, is the server on the sentinel host
    private static async Task<ConnectionMultiplexer> ConnectSeededOnTheSentinelHostAsync(
        InProcessTestServer data, InProcessSentinelServer sentinel, InProcessTestServer colocated, bool asynchronous)
    {
        Assert.True(sentinel.TryGetNode(SentinelEndPoint, out var sentinelNode));
        sentinelNode.UpdateSlots([]);
        Assert.True(sentinel.RemoveNode(SentinelEndPoint));
        var conn = await ConnectAsync(CreatePortlessConfig(data, sentinel, colocated), asynchronous);
        Assert.Contains(ColocatedRedis, conn.GetEndPoints());
        return conn;
    }

    private static Task AssertLeavesTheSentinelHostAsync(ConnectionMultiplexer conn) => UntilAsync(
        () => conn.GetEndPoints().Contains(Unreachable) && !conn.GetEndPoints().Contains(ColocatedRedis),
        "the client did not leave the sentinel host for the address sentinel names");

    private static ConfigurationOptions CreatePortlessConfig(InProcessTestServer data, InProcessSentinelServer sentinel, InProcessTestServer colocated)
    {
        var config = CreateConfig(data, sentinel);
        config.EndPoints.Clear();
        config.EndPoints.Add(PortlessSentinelHost);
        config.Tunnel = InProcessSentinelServer.CreateTunnel(data, sentinel, colocated);
        return config;
    }

    private static async Task UntilAsync(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) Assert.Fail($"{failure} within {timer.Elapsed.TotalSeconds:0}s");
            await Task.Delay(100);
        }
    }
}
