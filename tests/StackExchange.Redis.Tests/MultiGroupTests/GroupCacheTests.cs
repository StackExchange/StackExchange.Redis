using System;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

/// <summary>
/// Client-side caching in a connection group: each member keeps its own cache, and this process's writes
/// reach all of them.
/// </summary>
/// <remarks>
/// The two in-process servers do not replicate, which is the point: a write made through B never reaches
/// A's server, so A's tracking connection never hears of it - a replication lag that never catches up. Only
/// the client can know A's entry is out of date, because only the client made the write.
/// </remarks>
public class GroupCacheTests(ITestOutputHelper log)
{
    private static async Task WaitForAsync(Func<Task<bool>> condition, string because)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 5000)
        {
            if (await condition()) return;
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(await condition(), because);
    }

    private ConfigurationOptions Config(InProcessTestServer server)
    {
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp3;
        return config;
    }

    // the cache is a group-level setting (MultiGroupOptions.ClientCache), inherited by every member
    private static MultiGroupOptions.Builder Options() => new()
    {
        ClientCache = new CacheOptions { Admission = CacheAdmission.OnFirstMiss },
    };

    public enum WritePath
    {
        Database,
        Context,
    }

    [Theory]
    [InlineData(WritePath.Database)]
    [InlineData(WritePath.Context)]
    public async Task AWriteWhileFailedOverIsNotServedFromTheOldMembersCache(WritePath path)
    {
        using var serverA = new InProcessTestServer(log);
        using var serverB = new InProcessTestServer(log);

        ConnectionGroupMember a = new(Config(serverA), "a") { Weight = 9 }, b = new(Config(serverB), "b") { Weight = 2 };
        var options = Options();
        options.HealthCheck = new HealthCheck.Builder { Probe = HealthCheckProbe.Ping };
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync([a, b], options);
        await GroupWait.AssertConnectedAsync(conn);
        await WaitForAsync(() => Task.FromResult(a.IsConnected && b.IsConnected), "both members connected");

        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        var cacheA = typed.GetMembers()[0].Multiplexer.ClientCache;
        Assert.NotNull(cacheA);
        Assert.Same(a, conn.ActiveMember);

        // the same starting value on both servers, as replication would have left it
        var db = conn.GetDatabase();
        await db.StringSetAsync("k", "old");
        Assert.True(conn.TryFailoverTo(b));
        await db.StringSetAsync("k", "old");
        Assert.True(conn.TryFailoverTo(null));
        Assert.Same(a, conn.ActiveMember);

        // cached on A: a second read is served locally
        await WaitForAsync(async () => (string?)await db.Strings.GetAsync("k") == "old" && cacheA.Count == 1, "'k' cached on A");
        var stored = cacheA.Stored;
        Assert.Equal("old", (string?)await db.Strings.GetAsync("k"));
        Assert.Equal(stored, cacheA.Stored);

        // fail over and write through B; A's server never hears of it
        Assert.True(conn.TryFailoverTo(b));
        Assert.Same(b, conn.ActiveMember);
        if (path == WritePath.Database)
        {
            await db.StringSetAsync("k", "new");
        }
        else
        {
            await db.Strings.SetAsync("k", "new");
        }

        // fail back: A's entry must not answer, since this process replaced that value. What A's server says
        // is its own business (here, the old value: it never caught up); what matters is that it is ASKED
        Assert.True(conn.TryFailoverTo(null));
        Assert.Same(a, conn.ActiveMember);
        await db.Strings.GetAsync("k");
        Assert.Equal(stored + 1, cacheA.Stored);
    }

    [Fact]
    public async Task ARemovedMembersCacheIsNoLongerStamped()
    {
        using var serverA = new InProcessTestServer(log);
        using var serverB = new InProcessTestServer(log);

        ConnectionGroupMember a = new(Config(serverA), "a") { Weight = 9 }, b = new(Config(serverB), "b") { Weight = 2 };
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync([a, b], Options());
        await GroupWait.AssertConnectedAsync(conn);

        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        var cacheA = typed.GetMembers()[0].Multiplexer.ClientCache!;
        var cacheB = typed.GetMembers()[1].Multiplexer.ClientCache!;
        Assert.Same(cacheB, Assert.Single(cacheA.Peers));
        Assert.Same(cacheA, Assert.Single(cacheB.Peers));

        // B is gone: A no longer names it as a peer, and B names nobody
        Assert.True(conn.Remove(b));
        Assert.Empty(cacheA.Peers);
        Assert.Empty(cacheB.Peers);
    }

    [Fact]
    public async Task TheGroupCacheReachesEveryMemberWithoutTouchingTheirOptions()
    {
        using var serverA = new InProcessTestServer(log);
        using var serverB = new InProcessTestServer(log);
        using var serverC = new InProcessTestServer(log);
        ConnectionGroupMember a = new(Config(serverA), "a") { Weight = 9 }, b = new(Config(serverB), "b") { Weight = 2 };
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync([a, b], Options());
        await GroupWait.AssertConnectedAsync(conn);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);

        // every member runs the group's cache, and the callers' options were not written into
        var cacheA = typed.GetMembers()[0].Multiplexer.ClientCache;
        var cacheB = typed.GetMembers()[1].Multiplexer.ClientCache;
        Assert.NotNull(cacheA);
        Assert.NotNull(cacheB);
        Assert.Same(typed.Options.ClientCache, cacheA.Options);
        Assert.Same(typed.Options.ClientCache, cacheB.Options);
        Assert.Null(a.Configuration.ClientCache);
        Assert.Null(b.Configuration.ClientCache);
        Assert.NotNull(conn.GetClientCache());

        // ...including one that joins later, which also becomes a peer
        ConnectionGroupMember c = new(Config(serverC), "c") { Weight = 1 };
        await typed.AddAsync(c);
        var cacheC = c.Multiplexer.ClientCache;
        Assert.NotNull(cacheC);
        Assert.Contains(cacheC, cacheA.Peers);
        Assert.Contains(cacheA, cacheC.Peers);
    }

    [Fact]
    public async Task AMemberThatBringsItsOwnCacheIsRefused()
    {
        // the cache is the group's, so that members cannot disagree about what may be cached - a failover
        // would otherwise change what callers get; refused up front, before anything connects
        using var serverA = new InProcessTestServer(log);
        using var serverB = new InProcessTestServer(log);
        var own = Config(serverB);
        own.ClientCache = new CacheOptions();
        ConnectionGroupMember a = new(Config(serverA), "a") { Weight = 9 }, b = new(own, "b") { Weight = 2 };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => ConnectionMultiplexer.ConnectGroupAsync([a, b], Options()));
        Assert.Contains("MultiGroupOptions.ClientCache", ex.Message);
        Assert.Contains("'b'", ex.Message);

        // and the same for a member added later
        ConnectionGroupMember solo = new(Config(serverA), "a") { Weight = 9 };
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync([solo], Options());
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        var late = new ConnectionGroupMember(own, "late");
        ex = await Assert.ThrowsAsync<ArgumentException>(() => typed.AddAsync(late));
        Assert.Contains("MultiGroupOptions.ClientCache", ex.Message);
    }
}
