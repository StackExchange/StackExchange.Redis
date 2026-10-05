using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using NSubstitute;
using StackExchange.Redis.KeyspaceIsolation;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>TransitionalDatabase</c>: the old <see cref="IDatabase"/> surface over the new context surface, one
/// command at a time.
/// </summary>
public class TransitionalDatabaseTests
{
    // the multiplexer is only reached to apply a timeout when a send does NOT complete synchronously;
    // the fake always does, so passing null here also pins that the fast path really is the fast path
    private static IDatabase Target(FakeExecutor executor)
        => new TransitionalDatabase(new RespDatabaseContext(new RespContext().WithExecutor(executor)), null!, null);

    [Fact]
    public async Task ARefusedConditionNamesTheMemberTheCallerActuallyCalled()
    {
        // The rule lives once, in the context surface's switch; the NAME does not, because the two
        // surfaces spell the same operation differently. Being told that "DeleteAsync" cannot be used
        // when you called StringDeleteAsync is a poor error, and the alternative - a second copy of the
        // switch in the adapter - would be free to drift from the real one.
        var db = Target(new FakeExecutor(":1\r\n"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await db.StringDeleteAsync("k", ValueCondition.NotExists));
        Assert.StartsWith("StringDeleteAsync cannot be used with a NotExists condition", ex.Message);

        var sync = Assert.Throws<InvalidOperationException>(
            () => db.StringDelete("k", ValueCondition.NotExists));
        Assert.StartsWith("StringDelete cannot be used with a NotExists condition", sync.Message);
    }

    [Fact]
    public async Task KeyDeleteUpgradesToUnlinkWhereTheServerHasIt()
    {
        // IDatabase.KeyDelete has ALWAYS auto-upgraded to UNLINK, and nothing in the integration suite
        // can see the difference: the reply is the same integer either way, and what changes is whether
        // the server blocks while freeing a large key. So it is pinned here or not at all - which is how
        // it came to be missing from the context surface in the first place.
        var modern = new FakeExecutor(":1\r\n") { Features = new RedisFeatures(new Version(7, 0)) };
        await Target(modern).KeyDeleteAsync("k");
        Assert.Equal("*2|$6|UNLINK|$1|k|", Assert.Single(modern.Sent));

        var ancient = new FakeExecutor(":1\r\n") { Features = new RedisFeatures(new Version(3, 2)) };
        await Target(ancient).KeyDeleteAsync("k");
        Assert.Equal("*2|$3|DEL|$1|k|", Assert.Single(ancient.Sent));

        // unknown answers DEL, and the asymmetry with the expiry fallback is deliberate: falling back
        // from UNLINK to DEL costs only latency, while guessing wrong is a hard error on an old server.
        // Falling back from PEXPIRE to EXPIRE loses precision silently, so THAT one guesses modern.
        var unknown = new FakeExecutor(":1\r\n");
        await Target(unknown).KeyDeleteAsync("k");
        Assert.Equal("*2|$3|DEL|$1|k|", Assert.Single(unknown.Sent));
    }

    /// <summary>An executor that answers the routing question, and records that it was asked.</summary>
    private sealed class RoutingExecutor(bool connected, params string[] replies) : FakeExecutor(replies)
    {
        internal int Asked;

        internal CommandFlags LastFlags;

        internal string? LastKey;

        internal override bool IsReachable(in RedisKey key, CommandFlags flags)
        {
            Asked++;
            LastFlags = flags;
            LastKey = key.ToString();
            return connected;
        }
    }

    [Fact]
    public void IsConnectedAsksTheExecutorRatherThanTheFallback()
    {
        // it came off the fallback by becoming a routing question: the executor IS the router, so this
        // sends nothing and asks what routing WOULD do with the key. Note the null fallback - reaching
        // for it would throw, which is the assertion.
        var executor = new RoutingExecutor(connected: true);
        var db = Target(executor);

        Assert.True(db.IsConnected("user:1", CommandFlags.PreferReplica));

        Assert.Equal(1, executor.Asked);
        Assert.Equal("user:1", executor.LastKey);
        Assert.Equal(CommandFlags.PreferReplica, executor.LastFlags); // flags steer to a replica, so they travel
        Assert.False(executor.HasSent);                               // and nothing went to the wire
    }

    [Fact]
    public void IsConnectedReportsWhatTheRouterSays()
    {
        Assert.False(Target(new RoutingExecutor(connected: false)).IsConnected("user:1"));
        Assert.True(Target(new RoutingExecutor(connected: true)).IsConnected("user:1"));
    }

    [Fact]
    public void AnExecutorWithNoRoutingIsAlwaysConnected()
    {
        // the default: a fake, or a stream over one socket, can always reach the only server it has.
        // Saying "disconnected" instead would be the more damaging wrong answer.
        Assert.True(Target(new FakeExecutor()).IsConnected("user:1"));
    }

    [Fact]
    public async Task AnExecutorWithNoEndpointsAdmitsItRatherThanGuessing()
    {
        // the default is null - "no idea" - because an executor with no notion of endpoints has no honest
        // answer, and inventing one would be worse than admitting it
        var db = Target(new FakeExecutor());
        Assert.Null(await db.IdentifyEndpointAsync("user:1"));
        Assert.Null(db.IdentifyEndpoint("user:1"));
    }

    [Fact]
    public void AMovedCommandReachesTheContextSurface()
    {
        // THE test for the generator change. Both a hand-written public member and a generated EXPLICIT
        // one compile happily side by side, and interface dispatch silently prefers the generated throw -
        // so nothing here can be caught at build time. Going through IDatabase is what proves the
        // generator stood back.
        var executor = new FakeExecutor("$4\r\nmarc\r\n");
        var db = Target(executor);

        Assert.Equal("marc", (string?)db.StringGet("user:1"));
        Assert.Equal("*2|$3|GET|$6|user:1|", Assert.Single(executor.Sent));
    }

    [Fact]
    public async Task AMovedCommandWorksAsynchronouslyToo()
    {
        var executor = new FakeExecutor("$4\r\nmarc\r\n");
        var db = Target(executor);

        Assert.Equal("marc", (string?)await db.StringGetAsync("user:1"));
    }

    [Fact]
    public void TheFullSetOverloadRendersItsOptionalArguments()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var db = Target(executor);

        Assert.True(db.StringSet("k", "v", TimeSpan.FromSeconds(300), when: When.NotExists));
        Assert.Equal("*6|$3|SET|$1|k|$1|v|$2|NX|$2|EX|$3|300|", Assert.Single(executor.Sent));
    }

    /// <summary>
    /// <b>Where the "an unmoved command throws" exemplar used to be.</b> There is no longer a command to
    /// point at: every member of <see cref="IDatabase"/>/<see cref="IDatabaseAsync"/> is implemented
    /// against the RESP context surface, so nothing reaches the generated throw.
    /// </summary>
    /// <remarks>
    /// The exemplar went stale seven times by being right - KeyDelete, ListLeftPush, StreamLength,
    /// ArrayLength, LockQuery, LockRelease, StringGetWithExpiry - and finally Publish, which the comment
    /// here called the last one and said would retire this test when it moved. It has, so it does.
    /// <see cref="TransitionalCoverageTests.NothingIsGeneratedForTheTransitionalDatabaseAnyMore"/> is what
    /// asserts the state this used to sample, and it asserts it over the whole interface rather than one
    /// hand-picked member.
    /// </remarks>
    /// <summary>
    /// <b>The key prefix is applied exactly once, and the same way, whichever surface you hold.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// There are two prefixing mechanisms now, and they overlap: <c>KeyPrefixedDatabase</c> wraps an
    /// <see cref="IDatabase"/> and rewrites every <see cref="RedisKey"/> as it forwards, while
    /// <see cref="RespContext"/> carries a prefix the frame writer applies. A caller who reaches the
    /// context <i>through</i> a wrapper touches both, which is the shape a double prefix would take - and
    /// a double prefix is silent: every read and write agrees with every other, against the wrong keys.
    /// </para>
    /// <para>
    /// They do compose correctly, and this pins why rather than merely that. The wrapper's context is its
    /// <i>inner</i> context plus the prefix, not its own - so the prefix is contributed once, by whichever
    /// mechanism is actually in the path, and never by both.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheKeyPrefixIsAppliedOnceWhicheverSurfaceIsHeld()
    {
        var executor = new FakeExecutor("$1\r\nv\r\n", "$1\r\nv\r\n");
        var prefixed = Target(executor).WithKeyPrefix("p:");

        // the shipped interface, forwarded through the wrapper
        prefixed.StringGet("k");

        // the new surface, reached through the same wrapper
        await new RespDatabaseContext(((IRespTarget)prefixed).Context).Strings.GetAsync("k");

        Assert.Equal(["*2|$3|GET|$3|p:k|", "*2|$3|GET|$3|p:k|"], executor.Sent);
    }

    /// <summary>
    /// <b>Both prefixes apply to a channel, and in that order</b> - the connection's channel prefix
    /// outermost, the database's key prefix inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This looks wrong at first sight and is not, which is why it is pinned. A key prefix is keyspace
    /// <i>isolation</i>, and the shipped wrapper has always isolated channels along with keys -
    /// <c>KeyPrefixed.PublishAsync</c> forwards <c>ToInner(channel)</c> - so an isolated database
    /// publishes to an isolated channel. The connection-wide channel prefix is then prepended by the
    /// writer, as it is for every channel. A caller with both configured sees <c>c:p:ch</c>, and both
    /// surfaces have to agree on that or a publish and its subscription part company.
    /// </para>
    /// <para>
    /// The quiet failure this guards is the channel prefix going missing: it rides on the context's
    /// services, so a <c>With*</c> that rebuilt the context without carrying them would publish to the
    /// unprefixed channel while subscribers waited on the prefixed one - with no error anywhere.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothPrefixesApplyToAChannelChannelPrefixOutermost()
    {
        var executor = new FakeExecutor(":0\r\n", "$1\r\nv\r\n");
        IDatabase db = new TransitionalDatabase(
            new RespDatabaseContext(new RespContext().WithExecutor(executor).AppendChannelPrefix(RedisChannel.Literal("c:"))),
            null!,
            null);

        var prefixed = db.WithKeyPrefix("p:");
        prefixed.Publish(RedisChannel.Literal("ch"), "msg");
        prefixed.StringGet("k");

        // the channel took BOTH, channel prefix outermost; the key took only the key prefix
        Assert.Equal(["*3|$7|PUBLISH|$6|c:p:ch|$3|msg|", "*2|$3|GET|$3|p:k|"], executor.Sent);
    }

    [Fact]
    public void NoCommandIsLeftForTheGeneratedThrow()
    {
        var executor = new FakeExecutor(":2\r\n");
        var db = Target(executor);

        // the member that was the exemplar, now rendering a frame and going to the wire like any other
        Assert.Equal(2, db.Publish(RedisChannel.Literal("ch"), "msg"));
        Assert.Equal("*3|$7|PUBLISH|$2|ch|$3|msg|", Assert.Single(executor.Sent));
    }

    /// <summary>
    /// <c>HIMPORT</c> is a pair: a <c>PREPARE</c> that declares the field-set, then the <c>SET</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gate is not exercised here, and cannot be.</b> It answers "has THIS connection prepared
    /// this field-set?", and a fake executor has no connection - so this takes
    /// <c>RespExecutor.AwaitPair</c>'s sequential fallback, which sends both and consults nothing. What
    /// that still pins is the part a fake can see: two frames, in that order, with the same opaque
    /// field-set name in both. The name is what ties a <c>PREPARE</c> to its <c>SET</c>s, so a rendering
    /// that disagreed about it would produce a server error on every import.
    /// </para>
    /// <para>
    /// The name is the field-set's id blitted to eight bytes, so it differs per field-set and cannot be
    /// asserted literally; that both frames carry the same one is the property that matters.
    /// </para>
    /// </remarks>
    [Fact]
    public void HashImportSendsThePrepareAndThenTheSet()
    {
        using var fieldSet = HashImport.Create("f1", "f2");
        var executor = new FakeExecutor("+OK\r\n");
        var db = Target(executor);

        db.HashImport("k", fieldSet, new RedisValue[] { "v1", "v2" });

        Assert.Equal(2, executor.Sent.Count);
        var prepare = executor.Sent[0];
        var set = executor.Sent[1];

        Assert.StartsWith("*5|$7|HIMPORT|$7|PREPARE|$8|", prepare);
        Assert.EndsWith("|$2|f1|$2|f2|", prepare);

        Assert.StartsWith("*6|$7|HIMPORT|$3|SET|$1|k|$8|", set);
        Assert.EndsWith("|$2|v1|$2|v2|", set);

        // the same eight-byte name in both, which is the whole of what makes them a pair
        Assert.Equal(NameOf(prepare, "$7|PREPARE|$8|"), NameOf(set, "$1|k|$8|"));

        // the id is a counter blitted to eight bytes, so its upper bytes are zero and none of them is a
        // '|' - which is what lets this split on the separator the executor substituted for CRLF
        static string NameOf(string frame, string after)
        {
            var start = frame.IndexOf(after, StringComparison.Ordinal) + after.Length;
            return frame.Substring(start, frame.IndexOf('|', start) - start);
        }
    }

    /// <summary>The counts have to line up, and saying so beats a server error.</summary>
    [Fact]
    public void HashImportDemandsAValuePerField()
    {
        using var fieldSet = HashImport.Create("f1", "f2");
        var db = Target(new FakeExecutor("+OK\r\n"));

        Assert.Throws<ArgumentException>(() => db.HashImport("k", fieldSet, new RedisValue[] { "v1" }));
    }

    /// <summary>A disposed field-set may already have been discarded on the server.</summary>
    [Fact]
    public void HashImportRefusesADisposedFieldSet()
    {
        var fieldSet = HashImport.Create("f1");
        fieldSet.Dispose();
        var db = Target(new FakeExecutor("+OK\r\n"));

        Assert.Throws<ObjectDisposedException>(() => db.HashImport("k", fieldSet, new RedisValue[] { "v1" }));
    }

    /// <summary>The two lock members that moved are the commands they always were.</summary>
    /// <remarks>
    /// They are sugar rather than a group - <c>LockTake</c> is <c>SET ... NX</c> with an expiry and
    /// <c>LockQuery</c> is <c>GET</c> - so there is no lock group to test them as, and this is where the
    /// composition is pinned instead. The null-token guard is here too, because without it <c>SET</c>
    /// would <i>delete</i> the key and the lock would read as taken-then-vanished.
    /// </remarks>
    [Fact]
    public void TheMovedLockMembersAreSugarOverSetAndGet()
    {
        var executor = new FakeExecutor("+OK\r\n", "$5\r\ntoken\r\n");
        var db = Target(executor);

        Assert.True(db.LockTake("k", "token", TimeSpan.FromSeconds(30)));
        Assert.Equal("token", db.LockQuery("k"));

        Assert.Equal("*6|$3|SET|$1|k|$5|token|$2|NX|$2|EX|$2|30|", executor.Sent[0]);
        Assert.Equal("*2|$3|GET|$1|k|", executor.Sent[1]);

        Assert.Throws<ArgumentNullException>(() => db.LockTake("k", RedisValue.Null, TimeSpan.FromSeconds(30)));
    }

    /// <summary>A ValueTask source that records whether its result was consumed.</summary>
    private sealed class ConsumptionProbe : IValueTaskSource
    {
        public int GetResultCalls { get; private set; }

        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => continuation(state);

        public void GetResult(short token) => GetResultCalls++;
    }

    [Fact]
    public void AResultLessWaitStillConsumesTheValueTask()
    {
        // The rule that is easy to lose: a ValueTask backed by an IValueTaskSource must have its result
        // consumed exactly once, because GetResult(token) is what lets the source complete its lifecycle
        // and be reset or pooled. With no value to take, the call looks droppable - so this is here to
        // fail if someone drops it. Abandoning the source leaks it and can hand a stale token to whoever
        // borrows it next, which is a bug that would surface nowhere near this code.
        var probe = new ConsumptionProbe();
        var db = (TransitionalDatabase)Target(new FakeExecutor("+OK\r\n"));

        var wait = typeof(TransitionalDatabase).GetMethod(
            "Wait",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ValueTask)],
            modifiers: null);
        Assert.NotNull(wait);

        wait!.Invoke(db, [new ValueTask(probe, token: 0)]);

        Assert.Equal(1, probe.GetResultCalls);
    }

    [Fact]
    public void AsDatabaseCompletesTheRoundTrip()
    {
        // IDatabase.Context already goes new <- legacy; this is the other direction, so neither surface is
        // a one-way door. Note the concrete type stays internal - the contract is IDatabase.
        var executor = new FakeExecutor("$4\r\nmarc\r\n");
        var surface = new RespDatabaseContext(new RespContext().WithExecutor(executor));

        IDatabase legacy = surface.AsDatabase(Substitute.For<IConnectionMultiplexer>());

        Assert.Equal("marc", (string?)legacy.StringGet("user:1"));
        Assert.Equal("*2|$3|GET|$6|user:1|", Assert.Single(executor.Sent));

        // and back again, to the same context
        Assert.Same(executor, legacy.Context.Raw.Executor);
    }

    [Fact]
    public void TheWholeInterfaceIsImplemented()
    {
        // the point of the generator: this type satisfies IDatabase in full without anyone listing it
        Assert.True(typeof(IDatabase).IsAssignableFrom(typeof(TransitionalDatabase)));
    }
}
