using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Routing: the multiplexer executor, and the late-bound topology it reads. Design notes section 3b.
/// </summary>
/// <remarks>
/// The case that drives the design is a multiplexer constructed while disconnected: nobody knows yet
/// whether this is a cluster, <c>GetDatabase()</c> is called anyway, and the context it builds is
/// memoised for the life of the multiplexer. Anything that decided routing at that moment would be wrong
/// forever.
/// </remarks>
public class RespMultiplexerExecutorTests
{
    /// <summary>An endpoint that records what reached it.</summary>
    private sealed class Endpoint(string name, params string[] replies) : FakeExecutor(replies)
    {
        internal string Name => name;
    }

    private sealed class Router
    {
        internal readonly Dictionary<int, Endpoint> BySlot = [];

        internal Endpoint? Default;

        internal int SlotLookups;

        internal CommandFlags LastFlags;

        internal RespExecutorBase? ForSlot(int slot, RedisCommand command, CommandFlags flags)
        {
            SlotLookups++;
            LastFlags = flags;
            return BySlot.TryGetValue(slot, out var endpoint) ? endpoint : Default;
        }

        internal RespExecutorBase? Any(RedisCommand command, CommandFlags flags)
        {
            LastFlags = flags;
            return Default;
        }
    }

    private static (RespDatabaseContext Context, RespTopology Topology, Router Router) Build(
        RespClusterState known = RespClusterState.No)
    {
        var topology = new RespTopology(known);
        var router = new Router { Default = new Endpoint("default", "$2\r\nok\r\n") };
        var executor = new RespMultiplexerExecutor(topology, router.ForSlot, router.Any);
        var raw = new RespContext(serverType: ServerType.Standalone).WithTopology(topology).WithExecutor(executor);
        return (new RespDatabaseContext(raw), topology, router);
    }

    [Fact]
    public async Task OutsideClusterNothingIsHashedAndNothingIsLookedUp()
    {
        // the fast path is not just "we skip the dictionary" - the request never had a slot computed
        // either, because the builder consults the same topology before hashing a key
        var (context, _, router) = Build();

        Assert.Equal("ok", (string?)await context.Strings.GetAsync("user:1"));

        Assert.Equal(0, router.SlotLookups);
        Assert.Equal(ServerSelectionStrategy.NoSlot, SlotOf(context, "user:1"));
    }

    [Fact]
    public async Task InClusterTheSlotDecidesTheEndpoint()
    {
        var (context, topology, router) = Build(RespClusterState.Yes);
        var slot = ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1");
        var owner = new Endpoint("owner", "$4\r\nmine\r\n");
        router.BySlot[slot] = owner;

        Assert.Equal(RespClusterState.Yes, topology.State);
        Assert.Equal("mine", (string?)await context.Strings.GetAsync("user:1"));

        Assert.True(owner.HasSent);
        Assert.False(router.Default!.HasSent);
        Assert.Equal(1, router.SlotLookups);
    }

    [Fact]
    public async Task LearningItIsAClusterChangesRoutingWithoutRebuildingAnything()
    {
        // THE case: the context was built and memoised while nobody knew. Discovering the cluster later
        // has to change behaviour on the very next command, with no new context and no new executor.
        var (context, topology, router) = Build();
        var slot = ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1");
        var owner = new Endpoint("owner", "$4\r\nmine\r\n");
        router.BySlot[slot] = owner;

        await context.Strings.GetAsync("user:1");
        Assert.True(router.Default!.HasSent);   // went to the only endpoint we knew about
        Assert.False(owner.HasSent);

        topology.OnServerType(ServerType.Cluster);   // the discovery

        Assert.Equal("mine", (string?)await context.Strings.GetAsync("user:1"));
        Assert.True(owner.HasSent);                 // and now it is routed by slot
        Assert.NotEqual(ServerSelectionStrategy.NoSlot, SlotOf(context, "user:1"));
    }

    [Fact]
    public async Task WhileUnknownSlotsAreComputedSpeculativelyButNotActedOn()
    {
        // the two questions are not the same, and this is where that matters. "Might a slot matter?" is
        // YES while unknown, so the slot is there if it turns out to be needed. "Does the slot decide
        // where this goes?" is NO while unknown, because routing on a guess is wrong rather than wasteful.
        var (context, topology, router) = Build(RespClusterState.Unknown);
        var slot = ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1");
        var owner = new Endpoint("owner", "$4\r\nmine\r\n");
        router.BySlot[slot] = owner;

        Assert.NotEqual(ServerSelectionStrategy.NoSlot, SlotOf(context, "user:1"));  // computed...
        await context.Strings.GetAsync("user:1");
        Assert.True(router.Default!.HasSent);                                        // ...but not obeyed
        Assert.False(owner.HasSent);
    }

    [Fact]
    public async Task ARequestRenderedBeforeTheAnswerArrivedIsStillRoutable()
    {
        // THE reason Unknown computes slots. With a plain bool defaulting to "not a cluster", this
        // request would carry NoSlot, and discovering the cluster a moment later would leave it
        // unroutable - to be sent somewhere arbitrary and corrected by a redirect, if it is lucky.
        var (context, topology, router) = Build(RespClusterState.Unknown);
        var slot = ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1");
        var owner = new Endpoint("owner", "$4\r\nmine\r\n");
        router.BySlot[slot] = owner;

        using var request = context.Raw.Render($"{RedisCommand.GET}{(RedisKey)"user:1"}").Detach();
        Assert.Equal(slot, request.Slot);           // rendered while nobody knew, and carries its slot

        topology.OnServerType(ServerType.Cluster);  // ...and now we know

        var executor = context.Raw.Executor!;
        using var payload = await executor.SendAsync(request);
        Assert.True(owner.HasSent);                 // routed correctly despite predating the discovery
    }

    [Fact]
    public async Task OnceKnownNotToBeAClusterTheHashingStops()
    {
        var (context, topology, _) = Build(RespClusterState.Unknown);
        Assert.NotEqual(ServerSelectionStrategy.NoSlot, SlotOf(context, "user:1"));

        topology.OnServerType(ServerType.Standalone);

        Assert.Equal(ServerSelectionStrategy.NoSlot, SlotOf(context, "user:1"));
        await context.Strings.GetAsync("user:1");
    }

    [Fact]
    public async Task AKeylessCommandInAClusterGoesAnywhere()
    {
        // PING has nowhere in particular to go; so does a command rendered before the topology was known
        var (context, _, router) = Build(RespClusterState.Yes);

        await context.SendAsync($"{RedisCommand.PING}");

        Assert.True(router.Default!.HasSent);
        Assert.Equal(0, router.SlotLookups); // NoSlot short-circuits before the router is asked
    }

    [Fact]
    public async Task ACommandSpanningSlotsIsRefusedRatherThanGuessed()
    {
        var (context, _, _) = Build(RespClusterState.Yes);

        // two keys that hash to different slots: no single node can serve this
        await Assert.ThrowsAsync<RedisCommandException>(
            async () => await context.SendAsync($"{RedisCommand.MGET}{(RedisKey)"aaa"}{(RedisKey)"bbb"}"));
    }

    [Fact]
    public async Task HashTagsKeepRelatedKeysTogether()
    {
        // the whole point of hash tags: {user:1} pins both keys to one slot, so this IS servable
        var (context, _, router) = Build(RespClusterState.Yes);
        var slot = ServerSelectionStrategy.GetHashSlot((RedisKey)"{user:1}:a");
        var owner = new Endpoint("owner", "*2\r\n$1\r\nx\r\n$1\r\ny\r\n");
        router.BySlot[slot] = owner;

        await context.SendAsync($"{RedisCommand.MGET}{(RedisKey)"{user:1}:a"}{(RedisKey)"{user:1}:b"}");

        Assert.True(owner.HasSent);
    }

    [Fact]
    public async Task WithNoEndpointAtAllTheFailureSaysSo()
    {
        var (context, _, router) = Build();
        router.Default = null;

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () => await context.Strings.GetAsync("k"));
        Assert.Equal(ConnectionFailureType.UnableToResolvePhysicalConnection, ex.FailureType);
        Assert.Equal(CommandStatus.WaitingToBeSent, ex.CommandStatus); // never sent, so retry may re-issue
    }

    [Fact]
    public void RoutingQuestionsFollowTheSameTopology()
    {
        var (context, topology, router) = Build();
        var slot = ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1");
        router.BySlot[slot] = new Endpoint("owner", "$2\r\nok\r\n");

        var db = new TransitionalDatabase(context, null!, null);
        Assert.True(db.IsConnected("user:1"));
        Assert.Equal(0, router.SlotLookups); // standalone: no hashing even for the routing question

        topology.OnServerType(ServerType.Cluster);
        Assert.True(db.IsConnected("user:1"));
        Assert.Equal(1, router.SlotLookups); // cluster: now it asks who owns the slot
    }

    /// <summary>The slot the writer actually stamped on a rendered request.</summary>
    private static int SlotOf(RespDatabaseContext context, RedisKey key)
    {
        using var request = context.Raw.Render($"{RedisCommand.GET}{key}").Detach();
        return request.Slot;
    }
}
