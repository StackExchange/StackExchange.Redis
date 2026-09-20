using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Runs existing suites against the <b>new core</b> - operation, connection, transport, handshake,
/// endpoint and multiplexer executors, redirects - with no <c>Message</c> anywhere in the send path.
/// </summary>
/// <remarks>
/// <para>
/// <c>TransitionalSurfaceFixture</c> proved the context <i>surface</i> by re-running these suites over
/// the old pipeline. This proves the <i>core</i>, by re-running them over the new one. The value is the
/// same and the argument is the same: assertions written for the shipped library know far more about what
/// it must do than anything written alongside a spike.
/// </para>
/// <para>
/// Topology and configuration still come from <c>ConnectionMultiplexer</c> - which endpoints exist, which
/// node owns a slot, what the credentials are. Only <i>sending</i> is new. Those are the two halves worth
/// separating: re-implementing topology discovery to prove a point about the send path would be the wrong
/// experiment.
/// </para>
/// </remarks>
public abstract class RespNewCoreFixture
{
    /// <summary>The new core over this multiplexer, as an <see cref="IDatabase"/>.</summary>
    internal static IDatabase Wrap(IConnectionMultiplexer conn, int db, object? asyncState)
    {
        // the shared fixture hands out a NonDisposingConnection wrapper, so unwrap to the real thing
        var muxer = conn switch
        {
            ConnectionMultiplexer direct => direct,
            SharedConnectionFixture.NonDisposingConnection wrapper => (ConnectionMultiplexer)wrapper.UnderlyingConnection,
            _ => throw new InvalidOperationException($"cannot reach a multiplexer through {conn.GetType().Name}"),
        };

        var core = Cores.GetValue(muxer, static m => new RespNewCore(m));
        return new TransitionalDatabase(core.GetDatabase(db < 0 ? 0 : db), conn, asyncState, conn.GetDatabase(db, asyncState));
    }

    /// <remarks>
    /// One core per multiplexer, keyed weakly so a disposed connection takes its sockets with it. The
    /// suites share a connection fixture, so building a core per call would open a socket per command.
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConnectionMultiplexer, RespNewCore> Cores = new();
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreStringTests(ITestOutputHelper output, SharedConnectionFixture fixture) : StringTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreHashTests(ITestOutputHelper output, SharedConnectionFixture fixture) : HashTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreListTests(ITestOutputHelper output, SharedConnectionFixture fixture) : ListTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreSetTests(ITestOutputHelper output, SharedConnectionFixture fixture) : SetTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreSortedSetTests(ITestOutputHelper output, SharedConnectionFixture fixture) : SortedSetTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreKeyTests(ITestOutputHelper output, SharedConnectionFixture fixture) : KeyTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreStreamTests(ITestOutputHelper output, SharedConnectionFixture fixture) : StreamTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreExpiryTests(ITestOutputHelper output, SharedConnectionFixture fixture) : ExpiryTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreBasicOpsTests(ITestOutputHelper output, SharedConnectionFixture fixture) : BasicOpsTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreGeoTests(ITestOutputHelper output, SharedConnectionFixture fixture) : GeoTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreBitTests(ITestOutputHelper output, SharedConnectionFixture fixture) : BitTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

// BitTests is back, and it is the check that profiling landed: BitFieldAllGetGoesOutAsReadOnlyAndReachesAReplica
// asserts through a ProfilingSession which command went to which endpoint, so it fails flat against a
// core that feeds no profiling. It was excluded for exactly as long as that was true.

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreHyperLogLogTests(ITestOutputHelper output, SharedConnectionFixture fixture) : HyperLogLogTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreScanTests(ITestOutputHelper output, SharedConnectionFixture fixture) : ScanTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreHashFieldTests(ITestOutputHelper output, SharedConnectionFixture fixture) : HashFieldTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreSortTests(ITestOutputHelper output, SharedConnectionFixture fixture) : SortTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreLexTests(ITestOutputHelper output, SharedConnectionFixture fixture) : LexTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreMultiAddTests(ITestOutputHelper output, SharedConnectionFixture fixture) : MultiAddTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreCopyTests(ITestOutputHelper output, SharedConnectionFixture fixture) : CopyTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreMSetTests(ITestOutputHelper output, SharedConnectionFixture fixture) : MSetTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreFloatingPointTests(ITestOutputHelper output, SharedConnectionFixture fixture) : FloatingPointTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreSortedSetWhenTests(ITestOutputHelper output, SharedConnectionFixture fixture) : SortedSetWhenTest(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreKeyIdleTests(ITestOutputHelper output, SharedConnectionFixture fixture) : KeyIdleTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreIncrexTests(ITestOutputHelper output, SharedConnectionFixture fixture) : IncrexIntegrationTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreDigestTests(ITestOutputHelper output, SharedConnectionFixture fixture) : DigestIntegrationTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreOverloadCompatTests(ITestOutputHelper output, SharedConnectionFixture fixture) : OverloadCompatTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
[RunPerProtocol]
public class NewCoreHashImportTests(ITestOutputHelper output, SharedConnectionFixture fixture) : HashImportTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
/// <remarks>
/// The suite that exists to exercise profiling, run against the core that now feeds it. Anything it
/// asserts about timings, ordering or which endpoint answered is an assertion about the new operation's
/// lifecycle hooks rather than about <c>Message</c>.
/// </remarks>
[RunPerProtocol]
public class NewCoreProfilingTests(ITestOutputHelper output) : ProfilingTests(output)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}
