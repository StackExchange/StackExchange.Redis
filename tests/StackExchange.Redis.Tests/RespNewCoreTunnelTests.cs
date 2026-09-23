using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The new core honouring <see cref="Configuration.Tunnel"/>: the endpoint is a label, and the tunnel
/// decides what is actually connected to.
/// </summary>
/// <remarks>
/// <para>
/// <b>This could not have passed before the transport factory landed.</b> The new core opened a socket
/// directly at the endpoint, so a tunnel - which is how proxies, in-process servers and anything else
/// that is not a plain TCP dial are expressed - was ignored entirely. The connection would have been
/// attempted against an address nothing is listening on.
/// </para>
/// <para>
/// <see cref="InProcessTestServer"/> is itself a tunnel, which makes it the honest test: it answers
/// <c>BeforeAuthenticateAsync</c> with its own stream and never binds a socket at all, so a core that
/// ignores tunnels cannot reach it by accident.
/// </para>
/// </remarks>
public class RespNewCoreTunnelTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task ATunnelledConnectionWorksThroughTheNewCore()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = RespNewCoreFixture.Wrap(conn, -1, null);

        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "through-the-tunnel", flags: CommandFlags.FireAndForget);

        Assert.Equal("through-the-tunnel", (string?)await db.StringGetAsync(key));
    }

    /// <summary>And routing questions are answered through the tunnel too, not only the first dial.</summary>
    /// <remarks>
    /// After a command, deliberately: this core connects lazily, so "is it connected?" asked before
    /// anything has been sent is honestly false - the connection has not been needed yet. An earlier
    /// version of this test asserted it up front and was simply wrong about the contract.
    /// </remarks>
    [Fact]
    public async Task ATunnelledConnectionReportsItsEndpoint()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = RespNewCoreFixture.Wrap(conn, -1, null);
        await db.PingAsync();

        Assert.True(db.IsConnected(Me()));
        Assert.NotNull(await db.IdentifyEndpointAsync(Me()));
    }
}
