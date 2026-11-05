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

    // how long a command queued while the client is stuck may wait: far beyond the recovery, as the reporter's 60s
    private const int PendingTimeoutMilliseconds = 60_000;

    [Theory]
    [InlineData(false, "master")]
    [InlineData(true, "master")]
    [InlineData(false, "master,s_down,disconnected")]
    [InlineData(true, "master,s_down,disconnected")]
    public async Task CorrectedSentinelPrimaryIsFollowedWithoutSwitchMaster(bool asynchronous, string initialFlags)
    {
        using var data = new InProcessTestServer(log, Primary);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, Unreachable, SentinelEndPoint, log)
        {
            PrimaryFlags = initialFlags,
        };
        await using var conn = await ConnectAsync(CreateConfig(data, sentinel), asynchronous);
        // a primary held down leaves no address to use, so the client is seeded with the sentinel itself: connected,
        // but to no data node
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
