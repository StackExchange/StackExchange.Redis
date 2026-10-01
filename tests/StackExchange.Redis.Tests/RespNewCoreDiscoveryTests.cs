using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What the new core's handshake can tell the client about a server, which is the gate on one engine.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in the <c>Message</c> inventory holds the shipped bridges up; <c>ReconfigureAsync</c> does.
/// It establishes every <c>ServerEndPoint</c>'s beliefs by handshaking that bridge's socket and reading
/// ECHO/INFO/CLUSTER/CONFIG through it, so while those beliefs can come from nowhere else, that bridge
/// must connect - and a bridge that connects is a bridge that exists. These pin each belief as it moves
/// across, so that "the shipped handshake is no longer the source" becomes a thing that is measured
/// rather than hoped for; see design notes D2.8.
/// </para>
/// </remarks>
[RunPerProtocol]
public class RespNewCoreDiscoveryTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    /// <summary>The server-wide settings the client models are learned by asking this core's connection.</summary>
    /// <remarks>
    /// <para>
    /// Both beliefs are <b>cleared first</b>, because the shipped handshake has already run by the time a
    /// test can look - under the engine flag or not. Asserting on the values as found would pass whoever
    /// learned them, which is the one thing this needs to tell apart.
    /// </para>
    /// <para>
    /// <c>Databases</c> is also the gate the production path uses: <c>0</c> is the "nobody has described
    /// this server" value a <c>ServerEndPoint</c> starts at, so clearing it is exactly the state a dial
    /// into an undiscovered server is in.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheHandshakeLearnsTheServersSettings()
    {
        await using var conn = Create(allowAdmin: true, shared: false);
        var core = RespNewCoreFixture.CoreFor(conn);
        var endpoint = conn.GetEndPoints()[0];
        var server = TestMultiplexer.Unwrap(conn).GetServerEndPoint(endpoint, ServerProvenance.Configured)!;

        server.Databases = 0;
        server.ReplicaReadOnly = false;

        var context = new RespDatabaseContext((RespContext)core.ServerContext(endpoint));
        await RespHandshake.DiscoverServerConfigAsync(context, server);

        // 16 on a default install, and the point is that it is the server's answer rather than a guess
        Assert.True(server.Databases > 0, $"databases: {server.Databases}");
        Assert.True(server.ReplicaReadOnly, "replica-read-only");
    }

    /// <summary>A server that has already been described is not asked again.</summary>
    /// <remarks>
    /// The reason the asking is conditional at all: these are server-wide answers, so a second ask learns
    /// nothing and costs two round trips on a dial - which on a large cluster is per node, and is precisely
    /// what the lazy design exists to avoid. The shipped handshake asks on every handshake; this does not.
    /// </remarks>
    [Fact]
    public async Task ADescribedServerIsNotAskedAgain()
    {
        await using var conn = Create(allowAdmin: true, shared: false);
        var core = RespNewCoreFixture.CoreFor(conn);
        var endpoint = conn.GetEndPoints()[0];
        var server = TestMultiplexer.Unwrap(conn).GetServerEndPoint(endpoint, ServerProvenance.Configured)!;

        // a value no server has, so anything that asked would overwrite it
        server.Databases = 4242;
        server.ReplicaReadOnly = false;

        var context = new RespDatabaseContext((RespContext)core.ServerContext(endpoint));
        await RespHandshake.DiscoverServerConfigAsync(context, server);

        Assert.Equal(4242, server.Databases);
        Assert.False(server.ReplicaReadOnly, "nothing was asked, so nothing was learned");
    }
}
