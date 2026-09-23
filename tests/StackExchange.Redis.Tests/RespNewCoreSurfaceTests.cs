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
    /// <summary>The core behind a multiplexer, for tests that assert about connections rather than commands.</summary>
    internal static RespNewCore CoreFor(IConnectionMultiplexer conn)
        => Cores.GetValue(TestMultiplexer.Unwrap(conn), Create);

    /// <summary>The new core over this multiplexer, as an <see cref="IDatabase"/>.</summary>
    internal static IDatabase Wrap(IConnectionMultiplexer conn, int db, object? asyncState)
    {
        var muxer = TestMultiplexer.Unwrap(conn);

        var core = Cores.GetValue(muxer, Create);
        var wanted = db < 0 ? 0 : db;

        return new TransitionalDatabase(core.GetDatabase(wanted), conn, asyncState, conn.GetDatabase(db, asyncState));
    }

    /// <summary>
    /// One core per multiplexer, closed when that multiplexer closes.
    /// </summary>
    /// <remarks>
    /// <b>The weak key is not what closes the sockets.</b> It lets the core be <i>collected</i>; nothing
    /// about that shuts a connection, and the server goes on seeing it. That is tolerable for an idle
    /// interactive socket and not at all tolerable for a subscription, which carries on receiving - and
    /// carries on being counted by whatever asserts on subscriber counts next. Hooking the multiplexer's
    /// own closing is what actually ends them.
    /// </remarks>
    private static RespNewCore Create(ConnectionMultiplexer muxer)
    {
        var core = new RespNewCore(muxer);
        muxer.Closing += complete =>
        {
            // on the second call, once the multiplexer has finished with its own connections; disposing is
            // idempotent, so a repeat would be harmless anyway
            if (complete) _ = core.DisposeAsync().AsTask();
        };
        return core;
    }

    /// <remarks>
    /// The suites share a connection fixture, so building a core per call would open a socket per command.
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
[Collection(NonParallelCollection.Name)] // see HashImportTests: CONFIG RESETSTAT is server-wide
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

/// <summary>
/// <inheritdoc cref="RespNewCoreFixture"/>
/// </summary>
/// <remarks>
/// <b>The suites that were the gate.</b> Until <c>CreateBatch</c>/<c>CreateTransaction</c> moved off
/// <c>TransitionalDatabase</c>'s fallback these proved nothing - the batch they got back was the shipped
/// <c>RedisBatch</c>, and so was everything they then did. They have to run against the <i>new core</i>
/// rather than the transitional shim, because a batch and a transaction both need a
/// <c>RespConnection</c> to write a contiguous run to and the shim onto the old <c>PhysicalConnection</c>
/// pipeline has none. See design notes 7s.
/// </remarks>
[RunPerProtocol]
public class NewCoreBatchTests(ITestOutputHelper output, SharedConnectionFixture fixture) : BatchTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="NewCoreBatchTests"/>
[RunPerProtocol]
public class NewCoreTransactionTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TransactionTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <summary>
/// <inheritdoc cref="RespNewCoreFixture"/>
/// </summary>
/// <remarks>
/// <b>The suite that proves the last members off the fallback.</b> <c>LockRelease</c>/<c>LockExtend</c>
/// each have three implementations - the atomic <c>IFEQ</c> form, a transaction, and a bare
/// <c>DELETE</c>/<c>EXPIRE</c> where transactions are unavailable - and which one runs depends on the
/// server. Running the existing suite against the new core is what checks the branch this deployment
/// actually takes, rather than the one the author had in mind.
/// </remarks>
[RunPerProtocol]
public class NewCoreLockingTests(ITestOutputHelper output) : LockingTests(output)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);

    /// <inheritdoc/>
    protected override bool SupportsProxy => false;

    /// <inheritdoc/>
    protected override bool CountsMultiplexerOps => false;
}

/// <summary>
/// That the new core reaches the database it was asked for.
/// </summary>
/// <remarks>
/// <b>Not covered by any wrapped suite, and that is the point.</b> Suites that use a dedicated database
/// passed while every command silently went to database 0, because they write and read through the same
/// wrapper and self-consistent wrongness is invisible to a self-consistent test. Only a comparison
/// ACROSS databases - and against the shipped surface - can see it. See design notes 7x.
/// </remarks>
[RunPerProtocol]
public class NewCoreDatabaseRoutingTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task EachDatabaseIsItsOwn()
    {
        await using var conn = Create();
        var db0 = RespNewCoreFixture.Wrap(conn, 0, null);
        var other = TestConfig.GetDedicatedDB(conn);
        if (other == 0) Assert.Skip("needs a database other than 0");
        var dbN = RespNewCoreFixture.Wrap(conn, other, null);

        RedisKey key = Me();
        await db0.KeyDeleteAsync(key);
        await dbN.KeyDeleteAsync(key);

        await db0.StringSetAsync(key, "zero");
        await dbN.StringSetAsync(key, "other");

        Assert.Equal("zero", (string?)await db0.StringGetAsync(key));
        Assert.Equal("other", (string?)await dbN.StringGetAsync(key));

        // and the shipped surface agrees, which is the assertion that actually failed before: the writes
        // have to land where the INDEX says, not merely be self-consistent
        Assert.Equal("zero", (string?)await conn.GetDatabase(0).StringGetAsync(key));
        Assert.Equal("other", (string?)await conn.GetDatabase(other).StringGetAsync(key));
    }
}

#if NET // ScriptingTests itself is NET-only - it flushes and reloads scripts, so it runs in one suite
/// <inheritdoc cref="RespNewCoreFixture"/>
/// <remarks>
/// <b>Wrappable only since the preamble landed, and only once the core was given a script registry.</b>
/// Every EVALSHA needs its SCRIPT LOAD written as a contiguous pair, which the new core could not do until
/// IRespPreambleTarget gave both cores a way to answer a gate; and without a registry it took the branch
/// that had no NOSCRIPT repair, so a SCRIPT FLUSH from a sibling test was fatal rather than recoverable.
/// </remarks>
[Collection(ScriptCacheCollection.Name)] // SCRIPT FLUSH is server-wide; see the collection
[RunPerProtocol]
public class NewCoreScriptingTests(ITestOutputHelper output, SharedConnectionFixture fixture) : ScriptingTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}
#endif

/// <inheritdoc cref="RespNewCoreFixture"/>
/// <remarks>
/// Only the <i>publishing</i> half runs through the new core - subscribing is still the old surface's, and
/// is what these mostly assert against. That is the point: a publish routed by the new core has to reach a
/// subscription registered by the old one.
/// </remarks>
[RunPerProtocol]
public class NewCorePubSubTests(ITestOutputHelper output, SharedConnectionFixture fixture) : PubSubTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="RespNewCoreFixture"/>
/// <remarks>
/// <b>Wrappable only since one connection could serve several databases.</b> This suite is about the
/// database index itself - that GetDatabase(1) and GetDatabase(2) are separate keyspaces, and that a
/// command carrying no database leaves the selection alone - and the new core used to open a connection
/// per (endpoint, database), so SELECT was fixed at the handshake and could never move.
/// </remarks>
[RunPerProtocol]
public class NewCoreDatabaseTests(ITestOutputHelper output, SharedConnectionFixture fixture) : DatabaseTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}

// NOT wrapped yet, and measured rather than guessed: ClusterTests. Wrapping it reports 7 failures out of
// 118, and NONE of them is a missing slot-routing executor - that exists, and MOVED/ASK are handled:
//
//   5  TransactionWith{SameSlot,SameServer,MultiServer}Keys. A transaction is now routed by the slot its
//      queued commands agree on, which is necessary and not sufficient: a trace shows the slot is
//      sometimes NoSlot, because the commands were rendered before this core's topology had settled.
//      FoldSlot is gated on NeedsSlots, and seeding the belief from the multiplexer (below) narrows the
//      window without closing it - the render-time gate has to re-ask rather than read a value captured
//      once. Single commands survive the window: they are corrected with -MOVED, which is also how the
//      topology shakes out. A MULTI/EXEC run cannot be redirected mid-flight, so it aborts instead.
//    2  MovedProfiling - a profile does not follow a command across a redirect.
//
// Wrapping it is the first thing to do once the render-time topology gate is sorted.

/// <inheritdoc cref="RespNewCoreFixture"/>
/// <remarks>
/// <b>The new core could not do TLS at all until now</b> - it opened a bare socket and handed it to a
/// NetworkStream, so every encrypted deployment was out of reach, which is most managed ones. Wrapping
/// the existing TLS suite is the proof that the handshake it now performs is the shipped one: same host
/// resolution, same validation and selection callbacks, same protocols and revocation check.
/// </remarks>
[RunPerProtocol]
public class NewCoreSSLTests(ITestOutputHelper output, SSLTests.SSLServerFixture fixture) : SSLTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => RespNewCoreFixture.Wrap(conn, db, asyncState);
}
