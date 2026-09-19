using System;
using System.Collections.Generic;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Caching;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The group executor: send to whichever member is active, asked at the right moment. Design notes 3b.
/// </summary>
public class RespGroupExecutorTests
{
    private sealed class Member(string name, params string[] replies) : FakeExecutor(replies)
    {
        internal string Name => name;
    }

    private sealed class Group
    {
        internal Member? Active;

        internal int Resolutions;

        internal RespExecutorBase? Resolve()
        {
            Resolutions++;
            return Active;
        }
    }

    private static (RespDatabaseContext Context, Group Group, RespGroupExecutor Executor) Build()
    {
        var group = new Group { Active = new Member("a", "$1\r\nA\r\n") };
        var executor = new RespGroupExecutor(group.Resolve, describeUnavailable: () => "2 of 2 members unavailable.");
        return (new RespDatabaseContext(new RespContext().WithExecutor(executor)), group, executor);
    }

    [Fact]
    public async Task CommandsGoToTheActiveMember()
    {
        var (context, group, _) = Build();

        Assert.Equal("A", (string?)await context.Strings.GetAsync("k"));
        Assert.True(group.Active!.HasSent);
    }

    [Fact]
    public async Task AFailoverRedirectsSubsequentCommandsWithoutRebuildingAnything()
    {
        // the whole point of a group: the active member changes, and a context memoised for the life of
        // the multiplexer has to follow it. An executor that resolved once would pin itself to whoever
        // happened to be active when somebody first called GetDatabase().
        var (context, group, _) = Build();
        var first = group.Active!;

        await context.Strings.GetAsync("k");
        Assert.True(first.HasSent);

        var second = new Member("b", "$1\r\nB\r\n");
        group.Active = second;

        Assert.Equal("B", (string?)await context.Strings.GetAsync("k"));
        Assert.Equal(1, first.Sends);   // the old member saw exactly its own command, and no more
        Assert.True(second.HasSent);
    }

    [Fact]
    public async Task TheActiveMemberIsResolvedPerCommand()
    {
        var (context, group, _) = Build();
        var before = group.Resolutions;

        await context.Strings.GetAsync("a");
        await context.Strings.GetAsync("b");

        Assert.Equal(before + 2, group.Resolutions);
    }

    [Fact]
    public async Task AFullyDownGroupThrowsAConnectionExceptionThatSaysWhy()
    {
        // a RedisConnectionException rather than InvalidOperationException, matching the shipped group
        // facade: a caller catching the former should not have to know which kind it was handed (#3223)
        var (context, group, _) = Build();
        group.Active = null;

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () => await context.Strings.GetAsync("k"));
        Assert.Contains("2 of 2 members unavailable.", ex.Message);
        Assert.Equal(CommandStatus.WaitingToBeSent, ex.CommandStatus); // never sent, so retry may re-issue
    }

    [Fact]
    public void RoutingQuestionsAnswerRatherThanThrowWhenNothingIsActive()
    {
        // "can you reach this key?" has a perfectly good answer when the group is down, and it is no
        var (_, group, executor) = Build();
        group.Active = null;

        Assert.False(executor.IsConnected("k", CommandFlags.None));
        Assert.False(executor.HasActiveMember);
    }

    [Fact]
    public async Task EndpointIdentityFollowsTheActiveMemberAndIsNullWhenThereIsNone()
    {
        var (_, group, executor) = Build();

        Assert.Null(await executor.IdentifyEndpointAsync("k", CommandFlags.None)); // fake has no endpoint
        group.Active = null;
        Assert.Null(await executor.IdentifyEndpointAsync("k", CommandFlags.None));
    }

    [Fact]
    public async Task ARealGroupsContextSurfaceWorksAgainstARealServer()
    {
        // MultiGroupDatabase.GetContext() used to throw "not yet wired for multi-group". This is what it
        // needed: an executor that resolves the active member per send, because the context is memoised
        // for the life of the multiplexer and the active member is the thing a group exists to change.
        IConnectionGroup group;
        try
        {
            group = await ConnectionMultiplexer.ConnectGroupAsync(
                [new ConnectionGroupMember($"{TestConfig.Current.PrimaryServer}:{TestConfig.Current.PrimaryPort}")]);
        }
        catch (RedisConnectionException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = group;

        var db = group.GetDatabase();
        RedisKey key = "RespGroupExecutorTests:context";

        // the old surface through the group, as a control
        await db.KeyDeleteAsync(key);
        Assert.Equal(1, await db.StringIncrementAsync(key));

        // and the NEW surface through the same group, reaching the same server
        var context = db.Context;
        Assert.Equal(2, (long)await context.Strings.IncrementAsync(key));
        Assert.Equal("2", (string?)await context.Strings.GetAsync(key));
    }

    [Fact]
    public async Task TheGroupsContextExistsBeforeAnyMemberConnects()
    {
        // the command map is CONFIGURATION - each member is configured separately and the group reads the
        // map from that - so there is nothing to wait for and the context is buildable while everything
        // is down. The one thing that is NOT configuration is a defaulted database index; see below.
        var config = ConfigurationOptions.Parse("127.0.0.1:6399"); // deliberately nothing listening
        config.AbortOnConnectFail = false;
        config.ConnectTimeout = 500;
        config.CommandMap = CommandMap.Create(new HashSet<string> { "INCR" }, available: false);

        IConnectionGroup group;
        try
        {
            group = await ConnectionMultiplexer.ConnectGroupAsync([new ConnectionGroupMember(config)]);
        }
        catch (RedisConnectionException)
        {
            Assert.Skip("group refused to construct while down; nothing to assert here");
            return;
        }

        await using var owner = group;

        // an EXPLICIT database needs no member, so this is fully determined with nothing reachable
        var context = group.GetDatabase(0).Context;
        Assert.False(context.Raw.CommandMap.IsAvailable(RedisCommand.INCR)); // the CONFIGURED map
        Assert.True(context.Raw.CommandMap.IsAvailable(RedisCommand.GET));

        // and using it reports the group being down, rather than something about a half-built context
        var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () => await context.Strings.GetAsync("k"));
        Assert.Contains("unavailable", ex.Message);
    }

    [Fact]
    public async Task ADefaultedDatabaseIndexStillNeedsAMember()
    {
        // the honest asymmetry: -1 means "whatever the active member defaults to", which cannot be known
        // with nothing connected. Throwing matches what the Database property itself already does, so the
        // context surface is not inventing a new failure mode here.
        var config = ConfigurationOptions.Parse("127.0.0.1:6399");
        config.AbortOnConnectFail = false;
        config.ConnectTimeout = 500;

        IConnectionGroup group;
        try
        {
            group = await ConnectionMultiplexer.ConnectGroupAsync([new ConnectionGroupMember(config)]);
        }
        catch (RedisConnectionException)
        {
            Assert.Skip("group refused to construct while down; nothing to assert here");
            return;
        }

        await using var owner = group;

        var db = group.GetDatabase();
        Assert.Throws<RedisConnectionException>(() => db.Database);      // the shipped property
        Assert.Throws<RedisConnectionException>(() => _ = db.Context);   // and the context agrees
    }

    [Fact]
    public async Task MembersMustAgreeAboutTheDefaultDatabase()
    {
        // the silent one. GetDatabase() with no index resolves through whichever member is active, each
        // applying its OWN DefaultDatabase - so a failover between members configured differently moves
        // the caller to a different database, with no error and no event. Data going somewhere it was not
        // meant to, discovered during an outage. Refused at construction instead.
        var a = ConfigurationOptions.Parse("127.0.0.1:6399");
        a.DefaultDatabase = 0;
        var b = ConfigurationOptions.Parse("127.0.0.1:6398");
        b.DefaultDatabase = 3;

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ConnectionMultiplexer.ConnectGroupAsync(
                [new ConnectionGroupMember(a), new ConnectionGroupMember(b)]));

        Assert.Contains("same DefaultDatabase", ex.Message);
        Assert.Contains("member 0 says 0 and member 1 says 3", ex.Message);
    }

    [Fact]
    public async Task MembersMustAgreeAboutTheCommandMap()
    {
        var a = ConfigurationOptions.Parse("127.0.0.1:6399");
        var b = ConfigurationOptions.Parse("127.0.0.1:6398");
        b.CommandMap = CommandMap.Create(new HashSet<string> { "INCR" }, available: false);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ConnectionMultiplexer.ConnectGroupAsync(
                [new ConnectionGroupMember(a), new ConnectionGroupMember(b)]));

        Assert.Contains("same CommandMap", ex.Message);
    }

    [Fact]
    public void AgreementIsStructuralRatherThanByIdentity()
    {
        // configuration is usually reached by parsing a string, so two members saying exactly the same
        // thing hold DIFFERENT instances; identity would reject every correctly-configured group
        var first = ConfigurationOptions.Parse("127.0.0.1:6399").CommandMap;
        var second = ConfigurationOptions.Parse("127.0.0.1:6398").CommandMap;

        Assert.True(first.StructurallyEquals(second));

        var third = CommandMap.Create(new HashSet<string> { "INCR" }, available: false);
        Assert.False(first.StructurallyEquals(third));
        Assert.True(third.StructurallyEquals(CommandMap.Create(new HashSet<string> { "INCR" }, available: false)));
    }

    [Fact]
    public async Task TheCacheFollowsTheActiveMemberRatherThanBeingShared()
    {
        // A cached reply is only sound while the connection that produced it is the one being asked and
        // its CLIENT TRACKING registration is live. A shared group cache would serve member A's value
        // while B is active - wrong, and silently. So the cache is resolved per command, like everything
        // else about a group.
        using var cacheA = new RespClientCache();
        using var cacheB = new RespClientCache();
        RespClientCache? active = cacheA;

        var memberA = new Member("a", "$1\r\nA\r\n");
        RespExecutorBase? activeExecutor = memberA;

        var context = new RespDatabaseContext(
            new RespContext()
                .WithExecutor(new RespGroupExecutor(() => activeExecutor))
                .WithCacheResolver(() => active));

        const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;
        Assert.Equal("A", (string?)await context.Strings.GetAsync("k", Readable));
        Assert.Equal(1, memberA.Sends);

        // second read while A is active: served from A's cache, nothing on the wire
        Assert.Equal("A", (string?)await context.Strings.GetAsync("k", Readable));
        Assert.Equal(1, memberA.Sends);

        // failover: B's cache is cold, so the same key MUST go to the wire again rather than being
        // answered from the value A gave us
        var memberB = new Member("b", "$1\r\nB\r\n");
        activeExecutor = memberB;
        active = cacheB;

        Assert.Equal("B", (string?)await context.Strings.GetAsync("k", Readable));
        Assert.Equal(1, memberA.Sends);   // A was not asked again...
        Assert.Equal(1, memberB.Sends);   // ...and B really was

        // and switching back finds A's cache still warm, which nuking a shared cache would have lost
        activeExecutor = memberA;
        active = cacheA;
        Assert.Equal("A", (string?)await context.Strings.GetAsync("k", Readable));
        Assert.Equal(1, memberA.Sends);
    }

    [Fact]
    public async Task AGroupOverEndpointsComposesRatherThanReimplements()
    {
        // the three executors stack: group picks the member, and the member's own executor owns its
        // connection. Nothing about connecting, reconnecting or backlogs is repeated here.
        var topology = new RespTopology(RespClusterState.No);
        var endpointA = new Member("a", "$1\r\nA\r\n");
        var endpointB = new Member("b", "$1\r\nB\r\n");

        var muxerA = new RespMultiplexerExecutor(topology, _ => endpointA, () => endpointA);
        var muxerB = new RespMultiplexerExecutor(topology, _ => endpointB, () => endpointB);

        RespExecutorBase? active = muxerA;
        var group = new RespGroupExecutor(() => active);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(group));

        Assert.Equal("A", (string?)await context.Strings.GetAsync("k"));

        active = muxerB;
        Assert.Equal("B", (string?)await context.Strings.GetAsync("k"));
    }
}
