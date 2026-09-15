using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What the list group puts on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks><inheritdoc cref="RespSurfaceStringsTests" path="/remarks"/></remarks>
public class RespSurfaceListsTests
{
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public List<CommandFlags> Flags { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            Flags.Add(request.Flags);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static (RespContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? [":1\r\n"] : replies);
        return (new RespContext().WithExecutor(executor), executor);
    }

    [Fact]
    public async Task TheEndIsInTheCommandName()
    {
        var (ctx, exec) = Target();

        await ctx.Lists.LeftPush("k", "a");
        await ctx.Lists.RightPush("k", "a");
        await ctx.Lists.LeftPush("k", "a", When.Exists);
        await ctx.Lists.RightPush("k", "a", When.Exists);

        // LPUSH and RPUSH are two commands, not one with an operand - which is why the side stays in the
        // method name here and only Move takes a ListSide
        Assert.Equal(
            new[]
            {
                "*3|$5|LPUSH|$1|k|$1|a|",
                "*3|$5|RPUSH|$1|k|$1|a|",
                "*3|$6|LPUSHX|$1|k|$1|a|",
                "*3|$6|RPUSHX|$1|k|$1|a|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task PushingNothingAsksTheLengthInstead()
    {
        var (ctx, exec) = Target(":7\r\n");

        // an arity-zero LPUSH is a server error, but "how long is it now" still has an answer, and that
        // is what the old surface returns - so the empty run becomes LLEN
        Assert.Equal(7, await ctx.Lists.LeftPush("k", ReadOnlySpan<RedisValue>.Empty));
        Assert.Equal("*2|$4|LLEN|$1|k|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task ValuesAreValuesAndTheKeyIsAKey()
    {
        var (ctx, exec) = Target();

        await ctx.WithKeyPrefix("t:").Lists.RightPush("k", ["a", "b"]);

        Assert.Equal("*4|$5|RPUSH|$3|t:k|$1|a|$1|b|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task PopsHaveASingularAndACountedForm()
    {
        var (ctx, exec) = Target("$1\r\na\r\n", "*1\r\n$1\r\na\r\n", "$1\r\na\r\n", "*1\r\n$1\r\na\r\n");

        await ctx.Lists.LeftPop("k");
        using (await ctx.Lists.LeftPop("k", 2)) { }
        await ctx.Lists.RightPop("k");
        using (await ctx.Lists.RightPop("k", 2)) { }

        Assert.Equal(
            new[]
            {
                "*2|$4|LPOP|$1|k|",
                "*3|$4|LPOP|$1|k|$1|2|",
                "*2|$4|RPOP|$1|k|",
                "*3|$4|RPOP|$1|k|$1|2|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task MultiPopCountsItsKeysAndNamesItsEnd()
    {
        var (ctx, exec) = Target("*-1\r\n");

        RedisKey[] keys = ["a", "b"];
        await ctx.Lists.LeftPop(keys, 1);
        await ctx.Lists.RightPop(keys, 3);

        // COUNT is written even at 1, where the old builder omits it; both are valid and one shape beats
        // four bytes. What does NOT work is a null value in the hole to skip the number - a null still
        // writes an (empty) argument, which the server rejects.
        Assert.Equal(
            new[]
            {
                "*7|$5|LMPOP|$1|2|$1|a|$1|b|$4|LEFT|$5|COUNT|$1|1|",
                "*7|$5|LMPOP|$1|2|$1|a|$1|b|$5|RIGHT|$5|COUNT|$1|3|",
            },
            exec.Sent);
    }

    [Fact]
    public void MultiPopNeedsAKey()
    {
        var (ctx, _) = Target();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Lists.LeftPop(ReadOnlySpan<RedisKey>.Empty, 1));
        Assert.Contains("keys must have a size of at least 1", ex.Message);
    }

    [Fact]
    public async Task PositionAlwaysWritesRankAndMaxLen()
    {
        var (ctx, exec) = Target(":3\r\n");

        await ctx.Lists.Position("k", "v");
        await ctx.Lists.Position("k", "v", rank: -1, maxLength: 20);

        // the server's defaults are the same values, so writing them costs four arguments and keeps one
        // shape; the old builder does the same
        Assert.Equal(
            new[]
            {
                "*7|$4|LPOS|$1|k|$1|v|$4|RANK|$1|1|$6|MAXLEN|$1|0|",
                "*7|$4|LPOS|$1|k|$1|v|$4|RANK|$2|-1|$6|MAXLEN|$2|20|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task NotFoundIsMinusOneRatherThanNull()
    {
        var (ctx, _) = Target("$-1\r\n");

        // LPOS replies nil for "not found"; the old surface has always reported that as -1, and zero is
        // a perfectly good position, so this cannot just be default(long)
        Assert.Equal(-1, await ctx.Lists.Position("k", "nope"));
    }

    [Fact]
    public async Task ADiscardedReplyStillReportsMinusOne()
    {
        var (ctx, exec) = Target(":3\r\n");

        // fire-and-forget parses nothing, so the executor would hand back default - which for LPOS is a
        // WRONG answer rather than an absent one. The command still goes out.
        Assert.Equal(-1, await ctx.Lists.Position("k", "v", flags: CommandFlags.FireAndForget));
        Assert.Single(exec.Sent);
    }

    [Fact]
    public async Task PositionsAddsACount()
    {
        var (ctx, exec) = Target("*2\r\n:1\r\n:4\r\n");

        using var found = await ctx.Lists.Positions("k", "v", count: 0);
        Assert.Equal(new long[] { 1, 4 }, found.Span.ToArray());

        Assert.Equal("*9|$4|LPOS|$1|k|$1|v|$4|RANK|$1|1|$6|MAXLEN|$1|0|$5|COUNT|$1|0|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task MoveNamesBothSides()
    {
        var (ctx, exec) = Target("$1\r\na\r\n");

        await ctx.Lists.Move("src", "dst", ListSide.Right, ListSide.Left);

        // and this is what RPOPLPUSH is, which is why the group has no method for it
        Assert.Equal("*5|$5|LMOVE|$3|src|$3|dst|$5|RIGHT|$4|LEFT|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task TheBulkMoveCarriesItsModeAndOrder()
    {
        var (ctx, exec) = Target("*1\r\n$1\r\na\r\n");

        using (await ctx.Lists.Move("src", "dst", ListSide.Left, ListSide.Right, 3)) { }
        using (await ctx.Lists.Move("src", "dst", ListSide.Left, ListSide.Right, 3, ListMoveCount.Exactly, ListMoveOrder.OneByOne)) { }

        Assert.Equal(
            new[]
            {
                "*8|$6|LMOVEM|$3|src|$3|dst|$4|LEFT|$5|RIGHT|$5|COUNT|$1|3|$4|BULK|",
                "*8|$6|LMOVEM|$3|src|$3|dst|$4|LEFT|$5|RIGHT|$7|EXACTLY|$1|3|$3|OBO|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task TheBulkMoveTellsNilApartFromEmpty()
    {
        var (nothing, _) = Target("*-1\r\n");
        var (empty, _) = Target("*0\r\n");

        // the one array reply in this library where those are different answers: nil means nothing moved
        Assert.Null(await nothing.Lists.Move("s", "d", ListSide.Left, ListSide.Right, 1));

        using var moved = await empty.Lists.Move("s", "d", ListSide.Left, ListSide.Right, 1);
        Assert.NotNull(moved);
        Assert.True(moved.IsEmpty);
    }

    [Fact]
    public async Task InsertNamesItsSide()
    {
        var (ctx, exec) = Target(":4\r\n");

        await ctx.Lists.InsertBefore("k", "pivot", "v");
        await ctx.Lists.InsertAfter("k", "pivot", "v");

        Assert.Equal(
            new[]
            {
                "*5|$7|LINSERT|$1|k|$6|BEFORE|$5|pivot|$1|v|",
                "*5|$7|LINSERT|$1|k|$5|AFTER|$5|pivot|$1|v|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task RemovePutsTheCountBeforeTheValue()
    {
        var (ctx, exec) = Target(":2\r\n");

        await ctx.Lists.Remove("k", "v", -2);

        // LREM key count element, not key element count - the one ordering here that reads backwards
        Assert.Equal("*4|$4|LREM|$1|k|$2|-2|$1|v|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task TheResultLessCommandsStillReadTheirReply()
    {
        var (ctx, exec) = Target("+OK\r\n");

        await ctx.Lists.SetByIndex("k", 0, "v");
        await ctx.Lists.Trim("k", 0, 9);

        Assert.Equal(
            new[] { "*4|$4|LSET|$1|k|$1|0|$1|v|", "*4|$5|LTRIM|$1|k|$1|0|$1|9|" },
            exec.Sent);
    }

    [Fact]
    public async Task AResultLessCommandStillSurfacesAnError()
    {
        var (ctx, _) = Target("-ERR no such key\r\n");

        await Assert.ThrowsAsync<RespException>(async () => await ctx.Lists.Trim("k", 0, 9));
    }

    [Fact]
    public async Task ReadsAndWritesLandInTheRightRetryCategories()
    {
        var (ctx, exec) = Target("*0\r\n", ":1\r\n", "$1\r\na\r\n");

        using (await ctx.Lists.Range("k")) { }
        await ctx.Lists.LeftPush("k", "a");
        await ctx.Lists.LeftPop("k");

        Assert.Equal(CommandFlags.CommandRetryReadOnly, exec.Flags[0] & Message.MaskRetryCategory);

        // a push compounds - two attempts leave two entries
        Assert.Equal(CommandFlags.CommandRetryWriteAccumulating, exec.Flags[1] & Message.MaskRetryCategory);

        // and so does a pop, in the other direction
        Assert.Equal(CommandFlags.CommandRetryWriteAccumulating, exec.Flags[2] & Message.MaskRetryCategory);
    }
}
