using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What the sorted-set group puts on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks>
/// <inheritdoc cref="RespSurfaceStringsTests" path="/remarks"/>
/// <para>
/// This group needs them most. Nearly every command has formatting the caller never sees - a <c>(</c> on
/// an exclusive bound, a <c>[</c> on an inclusive lexical one, bounds that swap when the direction and the
/// numbers disagree, operands that vanish at their defaults - and a server will happily accept most of the
/// ways of getting that wrong, returning a plausible answer to a different question.
/// </para>
/// </remarks>
public class RespSurfaceSortedSetsTests
{
    private static (RespDatabaseContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? [":1\r\n"] : replies);
        return (new RespDatabaseContext(new RespContext().WithExecutor(executor)), executor);
    }

    [Fact]
    public async Task AnEntryWritesScoreThenElement()
    {
        var (ctx, exec) = Target();

        SortedSetEntry[] entries = [new("a", 1), new("b", 2)];
        await ctx.SortedSets.AddAsync("k", entries);

        // the reverse of how SortedSetEntry reads, and the order ZADD wants; getting this backwards
        // produces a command the server accepts and misinterprets
        Assert.Equal("*6|$4|ZADD|$1|k|$1|1|$1|a|$1|2|$1|b|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task AddRendersItsOptionsInTheDocumentedOrder()
    {
        var (ctx, exec) = Target();

        await ctx.SortedSets.AddAsync("k", "m", 1);
        await ctx.SortedSets.AddAsync("k", "m", 1, SortedSetWhen.GreaterThan, change: true);
        await ctx.SortedSets.AddAsync("k", "m", 1, SortedSetWhen.NotExists);

        Assert.Equal(
            new[]
            {
                "*4|$4|ZADD|$1|k|$1|1|$1|m|",
                "*6|$4|ZADD|$1|k|$2|GT|$2|CH|$1|1|$1|m|",
                "*5|$4|ZADD|$1|k|$2|NX|$1|1|$1|m|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task AddsRetryCategoryDependsOnItsOptions()
    {
        var (ctx, exec) = Target();

        await ctx.SortedSets.AddAsync("k", "m", 1);
        await ctx.SortedSets.AddAsync("k", "m", 1, SortedSetWhen.Exists);
        await ctx.SortedSets.IncrementAsync("k", "m", 1, SortedSetWhen.Exists);
        await ctx.SortedSets.IncrementAsync("k", "m", 1, SortedSetWhen.NotExists);

        // a bare ZADD overwrites, so last-wins; NX/XX/GT/LT make a replay converge, so checked; INCR
        // compounds - unless NX, where a replay can only find the member present and no-op
        Assert.Equal(CommandFlags.CommandRetryWriteLastWins, exec.Flags[0] & Message.MaskRetryCategory);
        Assert.Equal(CommandFlags.CommandRetryWriteChecked, exec.Flags[1] & Message.MaskRetryCategory);
        Assert.Equal(CommandFlags.CommandRetryWriteAccumulating, exec.Flags[2] & Message.MaskRetryCategory);
        Assert.Equal(CommandFlags.CommandRetryWriteChecked, exec.Flags[3] & Message.MaskRetryCategory);
    }

    [Fact]
    public async Task AnUnconditionalIncrementIsTheShorterCommand()
    {
        var (ctx, exec) = Target("$1\r\n5\r\n");

        await ctx.SortedSets.IncrementAsync("k", "m", 5);
        await ctx.SortedSets.IncrementAsync("k", "m", 5, SortedSetWhen.Exists);

        Assert.Equal(
            new[]
            {
                "*4|$7|ZINCRBY|$1|k|$1|5|$1|m|",
                "*6|$4|ZADD|$1|k|$2|XX|$4|INCR|$1|5|$1|m|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task AnUnboundedLengthIsADifferentCommand()
    {
        var (ctx, exec) = Target();

        await ctx.SortedSets.LengthAsync("k");
        await ctx.SortedSets.LengthAsync("k", 1, 10);
        await ctx.SortedSets.LengthAsync("k", 1, 10, Exclude.Start);

        // the whole set is what ZCARD answers without walking anything; and an exclusive bound is a '('
        Assert.Equal(
            new[]
            {
                "*2|$5|ZCARD|$1|k|",
                "*4|$6|ZCOUNT|$1|k|$1|1|$2|10|",
                "*4|$6|ZCOUNT|$1|k|$2|(1|$2|10|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task AScoreRangeIsPutIntoLowThenHighOrder()
    {
        var (ctx, exec) = Target("*0\r\n");

        await ctx.SortedSets.RangeByScoreAsync("k", 1, 10);
        await ctx.SortedSets.RangeByScoreAsync("k", 10, 1, order: Order.Descending);
        await ctx.SortedSets.RangeByScoreAsync("k", 10, 1, Exclude.Start, Order.Descending);
        await ctx.SortedSets.RangeByScoreAsync("k", 1, 10, Exclude.Start, Order.Descending);

        // each command wants its bounds in the direction it walks - ascending low-then-high, descending
        // high-then-low - so a caller who wrote them the other way round gets them swapped, and the
        // exclusivity swaps with them or the range quietly moves by one member. The first three are
        // already in the right order for their command and pass through; the fourth is not.
        Assert.Equal(
            new[]
            {
                "*4|$13|ZRANGEBYSCORE|$1|k|$1|1|$2|10|",
                "*4|$16|ZREVRANGEBYSCORE|$1|k|$2|10|$1|1|",
                "*4|$16|ZREVRANGEBYSCORE|$1|k|$3|(10|$1|1|",
                "*4|$16|ZREVRANGEBYSCORE|$1|k|$2|10|$2|(1|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task AWindowIsOnlyWrittenWhenOneWasAskedFor()
    {
        var (ctx, exec) = Target("*0\r\n");

        await ctx.SortedSets.RangeByScoreAsync("k", 1, 10);
        await ctx.SortedSets.RangeByScoreAsync("k", 1, 10, skip: 5, take: 2);
        await ctx.SortedSets.RangeByScoreWithScoresAsync("k", 1, 10, skip: 5, take: 2);

        Assert.Equal(
            new[]
            {
                "*4|$13|ZRANGEBYSCORE|$1|k|$1|1|$2|10|",
                "*7|$13|ZRANGEBYSCORE|$1|k|$1|1|$2|10|$5|LIMIT|$1|5|$1|2|",
                "*8|$13|ZRANGEBYSCORE|$1|k|$1|1|$2|10|$10|WITHSCORES|$5|LIMIT|$1|5|$1|2|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task AnOpenLexicalBoundFlipsWithTheDirection()
    {
        var (ctx, exec) = Target("*0\r\n");

        await ctx.SortedSets.RangeByValueAsync("k");
        await ctx.SortedSets.RangeByValueAsync("k", order: Order.Descending);
        await ctx.SortedSets.RangeByValueAsync("k", "a", "j");
        await ctx.SortedSets.RangeByValueAsync("k", "a", "j", Exclude.Both);

        // '-' and '+' are chosen by the ORDER, not by the position; the bounds themselves stay in
        // start-then-stop order even for the reversed command
        Assert.Equal(
            new[]
            {
                "*4|$11|ZRANGEBYLEX|$1|k|$1|-|$1|+|",
                "*4|$14|ZREVRANGEBYLEX|$1|k|$1|+|$1|-|",
                "*4|$11|ZRANGEBYLEX|$1|k|$2|[a|$2|[j|",
                "*4|$11|ZRANGEBYLEX|$1|k|$2|(a|$2|(j|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task RangeAndStoreBracketsAnInclusiveLexicalBound()
    {
        var (ctx, exec) = Target();

        await ctx.SortedSets.RangeAndStoreAsync("src", "dst", 0, -1);
        await ctx.SortedSets.RangeAndStoreAsync("src", "dst", "a", "j", SortedSetOrder.ByLex);
        await ctx.SortedSets.RangeAndStoreAsync("src", "dst", 1, 10, SortedSetOrder.ByScore);
        await ctx.SortedSets.RangeAndStoreAsync("src", "dst", "a", "j", SortedSetOrder.ByLex, take: 3);

        // the destination comes FIRST, and a by-lex bound is bracketed where a by-score one is bare -
        // an asymmetry that belongs to the server and is exactly what gets "tidied" into a bug
        Assert.Equal(
            new[]
            {
                "*5|$11|ZRANGESTORE|$3|dst|$3|src|$1|0|$2|-1|",
                "*6|$11|ZRANGESTORE|$3|dst|$3|src|$2|[a|$2|[j|$5|BYLEX|",
                "*6|$11|ZRANGESTORE|$3|dst|$3|src|$1|1|$2|10|$7|BYSCORE|",
                "*9|$11|ZRANGESTORE|$3|dst|$3|src|$2|[a|$2|[j|$5|BYLEX|$5|LIMIT|$1|0|$1|3|",
            },
            exec.Sent);
    }

    [Fact]
    public void RangeAndStoreRefusesOperandsItHasNowhereToPut()
    {
        var (ctx, _) = Target();

        // by rank the server has no operand for either, so dropping them silently would store a
        // different range than was asked for
        Assert.Throws<ArgumentException>(() => ctx.SortedSets.RangeAndStoreAsync("s", "d", 0, -1, take: 3));
        Assert.Throws<ArgumentException>(() => ctx.SortedSets.RangeAndStoreAsync("s", "d", 0, -1, exclude: Exclude.Start));
    }

    [Fact]
    public async Task CombinationsCountTheirKeysAndDropTheirDefaults()
    {
        var (ctx, exec) = Target("*0\r\n", "*0\r\n", "*0\r\n", ":0\r\n");

        RedisKey[] keys = ["a", "b"];
        await ctx.SortedSets.CombineAsync(SetOperation.Union, keys);
        await ctx.SortedSets.CombineAsync(SetOperation.Union, keys, [1, 2]);
        await ctx.SortedSets.CombineWithScoresAsync(SetOperation.Intersect, keys, default, Aggregate.Max);
        await ctx.SortedSets.CombineAndStoreAsync(SetOperation.Union, "dest", keys, [1, 2], Aggregate.Count);

        // SUM is the server's own default and renders as nothing; the STORE form puts its destination
        // before the count, which is the one place the key list is not what follows numkeys
        Assert.Equal(
            new[]
            {
                "*4|$6|ZUNION|$1|2|$1|a|$1|b|",
                "*7|$6|ZUNION|$1|2|$1|a|$1|b|$7|WEIGHTS|$1|1|$1|2|",
                "*7|$6|ZINTER|$1|2|$1|a|$1|b|$9|AGGREGATE|$3|MAX|$10|WITHSCORES|",
                "*10|$11|ZUNIONSTORE|$4|dest|$1|2|$1|a|$1|b|$7|WEIGHTS|$1|1|$1|2|$9|AGGREGATE|$5|COUNT|",
            },
            exec.Sent);
    }

    [Fact]
    public void CombinationsRejectWhatTheCommandCannotCarry()
    {
        var (ctx, _) = Target();
        RedisKey[] keys = ["a", "b"];

        // the message names whichever command was asked for, as the old surface does
        var diff = Assert.Throws<ArgumentException>(() => ctx.SortedSets.CombineAsync(SetOperation.Difference, keys, [1, 2]));
        Assert.StartsWith("ZDIFF ", diff.Message);

        var store = Assert.Throws<ArgumentException>(() => ctx.SortedSets.CombineAndStoreAsync(SetOperation.Difference, "d", keys, [1, 2]));
        Assert.StartsWith("ZDIFFSTORE ", store.Message);

        Assert.Throws<ArgumentException>(() => ctx.SortedSets.CombineAsync(SetOperation.Union, keys, [1]));
        Assert.Throws<ArgumentException>(() => ctx.SortedSets.CombineAsync(SetOperation.Union, ReadOnlySpan<RedisKey>.Empty));
    }

    [Fact]
    public async Task PopsNameTheirEnd()
    {
        var (ctx, exec) = Target("*2\r\n$1\r\na\r\n$1\r\n1\r\n");

        await ctx.SortedSets.PopAsync("k");
        await ctx.SortedSets.PopAsync("k", 2, Order.Descending);

        Assert.Equal(
            new[] { "*2|$7|ZPOPMIN|$1|k|", "*3|$7|ZPOPMAX|$1|k|$1|2|" },
            exec.Sent);
    }

    [Fact]
    public async Task PoppingNoneAsksNobody()
    {
        var (ctx, exec) = Target();

        // `count: 0` rather than a bare 0, because Order is an enum and so a literal zero is ambiguous
        // between the two overloads - a wart this surface inherits from the pair it replaces
        Assert.Empty((await ctx.SortedSets.PopAsync("k", count: 0)).Span.ToArray());
        Assert.Empty(exec.Sent);
    }

    [Fact]
    public async Task MultiPopCountsItsKeysAndNamesItsEnd()
    {
        var (ctx, exec) = Target("*-1\r\n");

        RedisKey[] keys = ["a", "b"];
        await ctx.SortedSets.PopAsync(keys, 3, Order.Descending);

        Assert.Equal("*7|$5|ZMPOP|$1|2|$1|a|$1|b|$3|MAX|$5|COUNT|$1|3|", Assert.Single(exec.Sent));
    }

    [Fact]
    public void MultiPopNeedsAKey()
    {
        var (ctx, _) = Target();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ctx.SortedSets.PopAsync(ReadOnlySpan<RedisKey>.Empty, 1));
        Assert.Contains("keys must have a size of at least 1", ex.Message);
    }

    [Fact]
    public async Task ScoresComeBackNullableBecauseAMemberMayBeAbsent()
    {
        var (ctx, exec) = Target("*3\r\n$1\r\n1\r\n$-1\r\n$1\r\n3\r\n");

        RedisValue[] members = ["a", "b", "c"];
        Assert.Equal(new double?[] { 1, null, 3 }, (await ctx.SortedSets.ScoresAsync("k", members)).Span.ToArray());

        Assert.Equal("*5|$7|ZMSCORE|$1|k|$1|a|$1|b|$1|c|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task IntersectionLengthCarriesItsLimitOrNothing()
    {
        var (ctx, exec) = Target();

        RedisKey[] keys = ["a", "b"];
        await ctx.SortedSets.CombineLengthAsync(keys);
        await ctx.SortedSets.CombineLengthAsync(keys, limit: 7);

        Assert.Equal(
            new[]
            {
                "*4|$10|ZINTERCARD|$1|2|$1|a|$1|b|",
                "*6|$10|ZINTERCARD|$1|2|$1|a|$1|b|$5|LIMIT|$1|7|",
            },
            exec.Sent);
    }
}
