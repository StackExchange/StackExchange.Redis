using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The <see cref="IServer"/> commands that used to build a <c>Message</c> and now render themselves on
/// the context surface - checked against the bytes that <c>Message</c> produced.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are a port, and a port's risk is the wire form.</b> Every one of these commands has a
/// subcommand token and an argument order that no result assertion would catch getting wrong: a
/// <c>LATENCY HISTORY</c> that sent the event name before the subcommand would fail at the server, but a
/// <c>LATENCY RESET</c> that dropped a name would quietly reset the wrong set. So the expected bytes come
/// from the shipped <c>Message</c> builders, driven for real, rather than from a string I typed after
/// reading the code I was replacing - see <see cref="RespSurfaceStreamsParityTests"/> for the argument in
/// full.
/// </para>
/// <para>
/// The `Message.Create` calls below are deliberately kept <b>here</b> after being removed from
/// <c>RedisServer</c>: they are the record of what the shipped library sent, and that is what a port has
/// to keep sending. See design notes D2.8, where the `IServer` tail is item (3).
/// </para>
/// </remarks>
public class RespSurfaceServerParityTests
{
    private sealed class FakeExecutor(string reply, int database = -1) : RespExecutorBase
    {
        public List<string> Sent { get; } = [];

        /// <summary>The flags each request carried, which is where the retry category lives.</summary>
        public List<CommandFlags> Flags { get; } = [];

        private int _database = database;

        public override int Database => _database;

        /// <summary>Re-pointable, and the SAME instance, because the test reads what it recorded.</summary>
        /// <param name="database">The database to answer for.</param>
        /// <remarks>
        /// A real executor that cannot move refuses, so that a command cannot run against the wrong
        /// database; this one has no database and no socket, so moving it is free. Returning <c>this</c>
        /// rather than a copy matters: the context keeps whatever comes back, and a copy would record the
        /// send somewhere the test is not looking. Needed because <c>KEYS</c> is one of the few
        /// <see cref="IServer"/> members that takes a database.
        /// </remarks>
        internal override RespExecutorBase WithDatabase(int database)
        {
            _database = database;
            return this;
        }

        public override RespPayload Send(in RespRequest request)
        {
            Sent.Add(Text(request.Span));
            Flags.Add(request.Flags);
            return RespPayload.Create(Encoding.UTF8.GetBytes(reply));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static string Text(ReadOnlySpan<byte> value) =>
        Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    /// <summary>The bytes the shipped <c>Message</c> pipeline writes.</summary>
    private static string Classic(Message message)
    {
        var writer = new RespFrameWriter();
        message.WriteTo(new MessageWriter(null, CommandMap.Default, writer));
        using var frame = writer.Complete(ServerSelectionStrategy.NoSlot);
        return Text(frame.Span);
    }

    /// <summary>The bytes the context surface writes, with the send driven to completion.</summary>
    private static string Modern(Func<RespServerContext, ValueTask> send, string reply)
    {
        var executor = new FakeExecutor(reply);
        var task = send(new RespServerContext(new RespContext().WithExecutor(executor)));
        Assert.True(task.IsCompleted); // the fake is synchronous; anything else means a stray await
        task.GetAwaiter().GetResult();
        return Assert.Single(executor.Sent);
    }

    private static void AssertSame(Message classic, Func<RespServerContext, ValueTask> modern, string reply)
        => Assert.Equal(Classic(classic), Modern(modern, reply));

    private const string IntReply = ":0\r\n";
    private const string HistoryReply = "*2\r\n*2\r\n:1405067822\r\n:251\r\n*2\r\n:1405067941\r\n:1001\r\n";
    private const string LatestReply = "*1\r\n*4\r\n$7\r\ncommand\r\n:1405067976\r\n:251\r\n:1001\r\n";
    private const string MapReply = "*2\r\n$14\r\npeak.allocated\r\n:1024\r\n";
    private const string PairsReply = "*2\r\n$9\r\nmaxmemory\r\n$1\r\n0\r\n";
    private const string KeysReply = "*1\r\n$1\r\nk\r\n";
    private const string RoleReply = "*3\r\n$6\r\nmaster\r\n:3129659\r\n*0\r\n";
    private const string SubscribeReply = "*3\r\n$9\r\nsubscribe\r\n$4\r\nchan\r\n:1\r\n";
    private const string UnsubscribeReply = "*3\r\n$11\r\nunsubscribe\r\n$4\r\nchan\r\n:0\r\n";
    private const string ClientListLine =
        "id=7 addr=127.0.0.1:1234 name=someName age=1 idle=0 flags=N db=0 sub=0 psub=0 multi=-1 cmd=client|list";
    private static readonly string ClientListReply = $"${ClientListLine.Length}\r\n{ClientListLine}\r\n";
    private const string StringsReply = "*1\r\n$3\r\nget\r\n";
    private const string SlowLogReply =                 // one entry: id, time, duration, [command, args]
        "*1\r\n*4\r\n:1\r\n:1405067822\r\n:251\r\n*2\r\n$3\r\nget\r\n$1\r\nk\r\n";

    [Fact]
    public void LatencyResetWithNoEventNamesResetsEverything()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.LATENCY, RedisLiterals.RESET),
            static ctx => Discard(ctx.Diagnostics.LatencyResetAsync()),
            IntReply);

    [Fact]
    public void LatencyResetCarriesOneEventName()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.LATENCY, RedisLiterals.RESET, (RedisValue)"command"),
            static ctx => Discard(ctx.Diagnostics.LatencyResetAsync(["command"])),
            IntReply);

    /// <summary>The many-names case, which is where the shipped code built its own array.</summary>
    [Fact]
    public void LatencyResetCarriesEveryEventNameInOrder()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.LATENCY, new RedisValue[] { RedisLiterals.RESET, "command", "fast-command" }),
            static ctx => Discard(ctx.Diagnostics.LatencyResetAsync(["command", "fast-command"])),
            IntReply);

    [Fact]
    public void LatencyHistoryNamesItsEventAfterTheSubcommand()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.LATENCY, RedisLiterals.HISTORY, (RedisValue)"command"),
            static ctx => Discard(ctx.Diagnostics.LatencyHistoryAsync("command")),
            HistoryReply);

    [Fact]
    public void LatencyLatest()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.LATENCY, RedisLiterals.LATEST),
            static ctx => Discard(ctx.Diagnostics.LatencyLatestAsync()),
            LatestReply);

    [Fact]
    public void MemoryStats()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.MEMORY, RedisLiterals.STATS),
            static ctx => Discard(ctx.Diagnostics.MemoryStatsAsync()),
            MapReply);

    /// <summary>A non-positive count omits the argument rather than sending zero.</summary>
    [Fact]
    public void SlowLogGetWithNoCountAsksForWhateverTheServerVolunteers()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.SLOWLOG, RedisLiterals.GET),
            static ctx => Discard(ctx.Diagnostics.SlowLogAsync()),
            SlowLogReply);

    [Fact]
    public void SlowLogGetCarriesItsCount()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.SLOWLOG, RedisLiterals.GET, 25),
            static ctx => Discard(ctx.Diagnostics.SlowLogAsync(25)),
            SlowLogReply);

    [Fact]
    public async Task TheSlowLogHandlerReadsItsReply()
    {
        var entries = await Context(SlowLogReply).Diagnostics.SlowLogAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(1L, entry.UniqueId);
        Assert.Equal(251, entry.Duration.Ticks / 10); // SLOWLOG reports microseconds, and a tick is 100ns
        Assert.Equal("get", entry.Arguments[0].ToString());
    }

    /// <summary>
    /// Each of these is a read whose answer belongs to the node that was asked, and says so.
    /// </summary>
    /// <remarks>
    /// <b>This assertion moved here from <c>CommandRetryCategoryUnitTests</c></b>, which could only reach
    /// it through a <c>Message</c> builder. <c>CLIENT</c>/<c>CLUSTER</c>/<c>CONFIG</c>/<c>MEMORY</c>/
    /// <c>LATENCY</c>/<c>SLOWLOG</c> are each one <see cref="RedisCommand"/> spanning very different
    /// verbs, so the whole-command default has to assume the most side-effecting one; a sub-command that
    /// only reads has to say so, or a retry that is safe will not be attempted - and the node-scoped bit
    /// has to survive, or the retry goes to a server that was never asked.
    /// </remarks>
    [Fact]
    public async Task TheseReadsAreNodeLocal()
    {
        await AssertNodeLocalRead(static ctx => Discard(ctx.Diagnostics.SlowLogAsync()), SlowLogReply);
        await AssertNodeLocalRead(static ctx => Discard(ctx.Diagnostics.SlowLogAsync(25)), SlowLogReply);
        await AssertNodeLocalRead(static ctx => Discard(ctx.Diagnostics.LatencyHistoryAsync("command")), HistoryReply);
        await AssertNodeLocalRead(static ctx => Discard(ctx.Diagnostics.LatencyLatestAsync()), LatestReply);
        await AssertNodeLocalRead(static ctx => Discard(ctx.Diagnostics.MemoryStatsAsync()), MapReply);

        static async Task AssertNodeLocalRead(Func<RespServerContext, ValueTask> send, string reply)
        {
            var executor = new FakeExecutor(reply);
            await send(new RespServerContext(new RespContext().WithExecutor(executor)));

            var flags = Assert.Single(executor.Flags);
            var sent = Assert.Single(executor.Sent);
            Assert.Equal(CommandFlags.CommandRetryReadOnly, CommandFlagsInternal.GetRetryCategory(flags));
            Assert.True((flags & CommandFlagsInternal.CommandServerSpecific) != 0, sent);
        }
    }

    [Fact]
    public void Role()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.ROLE),
            static ctx => Discard(ctx.Diagnostics.RoleAsync()),
            RoleReply);

    /// <summary>A reply this cannot model answers null, as the shipped processor does.</summary>
    [Theory]
    [InlineData("*-1\r\n")]
    [InlineData("_\r\n")]
    [InlineData("*0\r\n")]
    [InlineData("+nonsense\r\n")]
    public async Task AnUnreadableRoleReplyIsNull(string reply)
        => Assert.Null(await Context(reply).Diagnostics.RoleAsync());

    [Fact]
    public async Task TheRoleHandlerReadsItsReply()
    {
        var role = await Context(RoleReply).Diagnostics.RoleAsync();
        var primary = Assert.IsType<Role.Master>(role);
        Assert.Equal("master", primary.Value);
        Assert.Equal(3129659L, primary.ReplicationOffset);
        Assert.Empty(primary.Replicas);
    }

    /// <summary>Each save type is its own command, and the background two check what came back.</summary>
    [Fact]
    public void Save()
    {
        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.BGREWRITEAOF),
            static ctx => ctx.Diagnostics.SaveAsync(SaveType.BackgroundRewriteAppendOnlyFile),
            "+Background append only file rewriting started\r\n");

        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.BGSAVE),
            static ctx => ctx.Diagnostics.SaveAsync(SaveType.BackgroundSave),
            "+Background saving started\r\n");

#pragma warning disable CS0618 // SAVE is obsolete; IServer still offers it
        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.SAVE),
            static ctx => ctx.Diagnostics.SaveAsync(SaveType.ForegroundSave),
            "+OK\r\n");
#pragma warning restore CS0618
    }

    /// <summary>
    /// A background save that did not say it had started is a failure, not a silent success.
    /// </summary>
    /// <remarks>
    /// The whole reason these two have a handler rather than ignoring the reply: "+OK" would mean the
    /// server answered something other than what <c>BGSAVE</c> answers, and reporting that as a save in
    /// progress is the one outcome worth catching.
    /// </remarks>
    [Fact]
    public async Task ABackgroundSaveThatDidNotStartThrows()
    {
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await Context("+OK\r\n").Diagnostics.SaveAsync(SaveType.BackgroundSave));

        // ...and a longer reply that begins with the expected text is fine: the server appends detail
        await Context("+Background saving started by pid 1\r\n").Diagnostics.SaveAsync(SaveType.BackgroundSave);
    }

    [Fact]
    public void Shutdown()
    {
        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.SHUTDOWN),
            static ctx => ctx.Diagnostics.ShutdownAsync(),
            "+OK\r\n");

        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.SHUTDOWN, RedisLiterals.SAVE),
            static ctx => ctx.Diagnostics.ShutdownAsync(ShutdownMode.Always),
            "+OK\r\n");

        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.SHUTDOWN, RedisLiterals.NOSAVE),
            static ctx => ctx.Diagnostics.ShutdownAsync(ShutdownMode.Never),
            "+OK\r\n");
    }

    /// <summary>
    /// <c>KEYS</c>, which <see cref="IServer"/> only reaches on a server with no <c>SCAN</c>.
    /// </summary>
    /// <remarks>
    /// Worth pinning here precisely because the test topology always has <c>SCAN</c>, so nothing else in
    /// the suite sends this: a port whose only route is the one nobody exercises is a port nobody has
    /// checked.
    /// </remarks>
    [Fact]
    public async Task Keys()
    {
        Assert.Equal(
            Classic(Message.Create(4, CommandFlags.None, RedisCommand.KEYS, (RedisValue)"a*")),
            Modern(static ctx => Discard(new RespKeys(ctx.Raw.WithDatabase(4)).MatchingArray("a*")), KeysReply));

        var keys = await new RespKeys(new RespContext().WithExecutor(new FakeExecutor(KeysReply))).MatchingArray("*");
        Assert.Equal("k", Assert.Single(keys).ToString());
    }

    /// <summary>
    /// The six (un)subscribe spellings, against the <c>Message</c> the shipped subscription builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not yet wired to anything</b>, and pinned anyway: <c>PubSub.SubscribeAsync</c> is how
    /// subscribing will reach this core, and the part that has to be right first is which of the six
    /// commands a channel's options select - get that wrong and a pattern subscription silently becomes a
    /// literal one. Proving it against <c>Subscription.GetSubscriptionMessage</c> means the mapping is
    /// settled before the routing that depends on it is attempted; see design notes D2.5.
    /// </para>
    /// <para>
    /// <c>KeyRouted</c> appears on purpose: it changes where a subscription goes, not what it is called,
    /// so a key-routed literal channel must still say <c>SUBSCRIBE</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(RedisChannel.RedisChannelOptions.None)]
    [InlineData(RedisChannel.RedisChannelOptions.MultiNode)]
    [InlineData(RedisChannel.RedisChannelOptions.Pattern)]
    [InlineData(RedisChannel.RedisChannelOptions.Pattern | RedisChannel.RedisChannelOptions.MultiNode)]
    [InlineData(RedisChannel.RedisChannelOptions.Sharded)]
    [InlineData(RedisChannel.RedisChannelOptions.KeyRouted)]
    internal void SubscribeSpellingsMatchTheShippedOnes(RedisChannel.RedisChannelOptions options)
    {
        var channel = new RedisChannel("chan"u8.ToArray(), options);
        var subscription = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.None);

        foreach (var action in new[] { ConnectionMultiplexer.SubscriptionAction.Subscribe, ConnectionMultiplexer.SubscriptionAction.Unsubscribe })
        {
            var classic = subscription.GetSubscriptionMessage(channel, action, CommandFlags.None, internalCall: false);
            var modern = PubSub.SubscribeCommand(channel, subscribe: action == ConnectionMultiplexer.SubscriptionAction.Subscribe);

            Assert.Equal(classic.Command, modern);
        }
    }

    /// <summary>And the frames agree too, not just the command words.</summary>
    [Fact]
    public void SubscribeFramesMatchTheShippedOnes()
    {
        RedisChannel channel = RedisChannel.Literal("chan");
        var subscription = new ConnectionMultiplexer.SingleNodeSubscription(CommandFlags.None);

        Assert.Equal(
            Classic(subscription.GetSubscriptionMessage(channel, ConnectionMultiplexer.SubscriptionAction.Subscribe, CommandFlags.None, false)),
            Modern(ctx => Discard(new RespPubSub(ctx.Raw).SubscribeAsync(channel)), SubscribeReply));

        Assert.Equal(
            Classic(subscription.GetSubscriptionMessage(channel, ConnectionMultiplexer.SubscriptionAction.Unsubscribe, CommandFlags.None, false)),
            Modern(ctx => Discard(new RespPubSub(ctx.Raw).UnsubscribeAsync(channel)), UnsubscribeReply));
    }

    [Fact]
    public async Task TheConfirmationHandlerReadsTheCount()
    {
        RedisChannel channel = RedisChannel.Literal("chan");
        Assert.Equal(1, await new RespPubSub(new RespContext().WithExecutor(new FakeExecutor(SubscribeReply))).SubscribeAsync(channel));
        Assert.Equal(0, await new RespPubSub(new RespContext().WithExecutor(new FakeExecutor(UnsubscribeReply))).UnsubscribeAsync(channel));
    }

    /// <summary>
    /// <see cref="IServer"/>'s database-scoped <c>GET</c>, which takes its database explicitly.
    /// </summary>
    /// <remarks>
    /// <b>Pinned because nothing else sends it.</b> Every <c>StringGet</c> in the suite is the
    /// <see cref="IDatabase"/> one; this overload exists so a caller holding an <see cref="IServer"/> can
    /// read from a named database on that node, and a port of it would otherwise ship unexercised. The
    /// database is on the context rather than the command, which is what makes the <c>SELECT</c> happen
    /// where it should.
    /// </remarks>
    [Fact]
    public async Task ServerScopedStringGet()
    {
        RedisKey key = "k";
        Assert.Equal(
            Classic(Message.Create(4, CommandFlags.None, RedisCommand.GET, key)),
            Modern(static ctx => Discard(new RespStrings(ctx.Raw.WithDatabase(4)).GetAsync("k")), "$1\r\nv\r\n"));

        var value = await new RespStrings(new RespContext().WithExecutor(new FakeExecutor("$1\r\nv\r\n")))
            .GetAsync(key);
        Assert.Equal("v", value);
    }

    /// <summary>
    /// <c>MEMORY PURGE</c> is an administrative action on the node asked, where bare <c>MEMORY</c>
    /// defaults to read-only.
    /// </summary>
    /// <remarks>
    /// <b>Moved here from <c>CommandRetryCategoryUnitTests</c></b>, which could only reach it through a
    /// <c>Message</c> builder that nothing else used any more - production code kept alive for a test to
    /// look at. The category matters: <c>MEMORY</c> as a whole reads, so <c>PURGE</c> was once treated as
    /// a harmless read and retried as one, and the node-scoped bit has to survive or the retry goes to a
    /// server that was never asked.
    /// </remarks>
    [Fact]
    public async Task MemoryPurgeIsAdministrative()
    {
        var executor = new FakeExecutor("+OK\r\n");
        await new RespServerContext(new RespContext().WithExecutor(executor)).Diagnostics.MemoryPurgeAsync();

        var flags = Assert.Single(executor.Flags);
        Assert.Equal(CommandFlags.CommandRetryServerAdmin, CommandFlagsInternal.GetRetryCategory(flags));
        Assert.True((flags & CommandFlagsInternal.CommandServerSpecific) != 0, "the effect belongs to the node asked");
    }

    /// <summary>And a caller's own category still wins, without losing the node-scoped bit.</summary>
    [Fact]
    public async Task ACallerOverridesTheCategoryWithoutLosingNodeScope()
    {
        var executor = new FakeExecutor("+OK\r\n");
        await new RespServerContext(new RespContext().WithExecutor(executor))
            .Diagnostics.MemoryPurgeAsync(CommandFlags.CommandRetryAlways);

        var flags = Assert.Single(executor.Flags);
        Assert.Equal(CommandFlags.CommandRetryAlways, CommandFlagsInternal.GetRetryCategory(flags));
        Assert.True((flags & CommandFlagsInternal.CommandServerSpecific) != 0, "override must not clear server-specific");
    }

    [Fact]
    public void ClientList()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.CLIENT, RedisLiterals.LIST),
            static ctx => Discard(ctx.Diagnostics.ClientListArray()),
            ClientListReply);

    [Fact]
    public async Task TheClientListHandlerReadsItsReply()
    {
        var clients = await Context(ClientListReply).Diagnostics.ClientListArray();
        var client = Assert.Single(clients);
        Assert.Equal(7L, client.Id);
        Assert.Equal("someName", client.Name);
    }

    [Fact]
    public void CommandGetKeysPutsTheSubcommandFirst()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.COMMAND, new RedisValue[] { RedisLiterals.GETKEYS, "GET", "k" }),
            static ctx => Discard(ctx.Diagnostics.CommandGetKeysArray(["GET", "k"])),
            KeysReply);

    [Fact]
    public void CommandListUnfiltered()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.COMMAND, RedisLiterals.LIST),
            static ctx => Discard(ctx.Diagnostics.CommandListArray()),
            StringsReply);

    /// <summary>Each filter spells itself out in full, and only one may be given.</summary>
    [Fact]
    public void CommandListFilters()
    {
        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.COMMAND, new RedisValue[] { RedisLiterals.LIST, RedisLiterals.FILTERBY, RedisLiterals.MODULE, "mod" }),
            static ctx => Discard(ctx.Diagnostics.CommandListArray(moduleName: "mod")),
            StringsReply);

        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.COMMAND, new RedisValue[] { RedisLiterals.LIST, RedisLiterals.FILTERBY, RedisLiterals.ACLCAT, "read" }),
            static ctx => Discard(ctx.Diagnostics.CommandListArray(category: "read")),
            StringsReply);

        AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.COMMAND, new RedisValue[] { RedisLiterals.LIST, RedisLiterals.FILTERBY, RedisLiterals.PATTERN, "g*" }),
            static ctx => Discard(ctx.Diagnostics.CommandListArray(pattern: "g*")),
            StringsReply);

        var ex = Assert.Throws<ArgumentException>(
            () => Discard(Context(StringsReply).Diagnostics.CommandListArray(moduleName: "mod", category: "read")));
        Assert.Contains("More then one filter is not allowed", ex.Message);
    }

    [Fact]
    public async Task TheCommandHandlersReadTheirReplies()
    {
        var keys = await Context(KeysReply).Diagnostics.CommandGetKeysArray(["GET", "k"]);
        Assert.Equal("k", Assert.Single(keys).ToString());

        var names = await Context(StringsReply).Diagnostics.CommandListArray();
        Assert.Equal("get", Assert.Single(names));

        Assert.Empty(await Context("*0\r\n").Diagnostics.CommandListArray());
        Assert.Empty(await Context("*-1\r\n").Diagnostics.CommandGetKeysArray(["GET", "k"]));
    }

    [Fact]
    public void ConfigSet()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.CONFIG, RedisLiterals.SET, (RedisValue)"maxmemory", (RedisValue)"0"),
            static ctx => ctx.Config.SetAsync("maxmemory", "0"),
            "+OK\r\n");

    /// <summary>An omitted pattern becomes <c>*</c>; <c>CONFIG GET</c> with no pattern is an error.</summary>
    [Fact]
    public void ConfigGetWithNoPatternAsksForEverything()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.CONFIG, RedisLiterals.GET, RedisLiterals.Wildcard),
            static ctx => Discard(ctx.Config.GetArray()),
            PairsReply);

    [Fact]
    public void ConfigGetCarriesItsPattern()
        => AssertSame(
            Message.Create(-1, CommandFlags.None, RedisCommand.CONFIG, RedisLiterals.GET, (RedisValue)"maxmemory*"),
            static ctx => Discard(ctx.Config.GetArray("maxmemory*")),
            PairsReply);

    /// <summary>
    /// <c>CONFIG GET</c> is a connection-category read, not the server-admin that bare <c>CONFIG</c> is.
    /// </summary>
    /// <remarks><inheritdoc cref="TheseReadsAreNodeLocal" path="/remarks"/></remarks>
    [Fact]
    public async Task ConfigGetIsSafeMetadata()
    {
        var executor = new FakeExecutor(PairsReply);
        await Discard(new RespServerContext(new RespContext().WithExecutor(executor)).Config.GetArray());

        var flags = Assert.Single(executor.Flags);
        Assert.Equal(CommandFlags.CommandRetryConnection, CommandFlagsInternal.GetRetryCategory(flags));
        Assert.True((flags & CommandFlagsInternal.CommandServerSpecific) != 0, "the answer belongs to the node asked");
    }

    /// <summary>Both wire shapes of a <c>CONFIG GET</c> reply read the same.</summary>
    /// <remarks>
    /// RESP3 answers a map and RESP2 a flat array. The handler permits jagged pairs and then detects the
    /// shape from the bytes, which is what covers both - and a setting's value is always a scalar, so the
    /// detection has nothing to misfire on.
    /// </remarks>
    [Theory]
    [InlineData(PairsReply)]
    [InlineData("%1\r\n$9\r\nmaxmemory\r\n$1\r\n0\r\n")]
    [InlineData("*1\r\n*2\r\n$9\r\nmaxmemory\r\n$1\r\n0\r\n")]
    public async Task TheConfigHandlerReadsEitherShape(string reply)
    {
        var pairs = await Context(reply).Config.GetArray();
        var pair = Assert.Single(pairs);
        Assert.Equal("maxmemory", pair.Key);
        Assert.Equal("0", pair.Value);
    }

    [Fact]
    public async Task AnEmptyConfigGetReadsAsEmpty()
        => Assert.Empty(await Context("*0\r\n").Config.GetArray());

    /// <summary>The replies the handlers read, which the parity assertions above do not look at.</summary>
    /// <remarks>
    /// The element parses are the shipped ones - <c>LatencyHistoryEntry.TryParseEntry</c> and its
    /// sibling, already covered by <c>ResultProcessorUnitTests.Latency</c> - so what is new here is the
    /// array walk around them, and the null case.
    /// </remarks>
    [Fact]
    public async Task TheLatencyHandlersReadTheirReplies()
    {
        var history = await Context(HistoryReply).Diagnostics.LatencyHistoryAsync("command");
        Assert.Equal(2, history.Length);
        Assert.Equal(251, history[0].DurationMilliseconds);
        Assert.Equal(RedisBase.UnixEpoch.AddSeconds(1405067941), history[1].Timestamp);

        var latest = await Context(LatestReply).Diagnostics.LatencyLatestAsync();
        var entry = Assert.Single(latest);
        Assert.Equal("command", entry.EventName);
        Assert.Equal(251, entry.DurationMilliseconds);
        Assert.Equal(1001, entry.MaxDurationMilliseconds);
    }

    /// <summary>
    /// Nothing, rather than null - which is a deliberate difference from the shipped processors.
    /// </summary>
    /// <remarks>
    /// <c>LatencyLatestEntry.ToArray</c> answers <c>null</c> for a null reply, and <c>IServer</c> declares
    /// these members as returning a non-nullable array, so that was a null the signature said could not
    /// happen. <c>LATENCY</c> has no null reply to send, so nothing observes the change on a real server;
    /// it is the declared contract that improves.
    /// </remarks>
    [Theory]
    [InlineData("*-1\r\n")]
    [InlineData("_\r\n")]
    public async Task ANullLatencyReplyReadsAsEmpty(string reply)
    {
        Assert.Empty(await Context(reply).Diagnostics.LatencyHistoryAsync("command"));
        Assert.Empty(await Context(reply).Diagnostics.LatencyLatestAsync());
    }

    [Fact]
    public async Task TheEmptyLatencyRepliesReadAsEmpty()
    {
        Assert.Empty(await Context("*0\r\n").Diagnostics.LatencyHistoryAsync("command"));
        Assert.Empty(await Context("*0\r\n").Diagnostics.LatencyLatestAsync());
    }

    private static RespServerContext Context(string reply)
        => new RespServerContext(new RespContext().WithExecutor(new FakeExecutor(reply)));

    /// <summary>Drive a typed send for its bytes, discarding the reply.</summary>
    private static async ValueTask Discard<T>(ValueTask<T> pending) => _ = await pending;
}
