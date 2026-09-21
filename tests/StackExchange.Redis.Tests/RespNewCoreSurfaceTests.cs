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
        var muxer = TestMultiplexer.Unwrap(conn);

        var core = Cores.GetValue(muxer, static m => new RespNewCore(m));
        var wanted = db < 0 ? 0 : db;

        return new TransitionalDatabase(core.GetDatabase(wanted), conn, asyncState, conn.GetDatabase(db, asyncState));
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

// NOT wrapped yet, and the gap is measured rather than guessed: ScriptingTests. Converting it to the
// virtual GetDatabase (done here) makes wrapping a one-line subclass, and doing that temporarily reports
// 18 failures of three kinds:
//
//   14  NOSCRIPT No matching script. The new core has no NOSCRIPT recovery at all - every bit of it lives
//       in ResultProcessor/Message (see ResultProcessor.RespResult.cs), so an EVALSHA whose script the
//       server has forgotten is simply an error. The preamble gate avoids this in the common case, but a
//       SCRIPT FLUSH from a sibling test invalidates a belief the gate still holds. This is the next piece
//       of work for scripts, and it is frame-ownership sensitive: the retry must re-issue as EVAL without
//       handing a pooled buffer back twice.
//    2  ChangeDbInTranScript - the SELECT/connection-per-database gap, not a script problem.
//    2  exception type mismatches, unexamined.
//
// Wrapping it is the first thing to do once NOSCRIPT recovery lands.

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
