using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Runs an existing suite unchanged against <c>TransitionalDatabase</c>, so that every command the new
/// RESP context surface has taken over is checked by the assertions that were written for the old one.
/// </summary>
/// <remarks>
/// <para>
/// This is the proof the spike actually needs. A hand-written test for a moved command asserts what its
/// author believed the command does; re-running <c>StringTests</c> asserts what the library has always
/// claimed it does, against real servers, across every protocol the suite already covers - and it keeps
/// asserting it as commands keep moving, with no new test to write.
/// </para>
/// <para>
/// The database is a <c>TransitionalDatabase</c> with the ordinary one behind it: moved commands go
/// through the interpolated writer and the new handlers, and anything still unmoved - <c>KeyDelete</c>,
/// <c>KeyExpire</c>, <c>Execute</c>, the scans - forwards to the old implementation. So a failure here is
/// a failure of something that HAS moved, which is exactly the signal wanted. As more groups move, the
/// fallback carries less and the proof gets stronger on its own.
/// </para>
/// <para>
/// Note <c>TestBase.Me()</c> includes the test class name, so these runs use different keys from the base
/// class's and the two can run concurrently.
/// </para>
/// </remarks>
public abstract class TransitionalSurfaceFixture
{
    /// <summary>
    /// The context surface as an <see cref="IDatabase"/>, with the ordinary database as the fallback for
    /// commands that have not moved yet.
    /// </summary>
    internal static IDatabase Wrap(IConnectionMultiplexer conn, int db, object? asyncState)
    {
        var inner = conn.GetDatabase(db, asyncState);

        // RedisDatabase.Context is already wired to a live executor (RespMessageExecutor), so the new
        // surface reaches the same connection, the same backlog and the same multiplexing as the old one;
        // the only thing that differs is how the bytes were produced and how the reply was read
        return new TransitionalDatabase(new RespDatabase(inner.Context), conn, asyncState, inner);
    }
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalStringTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : StringTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalBitTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : BitTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}
