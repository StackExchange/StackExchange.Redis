using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A <c>-MOVED</c> whose target cannot be named: a hostname-preferring node redirecting to a peer that has
/// announced no hostname reports <c>MOVED &lt;slot&gt; ?:&lt;port&gt;</c>, and <c>?</c> denotes "an unknown
/// node" - explicitly not the node that answered, so it must not be substituted.
/// </summary>
/// <remarks>
/// Before this was handled, the redirect parsed as a <see cref="DnsEndPoint"/> whose host was literally
/// <c>"?"</c>; the client then created and dialled a <c>ServerEndPoint</c> for it, the command sat in the
/// backlog until it timed out, and the caller got a timeout exception pointing at the timeouts
/// troubleshooting article - with a phantom endpoint left behind in the multiplexer.
/// </remarks>
public class UnroutableRedirectUnitTests(ITestOutputHelper log)
{
    private const string Key = "unroutable-key";

    /// <summary>Cluster whose nodes prefer hostnames but announce none, so redirects between them say "?".</summary>
    private static InProcessTestServer CreateServer(ITestOutputHelper log, out EndPoint target)
    {
        var server = new InProcessTestServer(log)
        {
            ServerType = ServerType.Cluster,
            PreferredEndpointType = ClusterEndpointType.Hostname,
        };
        GetHost(server.DefaultEndPoint, out var port);
        target = new IPEndPoint(IPAddress.Loopback, port + 1);
        return server;
    }

    private static async Task<(ConnectionMultiplexer Conn, InProcessTestServer Server)> ConnectThenMigrateAsync(ITestOutputHelper log)
    {
        var server = CreateServer(log, out var targetEndpoint);
        var conn = await server.ConnectAsync(defaultOnly: true);

        var target = server.AddEmptyNode(targetEndpoint);

        // the target must be unaddressable by *every* form, not merely un-named. Hostnames are preferred and
        // it announces none, so the endpoint field is "?" - but the complement rule would then put its address
        // in the metadata, and a topology refresh landing between here and the command would discover it there
        // and route straight to it, leaving no redirect to test. Marking the address unknown as well removes
        // that race by construction rather than relying on the refresh not happening: under RESP2 the
        // subscription connection completing can trigger one, which is why this only ever failed on RESP2
        server.SetAnnouncedAddress(target, AnnouncedAddress.Empty);

        server.Migrate((RedisKey)Key, target);
        return (conn, server);
    }

    [Fact]
    public async Task UnroutableRedirectFaultsTheCommand()
    {
        var (conn, server) = await ConnectThenMigrateAsync(log);
        using (server)
        await using (conn)
        {
            var ex = await Assert.ThrowsAsync<RedisServerException>(
                () => conn.GetDatabase().StringGetAsync(Key));
            log.WriteLine(ex.Message);

            Assert.Equal(RedisErrorKind.UnknownRedirectTarget, ex.Kind);

            // the message must name the cause; the old behaviour blamed connectTimeout
            Assert.Contains("does not identify a node that can be connected to", ex.Message);
            Assert.DoesNotContain("connectTimeout", ex.Message);
        }
    }

    [Fact]
    public async Task UnroutableRedirectDoesNotInventAnEndpoint()
    {
        var (conn, server) = await ConnectThenMigrateAsync(log);
        using (server)
        await using (conn)
        {
            await Assert.ThrowsAsync<RedisServerException>(() => conn.GetDatabase().StringGetAsync(Key));

            foreach (var ep in conn.GetEndPoints())
            {
                log.WriteLine($"endpoint: {ep}");
            }

            // "?" used to be added as a DnsEndPoint and dialled forever
            Assert.DoesNotContain(conn.GetEndPoints(), ep => ep is DnsEndPoint { Host: "?" });
            Assert.All(conn.GetEndPoints(), ep => Assert.IsType<IPEndPoint>(ep));
        }
    }

    [Fact]
    public async Task UnroutableRedirectFailsWithoutWaitingForATimeout()
    {
        var (conn, server) = await ConnectThenMigrateAsync(log);
        using (server)
        await using (conn)
        {
            // the old path parked the command in the backlog for the full async timeout
            var timeout = conn.RawConfig.AsyncTimeout;
            var watch = Stopwatch.StartNew();
            await Assert.ThrowsAsync<RedisServerException>(() => conn.GetDatabase().StringGetAsync(Key));
            watch.Stop();

            log.WriteLine($"failed in {watch.ElapsedMilliseconds}ms, async timeout is {timeout}ms");
            Assert.True(watch.ElapsedMilliseconds < timeout / 2, $"took {watch.ElapsedMilliseconds}ms");
        }
    }

    [Fact]
    public async Task UnroutableRedirectIsTreatedAsNotApplied()
    {
        var (conn, server) = await ConnectThenMigrateAsync(log);
        using (server)
        await using (conn)
        {
            var ex = await Assert.ThrowsAsync<RedisServerException>(
                () => conn.GetDatabase().StringGetAsync(Key));

            // the redirect proves the command did not run, so a retry is a first attempt rather than a
            // repeat - which is what lets WithRetry recover once the topology refresh has landed
            var fault = new FaultContext(ex);
            Assert.True(fault.NotApplied);
            Assert.Equal(RedisErrorKind.UnknownRedirectTarget, fault.ErrorKind);
        }
    }

    [Fact]
    public async Task RefreshingTheTopologyDoesNotMakeTheTargetRoutable()
    {
        // this is the condition CI hit, asserted rather than avoided: a topology refresh between the migrate
        // and the command must not give the client a way to reach a node it cannot address. If it does, the
        // slot map routes directly, no redirect is issued, and every other test in this class silently stops
        // testing anything
        var (conn, server) = await ConnectThenMigrateAsync(log);
        using (server)
        await using (conn)
        {
            var slots = await conn.GetServer(server.DefaultEndPoint).ClusterSlotsAsync();
            var topology = ClusterTopology.From(slots);
            Assert.NotNull(topology);

            var mux = (ConnectionMultiplexer)conn;
            mux.UpdateClusterRange(topology);
            await mux.ApplyTopologyGenerationAsync(topology);

            foreach (var ep in conn.GetEndPoints())
            {
                log.WriteLine($"endpoint: {ep}");
            }

            // the target owns the slot but has no usable identity, so it cannot have become an endpoint
            GetHost(server.DefaultEndPoint, out var port);
            Assert.DoesNotContain(conn.GetEndPoints(), ep => PortOf(ep) == port + 1);

            static int PortOf(EndPoint ep) => ep switch
            {
                IPEndPoint ip => ip.Port,
                DnsEndPoint dns => dns.Port,
                _ => 0,
            };

            // ...and the command still earns the unroutable redirect
            var ex = await Assert.ThrowsAsync<RedisServerException>(
                () => conn.GetDatabase().StringGetAsync(Key));
            Assert.Equal(RedisErrorKind.UnknownRedirectTarget, ex.Kind);
        }
    }

    [Fact]
    public async Task RoutableRedirectStillFollowsNormally()
    {
        // the guard must not disturb ordinary redirects: same setup, but the target announces a hostname
        var server = CreateServer(log, out var targetEndpoint);
        using (server)
        {
            await using var conn = await server.ConnectAsync(defaultOnly: true);

            var target = server.AddEmptyNode(targetEndpoint);
            server.SetHostname(target, "target.redis.example.com");
            server.Migrate((RedisKey)Key, target);

            // the toy server maps any endpoint to an in-proc node, so the hostname form is dialable here
            await conn.GetDatabase().StringSetAsync(Key, "value");
            Assert.Equal("value", await conn.GetDatabase().StringGetAsync(Key));

            Assert.Contains(conn.GetEndPoints(), ep => ep is DnsEndPoint { Host: "target.redis.example.com" });
        }
    }
}
