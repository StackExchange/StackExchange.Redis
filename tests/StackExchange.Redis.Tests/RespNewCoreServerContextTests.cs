using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The new core's already-routed-server piece: a context pinned to one endpoint.
/// </summary>
/// <remarks>
/// <see cref="IServer"/> is the surface that needs it, and it is the one place where there is no routing
/// decision left to make - an endpoint executor <i>is</i> a server. What has to be true is that "pinned"
/// means pinned: a server context that quietly resolved like any other command would answer about
/// whichever node selection preferred, and every per-node answer (<c>INFO</c>, <c>DBSIZE</c>,
/// <c>CONFIG</c>) would be about the wrong one - plausibly, and silently.
/// </remarks>
[RunPerProtocol]
public class RespNewCoreServerContextTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    protected override string GetConfiguration()
        => TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

    private static string RunId(string? info)
    {
        foreach (var line in (info ?? "").Split('\n'))
        {
            if (line.StartsWith("run_id:")) return line.Substring("run_id:".Length).Trim();
        }

        return "";
    }

    /// <summary>Each endpoint's context reaches that endpoint, and no other.</summary>
    [Fact]
    public async Task AServerContextIsPinnedToItsEndpoint()
    {
        await using var conn = Create(allowAdmin: true, shared: false);
        var core = RespNewCoreFixture.CoreFor(conn);

        var endpoints = conn.GetEndPoints();
        Assert.True(endpoints.Length >= 2, "this needs two servers to be able to tell them apart");

        foreach (var endpoint in endpoints)
        {
            // the shipped IServer for this endpoint is the control: it is pinned by construction
            var expected = RunId(await conn.GetServer(endpoint).InfoRawAsync("server"));
            Assert.NotEqual("", expected);

            var actual = RunId(await core.ServerContext(endpoint).SendAsync<string>($"{RedisCommand.INFO}{(RedisValue)"server"}") ?? "");
            Assert.Equal(expected, actual);
        }

        // and the two servers really are distinguishable, so the loop above was not comparing a thing to
        // itself twice
        Assert.NotEqual(
            RunId(await conn.GetServer(endpoints[0]).InfoRawAsync("server")),
            RunId(await conn.GetServer(endpoints[1]).InfoRawAsync("server")));
    }

    /// <summary>A server command that takes a database takes it explicitly, and answers for that server.</summary>
    /// <remarks>
    /// <b>Against a private server</b>, because <c>DBSIZE</c> is a moving number: the first draft of this
    /// compared two readings taken from the shared topology and failed in the full suite while passing on
    /// its own, which is a test asserting that nothing else was running rather than anything about the
    /// code.
    /// </remarks>
    [Fact]
    public async Task ADatabaseScopedServerCommandWorks()
    {
        using var server = new InProcessTestServer();
        await using var conn = await server.ConnectAsync();
        var core = RespNewCoreFixture.CoreFor(conn);
        var endpoint = server.DefaultEndPoint;

        var db = conn.GetDatabase(0);
        for (var i = 0; i < 3; i++)
        {
            await db.StringSetAsync($"{Me()}-{i}", i);
        }

        // nobody else is writing here, so this is an exact number rather than a race
        Assert.Equal(3, await core.ServerContext(endpoint).Keyspace.CountAsync(0));
        Assert.Equal(await conn.GetServer(endpoint).DatabaseSizeAsync(0), await core.ServerContext(endpoint).Keyspace.CountAsync(0));
    }
}
