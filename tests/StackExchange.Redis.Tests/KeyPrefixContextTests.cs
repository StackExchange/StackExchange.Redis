using System;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using StackExchange.Redis.Interfaces;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>WithKeyPrefix</c> as context state: the prefix is applied by the frame writer, to every
/// <see cref="RedisKey"/> hole, at render time.
/// </summary>
/// <remarks>
/// <para>
/// <b>These replace four thousand lines of member-by-member wrapper tests</b>
/// (<c>KeyPrefixedTests</c>, <c>KeyPrefixedDatabaseTests</c> and friends), and the reduction is the
/// point rather than a loss: those pinned a hand-written per-member argument mapping, which could be
/// wrong per member and so needed a test per member. The context prefix has no per-member mapping to
/// get wrong - a key reaches the wire only through a <see cref="RedisKey"/>-typed interpolation hole,
/// and the writer prefixes every one of them in one place - so what needs pinning is that one
/// mechanism, plus the enumerable exceptions where a key travels as something other than a key:
/// <c>SORT</c>'s BY/GET patterns (values), <c>RANDOMKEY</c> (no key at all, and an un-mappable reply),
/// and the out-of-band routing questions (<c>IsConnected</c> and friends, which never render).
/// </para>
/// <para>
/// Measured at the executor, which sees the final frame: what is asserted is the RESP the server
/// would receive, not what any intermediate layer promised to do.
/// </para>
/// </remarks>
public class KeyPrefixContextTests
{
    private static IDatabase Target(FakeExecutor executor)
        => new RedisDatabase(new RespDatabaseContext(new RespContext().WithExecutor(executor)), null!, null);

    [Fact]
    public void ASingleKeyIsPrefixedAndValuesAreNot()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var db = Target(executor).WithKeyPrefix("p:");

        db.StringSet("k", "v");

        Assert.Equal("*3|$3|SET|$3|p:k|$1|v|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void EveryKeyInAMultiKeyCommandIsPrefixed()
    {
        var executor = new FakeExecutor(":2\r\n");
        var db = Target(executor).WithKeyPrefix("p:");

        db.KeyDelete(["a", "b"]);

        Assert.Equal("*3|$3|DEL|$3|p:a|$3|p:b|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void HashFieldsAreNotKeysAndAreNotPrefixed()
    {
        var executor = new FakeExecutor(":1\r\n");
        var db = Target(executor).WithKeyPrefix("p:");

        db.HashSet("k", "f", "v");

        Assert.Equal("*4|$4|HSET|$3|p:k|$1|f|$1|v|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void NestedPrefixesComposeOutermostFirst()
    {
        var executor = new FakeExecutor("$1\r\nv\r\n");
        var db = Target(executor).WithKeyPrefix("a:").WithKeyPrefix("b:");

        db.StringGet("k");

        // the same order the nested decorators produced: outer call outermost on the wire
        Assert.Equal("*2|$3|GET|$5|a:b:k|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void AnEmptyPrefixIsTheSameDatabase()
    {
        var db = Target(new FakeExecutor("+OK\r\n"));
        Assert.Same(db, db.WithKeyPrefix(""));
    }

    [Fact]
    public void ANullPrefixIsACallerError()
    {
        var db = Target(new FakeExecutor("+OK\r\n"));
        Assert.Throws<ArgumentNullException>(() => db.WithKeyPrefix((string?)null));
    }

    /// <summary>
    /// A database from outside this library has no context to re-point - and unlike <c>WithRetry</c>,
    /// where "no retry" is the behaviour the caller already had, quietly not prefixing would send every
    /// command to the wrong keyspace. It throws.
    /// </summary>
    [Fact]
    public void AForeignDatabaseIsRefusedRatherThanSilentlyUnprefixed()
    {
        var foreign = Substitute.For<IDatabase>();
        Assert.Throws<NotSupportedException>(() => foreign.WithKeyPrefix("p:"));
    }

    [Fact]
    public async Task KeyRandomRefusesBecauseTheReplyCannotBeMapped()
    {
        var db = Target(new FakeExecutor("$3\r\nkey\r\n")).WithKeyPrefix("p:");

        Assert.Throws<NotSupportedException>(() => db.KeyRandom());
        await Assert.ThrowsAsync<NotSupportedException>(() => db.KeyRandomAsync());
    }

    [Fact]
    public void TheNewSurfaceRefusesRandomTheSameWay()
    {
        var executor = new FakeExecutor("$3\r\nkey\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor)).AppendKeyPrefix("p:");

        Assert.Throws<NotSupportedException>(() => context.Keys.RandomAsync());
        Assert.False(executor.HasSent);
    }

    [Fact]
    public void SortByAndGetPatternsArePrefixedWithTheOperandSpellingsExempt()
    {
        var executor = new FakeExecutor("*1\r\n$1\r\na\r\n");
        var db = Target(executor).WithKeyPrefix("p:");

        // BY names keys via a pattern, so the pattern is prefixed; GET # is an operand and passes through
        db.Sort("k", by: "w_*", get: ["#", "x_*"]);

        Assert.Equal(
            "*8|$4|SORT|$3|p:k|$2|BY|$5|p:w_*|$3|GET|$1|#|$3|GET|$5|p:x_*|",
            Assert.Single(executor.Sent));
    }

    [Fact]
    public void SortByNosortIsAnOperandAndIsNotPrefixed()
    {
        var executor = new FakeExecutor("*1\r\n$1\r\na\r\n");
        var db = Target(executor).WithKeyPrefix("p:");

        db.Sort("k", by: "nosort");

        Assert.Equal("*4|$4|SORT|$3|p:k|$2|BY|$6|nosort|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void SortAndStorePrefixesBothKeys()
    {
        var executor = new FakeExecutor(":0\r\n");
        var db = Target(executor).WithKeyPrefix("p:");

        db.SortAndStore("dest", "k");

        Assert.Equal("*4|$4|SORT|$3|p:k|$5|STORE|$6|p:dest|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void AdHocExecutePrefixesKeyArgumentsAndOnlyThose()
    {
        var executor = new FakeExecutor("$1\r\nv\r\n");
        var db = Target(executor).WithKeyPrefix("p:");

        // a RedisKey goes through the key path (prefixed, routes, invalidates); a string is a value
        db.Execute("GETEX", new object[] { (RedisKey)"k", "PERSIST" });

        Assert.Equal("*3|$5|GETEX|$3|p:k|$7|PERSIST|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void ScriptKeysArePrefixedAndScriptValuesAreNot()
    {
        var executor = new FakeExecutor(":1\r\n") { CaptureKeys = true };
        var db = Target(executor).WithKeyPrefix("p:");

        db.ScriptEvaluate("return 1", [(RedisKey)"k"], ["v"]);

        // asserted via the writer's own key marks, which survive whatever EVAL/EVALSHA shape was
        // chosen (here a SCRIPT LOAD + EVALSHA pair; the key rides on the EVALSHA)
        var frame = executor.Sent[executor.Sent.Count - 1]; // (no Index on net481)
        Assert.Equal("p:k", Assert.Single(executor.Keys));
        Assert.Contains("|p:k|", frame);
        Assert.Contains("|v|", frame);
    }

    /// <summary>
    /// The routing questions never render a frame, so the render-time prefix cannot reach them; they
    /// prefix the key before asking, or in a cluster they would answer about a slot no command uses.
    /// </summary>
    [Fact]
    public void RoutingQuestionsAskAboutThePrefixedKey()
    {
        var executor = new RoutingProbe();
        var db = Target(executor).WithKeyPrefix("p:");

        Assert.True(db.IsConnected("k"));
        Assert.Equal("p:k", executor.LastRoutedKey);

        db.IdentifyEndpoint("k");
        Assert.Equal("p:k", executor.LastRoutedKey);
    }

    [Fact]
    public void APrefixedDatabaseSaysSoInItsFeatures()
    {
        var db = Target(new FakeExecutor("+OK\r\n")).WithKeyPrefix("p:");
        Assert.True(db.GetFeatures(out _).HasFlag(DatabaseFeatureFlags.KeyPrefix));
    }

    [Fact]
    public void ThePrefixAppliesToTheAsyncSurfaceIdentically()
    {
        var executor = new FakeExecutor("$1\r\nv\r\n");
        var db = Target(executor).WithKeyPrefix("p:");

        Assert.True(db.StringGetAsync("k").IsCompleted); // the fake answers inline

        Assert.Equal("*2|$3|GET|$3|p:k|", Assert.Single(executor.Sent));
    }

    private sealed class RoutingProbe() : FakeExecutor("+PONG\r\n")
    {
        public string? LastRoutedKey;

        internal override RespExecutorBase? ResolveFor(in RedisKey key, RedisCommand command, CommandFlags flags)
        {
            LastRoutedKey = key;
            return base.ResolveFor(in key, command, flags);
        }
    }
}
