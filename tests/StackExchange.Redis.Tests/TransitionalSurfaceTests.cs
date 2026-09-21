using System;
using System.Linq;
using System.Reflection;
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
        return new TransitionalDatabase(inner.Context, conn, asyncState, inner);
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

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalSortedSetTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : SortedSetTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalListTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : ListTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalHyperLogLogTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : HyperLogLogTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalSortTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : SortTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalGeoTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : GeoTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalVectorSetTests(ITestOutputHelper output)
    : VectorSetIntegrationTests(output)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalStreamTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : StreamTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalBasicOpsTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : BasicOpsTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalKeyTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : KeyTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalExpiryTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : ExpiryTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalScanTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : ScanTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
[Collection(NonParallelCollection.Name)] // see HashImportTests: CONFIG RESETSTAT is server-wide
public class TransitionalHashImportTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : HashImportTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalMultiAddTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : MultiAddTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalSortedSetWhenTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : SortedSetWhenTest(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalLexTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : LexTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalFloatingPointTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : FloatingPointTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalCopyTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : CopyTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalMSetTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : MSetTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalIncrexTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : IncrexIntegrationTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalDigestTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : DigestIntegrationTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalOverloadCompatTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : OverloadCompatTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

// ScriptingTests is deliberately NOT re-run here, and the reason generalises.
//
// TestBase.Me() puts the class name in every key, which is what lets a suite and its transitional twin
// run side by side against one server. The SCRIPT cache is not keyed: it is server-global state, shared
// by SCRIPT LOAD, SCRIPT FLUSH, EVALSHA and NOSCRIPT recovery. Running two copies of ScriptingTests
// concurrently therefore has them flushing each other's scripts, and both copies fail intermittently -
// observed as NOSCRIPT in one class or the other on successive full-suite runs, while passing every time
// the two are run in isolation.
//
// So the rule for adding to this list: a suite can be re-run if its state is key-scoped. Anything that
// mutates server-global state - the script cache, CONFIG, CLIENT settings, the keyspace at large -
// cannot, and needs the moved commands proving some other way.

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalQueuedResultTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : QueuedResultTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalKeyIdleTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : KeyIdleTests(output, fixture)
{
    protected override IDatabase GetDatabase(IConnectionMultiplexer conn, int db = -1, object? asyncState = null)
        => TransitionalSurfaceFixture.Wrap(conn, db, asyncState);
}

/// <inheritdoc cref="TransitionalSurfaceFixture"/>
[RunPerProtocol]
public class TransitionalKeyIdleAsyncTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : KeyIdleAsyncTests(output, fixture)
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
    private static string[] Generated(string prefix, Type iface) => Generated(typeof(TransitionalDatabase), prefix, iface);

    private static string[] Generated(Type implementation, string prefix, Type iface)
    {
        var map = implementation.GetInterfaceMap(iface);
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
    [InlineData("SortedSet")]
    [InlineData("List")]
    [InlineData("HyperLogLog")]
    [InlineData("Sort")]
    [InlineData("Geo")]
    [InlineData("VectorSet")]
    [InlineData("Key")]
    [InlineData("Script")]
    [InlineData("Stream")]
    [InlineData("Array")]
    public void EveryMemberOfAMovedGroupIsImplemented(string prefix)
    {
        var generated = Generated(prefix, typeof(IDatabase)).Concat(Generated(prefix, typeof(IDatabaseAsync)))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        // the ones deliberately left behind, each for a reason that is not "not done yet":
        // - StringGetWithExpiry pipelines TTL+GET, and a composite is not a frame
        // - HashImport references connection-local state and has no self-contained fallback, so its
        //   recovery is two ordered commands on one socket; see the remarks on HashImport, which also
        //   record why EVALSHA - the same shape - escapes this and HIMPORT cannot
        // - the scans are deferred-execution cursors
        var expected = generated
            .Where(x => !x.StartsWith("StringGetWithExpiry", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("HashImport", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("HashScan", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("SetScan", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("SortedSetScan", StringComparison.Ordinal))

            // Key: MIGRATE and RESTORE are not on the new surface at all. The OBJECT family used to be
            // excluded here too, as "a different command shape, deferred as a unit" - it has since moved,
            // so the exclusions are gone and this test now holds it to the same standard as the rest

            // Stream: the scalar half has moved. What is left returns composites that hold arrays -
            // StreamEntry holds NameValueEntry[], RedisStream holds StreamEntry[] - and choosing a
            // lease-shaped representation for those is a design decision, not a transcription. Listed
            // individually rather than as one "Stream*" pass, so that adding a read without its shape
            // decision fails here instead of quietly widening the gap.
            .Where(x => !x.StartsWith("StreamRange", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamRead", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamClaim", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamAutoClaim", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamPending", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamInfo", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamGroupInfo", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamConsumerInfo", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamAdd", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamAcknowledgeAndDelete", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("StreamNegativeAcknowledge", StringComparison.Ordinal))

            // and one composite on the INPUT side: StreamConfigure takes a StreamConfiguration, which is
            // the same "pick a shape" question as the reads, just pointing the other way
            .Where(x => !x.StartsWith("StreamConfigure", StringComparison.Ordinal))

            // Array: ARGREP alone. ArrayGrepRequest is a mutable builder whose predicates render
            // themselves through the OLD MessageWriter, so moving it is a decision about that type rather
            // than a transcription of a command - the same shape of question as StreamConfigure.
            .Where(x => !x.StartsWith("ArrayGrep", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("KeyMigrate", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("KeyRestore", StringComparison.Ordinal))

            // Script: only the Resp-returning forms have moved; see TransitionalDatabase.Scripts.cs for
            // why the RedisResult, LuaScript and hash-addressed overloads are waiting rather than missed
            .Where(x => !x.StartsWith("ScriptEvaluateAsync", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("ScriptEvaluate(", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("ScriptEvaluateReadOnly(", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("ScriptEvaluateReadOnlyAsync", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(expected);
    }

    /// <summary>
    /// Every group that has been moved is actually named above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The second-order control.</b> <c>EveryMemberOfAMovedGroupIsImplemented</c> is honest about the
    /// prefixes it is handed and silent about the ones it is not - so a group could be written, wired, and
    /// never checked, with nothing complaining that nothing was checking. That is exactly what happened to
    /// <c>Key</c> and <c>Script</c>: both surfaces existed, neither was listed, and the suite stayed green.
    /// </para>
    /// <para>
    /// This closes it without a second list to maintain. Every hand-written member must fall under one of
    /// the tested prefixes, so adding a group's adapter without adding its <c>[InlineData]</c> fails here,
    /// naming the members that nothing is covering.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryImplementedMemberBelongsToATestedGroup()
    {
        string[] tested = ["String", "Hash", "Set", "SortedSet", "List", "HyperLogLog", "Sort", "Geo", "VectorSet", "Key", "Script", "Stream", "Array"];

        // the members that belong to no command group: funnels, fallbacks, and the ad-hoc Execute family.
        // A second list, but a STABLE one - infrastructure does not come and go, whereas command groups
        // arrive regularly, and it is the arriving ones this exists to catch.
        string[] infrastructure =
        [
            "CreateBatch", "CreateTransaction", "Execute", "ExecuteAsync", "ExecuteResp", "ExecuteRespAsync",
            "IdentifyEndpoint", "IdentifyEndpointAsync", "IsConnected", "Ping", "PingAsync",
            "Publish", "PublishAsync", "WithKeyPrefix", "DebugObject", "DebugObjectAsync",
            "get_Database",

            // the cursor members are forwarded one at a time from TransitionalDatabase.Scans.cs rather than
            // moved as a group - deferred execution is not a frame - so they have no group to be tested as
            "VectorSetRangeEnumerate", "VectorSetRangeEnumerateAsync",

            // Locks are a PATTERN over other groups rather than a group of their own: LockTake is
            // SET ... NX with an expiry, LockQuery is GET, and LockRelease/LockExtend are one IFEQ-style
            // command where the server has it and a transaction where it does not. There is no "Lock"
            // group to test, so they are covered by NewCoreLockingTests, which runs the existing locking
            // suite against the new core and therefore checks whichever branch this deployment takes.
            "LockTake", "LockTakeAsync", "LockQuery", "LockQueryAsync",
            "LockRelease", "LockReleaseAsync", "LockExtend", "LockExtendAsync",
        ];

        var uncovered = HandWritten(typeof(IDatabase)).Concat(HandWritten(typeof(IDatabaseAsync)))
            .Where(name => !infrastructure.Contains(name, StringComparer.Ordinal))
            .Where(name => !tested.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(uncovered);
    }

    /// <summary>Members the class implements itself; the complement of <see cref="Generated"/>.</summary>
    private static string[] HandWritten(Type iface)
    {
        var map = typeof(TransitionalDatabase).GetInterfaceMap(iface);
        return map.InterfaceMethods
            .Select((m, i) => (Interface: m, Target: map.TargetMethods[i]))
            .Where(x => !x.Target.IsPrivate) // public target: declared by the class, not generated
            .Select(x => x.Interface.Name)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// <b>The milestone, standing where the control used to.</b> Nothing in <see cref="IDatabase"/> or
    /// <see cref="IDatabaseAsync"/> is generated for <see cref="TransitionalDatabase"/> any more: every
    /// member is implemented against the RESP context surface.
    /// </summary>
    /// <remarks>
    /// This slot held the opposite assertion for most of the port - "some group is still generated" - as a
    /// control proving the interface-map query could tell the two kinds of member apart. It named Stream,
    /// then Lock, then Publish, going stale each time by being right. Publish was the last: it is not a
    /// command group, and it routes to the subscribed server rather than by key, so it was the member with
    /// nowhere else to be. With it moved there is nothing left to point at, and an assertion that
    /// something is missing cannot be written truthfully any more.
    /// <para>
    /// The anti-vacuity job it did is not dropped, it moves to
    /// <see cref="TheGeneratedQueryCanStillSeeAGeneratedMember"/> - which is a better control anyway,
    /// because it demonstrates the instrument on a type that is generated <i>by design</i> rather than on
    /// our own unfinished work.
    /// </para>
    /// </remarks>
    [Fact]
    public void NothingIsGeneratedForTheTransitionalDatabaseAnyMore()
    {
        Assert.Empty(Generated(string.Empty, typeof(IDatabase)));

        // ...with one exception, and it is a limit of the instrument rather than a member left behind.
        // IDatabaseAsync.CreateTransaction returns ITransactionAsync, so it CANNOT be served by the public
        // CreateTransaction (which returns ITransaction) and has to be written explicitly - and an explicit
        // implementation is private, exactly like a generated one. The interface map cannot tell those two
        // apart, so this is the single known false positive. It is named rather than filtered out by
        // pattern, so a genuinely generated member appearing here would still fail.
        Assert.Equal(["CreateTransaction(Object)"], Generated(string.Empty, typeof(IDatabaseAsync)));
    }

    /// <summary>
    /// The control for every <c>Assert.Empty</c> above: the query really can see a generated member, so
    /// "nothing is generated" means nothing is generated rather than "the query stopped working".
    /// </summary>
    /// <remarks>
    /// <see cref="Availability.RetryDatabase"/> is <c>[AutoDatabase(Replays = true)]</c> and is generated
    /// on purpose - capture-and-replay wrappers are what the generator is <i>for</i>, and it keeps that
    /// job after the transitional database stops needing it. So this control cannot go stale by being
    /// right, which is how the previous one kept failing.
    /// </remarks>
    [Fact]
    public void TheGeneratedQueryCanStillSeeAGeneratedMember()
        => Assert.NotEmpty(Generated(typeof(Availability.RetryDatabase), "String", typeof(IDatabaseAsync)));
}

// NOT wrapped, and the reason is load-bearing: BatchTests and TransactionTests. Both were tried here and
// both fail wholesale, because TransitionalSurfaceFixture.Wrap builds over RespMessageExecutor - the shim
// onto the OLD PhysicalConnection pipeline - and the new batch and transaction executors need a
// RespConnection to write a contiguous run to. The shim has none, so TrySendBatch/TrySendTransaction
// inherit the declining base and every batch and transaction reports that it cannot be served.
//
// This is a boundary rather than a bug: contiguity IS the semantics for MULTI/EXEC, so falling back to
// sending the frames individually would let another caller interleave and join the transaction - a
// correctness hole dressed as a degradation. The suites become the proof the moment GetDatabase is wired
// through RespNewCore; until then they would be asserting against the shipped implementation anyway.
// (TransactionTests and BatchTests were converted to the virtual GetDatabase(conn) in preparation, which
// costs nothing and is what makes wrapping a one-line subclass when that lands.)
