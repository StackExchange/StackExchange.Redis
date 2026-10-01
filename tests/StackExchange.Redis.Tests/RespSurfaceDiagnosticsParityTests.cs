using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The diagnostic commands <see cref="IServer"/> used to build a <c>Message</c> for, now rendered by the
/// context surface - checked against the bytes that <c>Message</c> produced.
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
public class RespSurfaceDiagnosticsParityTests
{
    private sealed class FakeExecutor(string reply) : RespExecutorBase
    {
        public List<string> Sent { get; } = [];

        public override int Database => -1;

        public override RespPayload Send(in RespRequest request)
        {
            Sent.Add(Text(request.Span));
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
