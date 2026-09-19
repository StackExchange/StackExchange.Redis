using System;
using StackExchange.Redis.Availability;
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
