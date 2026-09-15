using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The context surface reached from <see cref="IServer"/>, which is where server-scoped groups will hang.
/// </summary>
/// <remarks>
/// <para>
/// Handover notes for whoever writes the first server group: bind it to <see cref="IRespServerTarget"/>,
/// not <see cref="IRespTarget"/> - the latter would put it back on <c>ISubscriber</c> too, which
/// <c>RespTargetSplitTests</c> will fail you for. The keyspace groups are deliberately not reachable here.
/// </para>
/// <para>
/// The context carries <b>no database</b>, so a database-scoped command fails at construction rather than
/// running against database 0; <c>IServer</c>'s own members take the number explicitly, and a server group
/// should do the same.
/// </para>
/// </remarks>
public class RespServerContextTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task AServerContextReachesThatServer()
    {
        await using var conn = Create(allowAdmin: true);
        var server = conn.GetServer(conn.GetEndPoints()[0]);

        using var reply = await server.Context.ExecuteAsync("PING", default);
        Assert.Equal("PONG", reply.ReadScalar().ReadString());
    }

    /// <summary>
    /// Each server's context is pinned to its own endpoint, which is the entire point of having one.
    /// </summary>
    /// <remarks>
    /// Asserted by asking two different servers who they are: a context that fell back to ordinary
    /// selection would answer from whichever server the multiplexer liked, and would pass a single-server
    /// test while being completely wrong.
    /// </remarks>
    [Fact]
    public async Task EachServerContextIsPinnedToItsOwnEndpoint()
    {
        await using var conn = Create(allowAdmin: true);
        var endpoints = conn.GetEndPoints();
        Assert.SkipWhen(endpoints.Length < 2, "needs the primary/replica pair");

        foreach (var endpoint in endpoints)
        {
            var server = conn.GetServer(endpoint);
            if (!server.IsConnected) continue;

            using var reply = await server.Context.ExecuteAsync(
                "CONFIG", new[] { RedisKeyOrValue.FromValue("GET"), RedisKeyOrValue.FromValue("port") });

            var reader = reply.Read();
            reader.MoveNext();          // the map/array wrapper
            reader.MoveNext();          // "port"
            reader.MoveNext();          // the value
            var port = reader.ReadString();

            Assert.Equal(((System.Net.IPEndPoint)endpoint).Port.ToString(), port);
        }
    }

    /// <summary>A database-scoped command through a server context fails loudly rather than assuming db 0.</summary>
    [Fact]
    public async Task ADatabaseScopedCommandIsRefused()
    {
        await using var conn = Create(allowAdmin: true);
        var server = conn.GetServer(conn.GetEndPoints()[0]);

        var ex = await Assert.ThrowsAnyAsync<System.Exception>(
            async () => (await server.Context.ExecuteAsync("GET", new[] { RedisKeyOrValue.FromKey(Me()) })).Dispose());

        Assert.Contains("database", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}
