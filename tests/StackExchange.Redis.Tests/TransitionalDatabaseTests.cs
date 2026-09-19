using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using NSubstitute;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>TransitionalDatabase</c>: the old <see cref="IDatabase"/> surface over the new context surface, one
/// command at a time.
/// </summary>
public class TransitionalDatabaseTests
{
    private sealed class FakeExecutor(params string[] replies) : RespExecutorBase
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public override int Database => 0;

        public override RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    // the multiplexer is only reached to apply a timeout when a send does NOT complete synchronously;
    // the fake always does, so passing null here also pins that the fast path really is the fast path
    private static IDatabase Target(FakeExecutor executor)
        => new TransitionalDatabase(new RespDatabaseContext(new RespContext().WithExecutor(executor)), null!, null);

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

    [Fact]
    public void AnUnmovedCommandThrowsAndSaysSo()
    {
        var db = Target(new FakeExecutor("+OK\r\n"));

        // the exemplar has to be a command that genuinely has not moved, so it changes as groups land -
        // KeyDelete was this until the Key group arrived, ListLeftPush until the List group did,
        // StreamLength until the stream scalars did, and ArrayLength lasted about an hour. Going stale is
        // the point: it fails here loudly rather than silently asserting nothing.
        //
        // LockQuery was the previous pick, on the reasoning that "the lock group waits on transactions".
        // Half of that group did not: LockQuery is GET and LockTake is SET NX, so they moved as sugar over
        // the String group. LockRelease genuinely does wait - it is one IFEQ-style message on a new enough
        // server and a TRANSACTION otherwise - so it should outlast most of what is left.
        var ex = Assert.Throws<NotImplementedException>(() => db.LockRelease("k", "token"));
        Assert.Contains("has not yet moved", ex.Message);
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
