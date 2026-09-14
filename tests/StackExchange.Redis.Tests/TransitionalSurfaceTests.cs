using System;
using System.Linq;
using System.Reflection;
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

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalHashTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : HashTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalHashFieldTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : HashFieldTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalSetTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : SetTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <summary>
/// That the re-runs above are actually re-running anything.
/// </summary>
/// <remarks>
/// The fallback makes <c>TransitionalDatabase</c> a complete <see cref="IDatabase"/>, which is what lets
/// an existing suite run against it - and is also how a suite could pass without touching the new surface
/// at all, if a command were quietly still being forwarded. So these assert the other half: that the
/// members those suites call are implemented by the class itself rather than generated.
/// <para>
/// <c>[AutoDatabase]</c> emits EXPLICIT interface implementations, so the interface map is what tells the
/// two apart - a hand-written member maps to a public method, a generated one to a private explicit one.
/// Nothing else can see the difference, which is the same reason TransitionalDatabaseTests goes through
/// IDatabase rather than the concrete type.
/// </para>
/// </remarks>
public class TransitionalCoverageTests
{
    private static string[] Generated(string prefix, Type iface)
    {
        var map = typeof(TransitionalDatabase).GetInterfaceMap(iface);
        return map.InterfaceMethods
            .Select((m, i) => (Interface: m, Target: map.TargetMethods[i]))
            .Where(x => x.Interface.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Where(x => x.Target.IsPrivate) // an explicit implementation: generated, so it throws or forwards
            .Select(x => x.Interface.Name + "(" + string.Join(", ", x.Interface.GetParameters().Select(p => p.ParameterType.Name)) + ")")
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Hash")]
    [InlineData("Set")]
    public void EveryMemberOfAMovedGroupIsImplemented(string prefix)
    {
        var generated = Generated(prefix, typeof(IDatabase)).Concat(Generated(prefix, typeof(IDatabaseAsync)))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        // the ones deliberately left behind, each for a reason that is not "not done yet":
        // - StringGetWithExpiry pipelines TTL+GET, and a composite is not a frame
        // - HashImport needs a connection-local PREPARE injected ahead of it
        // - the scans are deferred-execution cursors
        var expected = generated
            .Where(x => !x.StartsWith("StringGetWithExpiry", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("HashImport", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("HashScan", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("SetScan", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(expected);
    }

    [Fact]
    public void AnUnmovedGroupIsStillGenerated()
    {
        // the control. Without this, EveryMemberOfAMovedGroupIsImplemented would pass just as happily if
        // the interface map stopped distinguishing the two kinds of member, and the coverage claim above
        // would be vacuous rather than wrong - which is the harder failure to notice.
        Assert.NotEmpty(Generated("SortedSet", typeof(IDatabase)));
    }
}
