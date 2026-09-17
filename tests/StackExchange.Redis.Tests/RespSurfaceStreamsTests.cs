using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The stream group's scalar half: what goes on the wire.
/// </summary>
/// <remarks>
/// The optional tokens are where streams get interesting - <c>~</c>, <c>LIMIT n</c> and the trim mode are
/// three independent options on one command, and their <i>order</i> is fixed by the server. These pin the
/// bytes for each combination, because an option written in the wrong place is a server error at best and a
/// differently-interpreted command at worst.
/// </remarks>
public class RespSurfaceStreamsTests
{
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static (RespDatabaseContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? [":1\r\n"] : replies);
        return (new RespDatabaseContext(new RespContext().WithExecutor(executor)), executor);
    }

    [Fact]
    public async Task TheScalarCommandsRender()
    {
        var (ctx, exec) = Target();

        await ctx.Streams.LengthAsync("s");
        await ctx.Streams.AcknowledgeAsync("s", "g", "1-1");
        await ctx.Streams.AcknowledgeAsync("s", "g", [(RedisValue)"1-1", (RedisValue)"1-2"]);
        await ctx.Streams.DeleteAsync("s", [(RedisValue)"1-1"]);
        await ctx.Streams.DeleteConsumerAsync("s", "g", "c");
        await ctx.Streams.DeleteConsumerGroupAsync("s", "g");

        Assert.Equal(
            new[]
            {
                "*2|$4|XLEN|$1|s|",
                "*4|$4|XACK|$1|s|$1|g|$3|1-1|",
                "*5|$4|XACK|$1|s|$1|g|$3|1-1|$3|1-2|",
                "*3|$4|XDEL|$1|s|$3|1-1|",
                "*5|$6|XGROUP|$11|DELCONSUMER|$1|s|$1|g|$1|c|",
                "*4|$6|XGROUP|$7|DESTROY|$1|s|$1|g|",
            },
            exec.Sent);
    }

    /// <summary>The group position defaults to "new messages only", and XGROUP CREATE adds MKSTREAM by default.</summary>
    [Fact]
    public async Task ConsumerGroupCreationResolvesItsPosition()
    {
        var (ctx, exec) = Target("+OK\r\n");

        await ctx.Streams.CreateConsumerGroupAsync("s", "g");
        await ctx.Streams.CreateConsumerGroupAsync("s", "g", "0-0", createStream: false);
        await ctx.Streams.SetConsumerGroupPositionAsync("s", "g", "0-0");

        Assert.Equal(
            new[]
            {
                "*6|$6|XGROUP|$6|CREATE|$1|s|$1|g|$1|$|$8|MKSTREAM|",
                "*5|$6|XGROUP|$6|CREATE|$1|s|$1|g|$3|0-0|",
                "*5|$6|XGROUP|$5|SETID|$1|s|$1|g|$3|0-0|",
            },
            exec.Sent);
    }

    /// <summary>
    /// The three trim options are independent, and their order on the wire is the server's, not the caller's.
    /// </summary>
    /// <remarks>
    /// <c>~</c> precedes the threshold, <c>LIMIT</c> follows it, and the mode comes last - which is why the
    /// tail is one operand rather than three holes. The default mode is omitted deliberately: <c>XTRIM</c>
    /// predates the modes, so sending <c>KEEPREF</c> unasked would break against an older server that has
    /// never heard of it.
    /// </remarks>
    [Theory]
    [InlineData(false, null, StreamTrimMode.KeepReferences, "*4|$5|XTRIM|$1|s|$6|MAXLEN|$3|100|")]
    [InlineData(true, null, StreamTrimMode.KeepReferences, "*5|$5|XTRIM|$1|s|$6|MAXLEN|$1|~|$3|100|")]
    [InlineData(false, 50L, StreamTrimMode.KeepReferences, "*6|$5|XTRIM|$1|s|$6|MAXLEN|$3|100|$5|LIMIT|$2|50|")]
    [InlineData(false, null, StreamTrimMode.Acknowledged, "*5|$5|XTRIM|$1|s|$6|MAXLEN|$3|100|$5|ACKED|")]
    [InlineData(true, 50L, StreamTrimMode.DeleteReferences, "*8|$5|XTRIM|$1|s|$6|MAXLEN|$1|~|$3|100|$5|LIMIT|$2|50|$6|DELREF|")]
    public async Task TrimWritesItsOptionsInTheServersOrder(bool approximate, long? limit, StreamTrimMode mode, string expected)
    {
        var (ctx, exec) = Target();

        await ctx.Streams.TrimAsync("s", 100, approximate, limit, mode);

        Assert.Equal([expected], exec.Sent);
    }

    /// <summary>MINID is the same tail with a different strategy token.</summary>
    [Fact]
    public async Task TrimByMinIdSharesTheTail()
    {
        var (ctx, exec) = Target();

        await ctx.Streams.TrimByMinIdAsync("s", "5-0", approximate: true, limit: 10);

        Assert.Equal(["*7|$5|XTRIM|$1|s|$5|MINID|$1|~|$3|5-0|$5|LIMIT|$2|10|"], exec.Sent);
    }

    /// <summary>
    /// XDELEX always writes its mode, where XTRIM omits the default - and it counts its ids.
    /// </summary>
    [Fact]
    public async Task DeleteWithModeWritesIdsAndCount()
    {
        var (ctx, exec) = Target("*2\r\n:1\r\n:-1\r\n");

        using var results = await ctx.Streams.DeleteAsync("s", [(RedisValue)"1-1", (RedisValue)"1-2"], StreamTrimMode.KeepReferences);

        Assert.Equal(["*7|$6|XDELEX|$1|s|$7|KEEPREF|$3|IDS|$1|2|$3|1-1|$3|1-2|"], exec.Sent);
        Assert.Equal([StreamTrimResult.Deleted, StreamTrimResult.NotFound], results.Span.ToArray());
    }

    /// <summary>An empty id list is a caller error, not a request that trivially does nothing.</summary>
    [Fact]
    public async Task AnEmptyIdListIsRejected()
    {
        var (ctx, exec) = Target();

        Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Streams.DeleteAsync("s", ReadOnlySpan<RedisValue>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Streams.AcknowledgeAsync("s", "g", ReadOnlySpan<RedisValue>.Empty));
        Assert.Empty(exec.Sent);
        await Task.CompletedTask;
    }
}
