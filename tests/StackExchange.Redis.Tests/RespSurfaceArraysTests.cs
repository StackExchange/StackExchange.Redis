﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The array group: what goes on the wire.
/// </summary>
/// <remarks>
/// Two things worth pinning here. The element types render <i>themselves</i>, so a span of indices, entries
/// or ranges goes through one generic hole rather than an overload per type - these check the resulting
/// argument counts. And the optional operands must write <b>nothing</b> when absent: a
/// <see cref="RedisValue"/> hole cannot express that, because it always writes and counts, so an omitted
/// operand would become an empty argument and the server would read a different command.
/// </remarks>
public class RespSurfaceArraysTests
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

    private static (RespContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? [":1\r\n"] : replies);
        return (new RespContext().WithExecutor(executor), executor);
    }

    [Fact]
    public async Task TheSingleSlotCommandsRender()
    {
        var (ctx, exec) = Target();

        await ctx.Arrays.SetAsync("a", new RedisArrayIndex(3), "v");
        await ctx.Arrays.GetAsync("a", new RedisArrayIndex(3));
        await ctx.Arrays.DeleteAsync("a", new RedisArrayIndex(3));
        await ctx.Arrays.SeekAsync("a", new RedisArrayIndex(3));
        await ctx.Arrays.LengthAsync("a");
        await ctx.Arrays.CountAsync("a");
        await ctx.Arrays.NextAsync("a");
        await ctx.Arrays.InsertAsync("a", "v");

        Assert.Equal(
            new[]
            {
                "*4|$5|ARSET|$1|a|$1|3|$1|v|",
                "*3|$5|ARGET|$1|a|$1|3|",
                "*3|$5|ARDEL|$1|a|$1|3|",
                "*3|$6|ARSEEK|$1|a|$1|3|",
                "*2|$5|ARLEN|$1|a|",
                "*2|$7|ARCOUNT|$1|a|",
                "*2|$6|ARNEXT|$1|a|",
                "*3|$8|ARINSERT|$1|a|$1|v|",
            },
            exec.Sent);
    }

    /// <summary>A span of a self-rendering element type becomes one argument per part, in order.</summary>
    [Fact]
    public async Task SpansOfElementTypesRenderThemselves()
    {
        // ARMGET answers an array; the other two answer integers
        var (ctx, exec) = Target("*0\r\n", ":1\r\n", ":1\r\n");

        await ctx.Arrays.GetAsync("a", [new RedisArrayIndex(1), new RedisArrayIndex(2)]);
        await ctx.Arrays.SetAsync("a", [new RedisArrayEntry(new RedisArrayIndex(1), "x"), new RedisArrayEntry(new RedisArrayIndex(2), "y")]);
        await ctx.Arrays.DeleteRangeAsync("a", [new RedisArrayRange(new RedisArrayIndex(1), new RedisArrayIndex(5))]);

        Assert.Equal(
            new[]
            {
                "*4|$6|ARMGET|$1|a|$1|1|$1|2|",           // one argument per index
                "*6|$5|ARSET|$1|a|$1|1|$1|x|$1|2|$1|y|",  // two per entry, index then value
                "*4|$10|ARDELRANGE|$1|a|$1|1|$1|5|",      // two per range, start then end
            },
            exec.Sent);
    }

    /// <summary>
    /// An absent operand writes <b>nothing</b> - not an empty argument.
    /// </summary>
    /// <remarks>
    /// The first draft of this group spelled both of these as conditional <see cref="RedisValue"/> holes,
    /// which is wrong in a way nothing would have caught at run time: the frame stays well-formed, the
    /// server accepts it, and it reads <c>ARSCAN key 1 9 ""</c> as a different request. The argument count
    /// in the header is the assertion that matters.
    /// </remarks>
    [Fact]
    public async Task AbsentOperandsWriteNothing()
    {
        // scans and ARLASTITEMS and ARINFO answer aggregates; AROP answers a scalar
        var (ctx, exec) = Target(
            "*0\r\n", "*0\r\n", ":1\r\n", ":1\r\n", "*0\r\n", "*0\r\n", "*0\r\n", "*0\r\n");

        await ctx.Arrays.ScanAsync("a", new RedisArrayIndex(1), new RedisArrayIndex(9));
        await ctx.Arrays.ScanAsync("a", new RedisArrayIndex(1), new RedisArrayIndex(9), limit: 7);
        await ctx.Arrays.OperationAsync("a", new RedisArrayIndex(1), new RedisArrayIndex(9), ArrayOperation.Sum);
        await ctx.Arrays.OperationAsync("a", new RedisArrayIndex(1), new RedisArrayIndex(9), ArrayOperation.Match, "needle");
        await ctx.Arrays.LastItemsAsync("a", 3);
        await ctx.Arrays.LastItemsAsync("a", 3, reverse: true);
        await ctx.Arrays.InfoAsync("a");
        await ctx.Arrays.InfoAsync("a", full: true);

        Assert.Equal(
            new[]
            {
                "*4|$6|ARSCAN|$1|a|$1|1|$1|9|",
                "*6|$6|ARSCAN|$1|a|$1|1|$1|9|$5|LIMIT|$1|7|",
                "*5|$4|AROP|$1|a|$1|1|$1|9|$3|SUM|",
                "*6|$4|AROP|$1|a|$1|1|$1|9|$5|MATCH|$6|needle|",
                "*3|$11|ARLASTITEMS|$1|a|$1|3|",
                "*4|$11|ARLASTITEMS|$1|a|$1|3|$3|REV|",
                "*2|$6|ARINFO|$1|a|",
                "*3|$6|ARINFO|$1|a|$4|FULL|",
            },
            exec.Sent);
    }

    /// <summary>An operand belongs to Match and to nothing else, and that is checked before sending.</summary>
    [Fact]
    public void TheOperandIsRejectedWhereItMeansNothing()
    {
        var (ctx, exec) = Target();

        Assert.Throws<ArgumentException>(() => ctx.Arrays.OperationAsync("a", default, default, ArrayOperation.Sum, "x"));
        Assert.Throws<ArgumentException>(() => ctx.Arrays.OperationAsync("a", default, default, ArrayOperation.Match));
        Assert.Empty(exec.Sent);
    }

    /// <summary>An empty span is not a command: the server would reject the arity, and the answer is nothing.</summary>
    [Fact]
    public async Task EmptySpansSendNothing()
    {
        var (ctx, exec) = Target();

        Assert.Equal(0, await ctx.Arrays.SetAsync("a", new RedisArrayIndex(1), ReadOnlySpan<RedisValue>.Empty));
        Assert.Equal(0, await ctx.Arrays.DeleteAsync("a", ReadOnlySpan<RedisArrayIndex>.Empty));
        using (var empty = await ctx.Arrays.GetAsync("a", ReadOnlySpan<RedisArrayIndex>.Empty))
        {
            Assert.Equal(0, empty.Length);
        }

        Assert.Empty(exec.Sent);
    }
}
