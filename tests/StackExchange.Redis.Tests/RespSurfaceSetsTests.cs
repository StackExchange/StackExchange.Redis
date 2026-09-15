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
/// What the set group puts on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks><inheritdoc cref="RespSurfaceStringsTests" path="/remarks"/></remarks>
public class RespSurfaceSetsTests
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
    public async Task OneMemberAndManyAreTheSameCommand()
    {
        var (ctx, exec) = Target();

        await ctx.Sets.Add("k", "a");
        await ctx.Sets.Add("k", ["a", "b"]);

        // unlike the string group's Get, the arity is the ONLY difference here - so the two overloads
        // exist for their return types (bool versus a count), not for the command
        Assert.Equal(
            new[] { "*3|$4|SADD|$1|k|$1|a|", "*4|$4|SADD|$1|k|$1|a|$1|b|" },
            exec.Sent);
    }

    [Fact]
    public async Task MembersAreValuesAndNotKeys()
    {
        var (ctx, exec) = Target();

        await ctx.WithKeyPrefix("t:").Sets.Remove("k", ["a", "b"]);

        Assert.Equal("*4|$4|SREM|$3|t:k|$1|a|$1|b|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task NothingToDoMeansNoCommand()
    {
        var (ctx, exec) = Target();

        Assert.Equal(0, await ctx.Sets.Add("k", ReadOnlySpan<RedisValue>.Empty));
        Assert.Equal(0, await ctx.Sets.Remove("k", ReadOnlySpan<RedisValue>.Empty));
        Assert.Empty((await ctx.Sets.Contains("k", ReadOnlySpan<RedisValue>.Empty)).Span.ToArray());

        Assert.Empty(exec.Sent);
    }

    [Fact]
    public async Task PopOfNoneRemovesNothing()
    {
        var (ctx, exec) = Target("*2\r\n$1\r\na\r\n$1\r\nb\r\n");

        // the old surface sends a bare SPOP for a count of zero, which removes ONE. Diverging here is
        // deliberate: "pop none" quietly popping one is discovered in production, not in review.
        Assert.Empty((await ctx.Sets.Pop("k", 0L)).Span.ToArray());
        Assert.Empty(exec.Sent);

        await ctx.Sets.Pop("k", 2);
        Assert.Equal("*3|$4|SPOP|$1|k|$1|2|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task ContainsHasASingularAndAPluralCommand()
    {
        var (ctx, exec) = Target(":1\r\n", "*2\r\n:1\r\n:0\r\n");

        Assert.True(await ctx.Sets.Contains("k", "a"));
        Assert.Equal(new[] { true, false }, (await ctx.Sets.Contains("k", ["a", "b"])).Span.ToArray());

        Assert.Equal(
            new[] { "*3|$9|SISMEMBER|$1|k|$1|a|", "*4|$10|SMISMEMBER|$1|k|$1|a|$1|b|" },
            exec.Sent);
    }

    [Fact]
    public async Task CombineTakesAnyNumberOfKeys()
    {
        var (ctx, exec) = Target("*1\r\n$1\r\na\r\n");

        RedisKey[] keys = ["s1", "s2", "s3"];
        await ctx.Sets.Combine(SetOperation.Union, keys);
        await ctx.Sets.Combine(SetOperation.Difference, ["s1"]);

        Assert.Equal(
            new[]
            {
                "*4|$6|SUNION|$2|s1|$2|s2|$2|s3|",
                "*2|$5|SDIFF|$2|s1|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task CombineAndStorePutsTheDestinationFirst()
    {
        var (ctx, exec) = Target();

        RedisKey[] keys = ["s1", "s2"];
        await ctx.Sets.CombineAndStore(SetOperation.Intersect, "dest", keys);

        Assert.Equal("*4|$11|SINTERSTORE|$4|dest|$2|s1|$2|s2|", Assert.Single(exec.Sent));
    }

    [Fact]
    public void CombineChecksItsArityBeforeTheServerDoes()
    {
        var (ctx, _) = Target();

        Assert.Throws<ArgumentException>(() => ctx.Sets.Combine(SetOperation.Union, ReadOnlySpan<RedisKey>.Empty));
        Assert.Throws<ArgumentException>(() => ctx.Sets.CombineAndStore(SetOperation.Union, "d", ReadOnlySpan<RedisKey>.Empty));
        Assert.Throws<ArgumentException>(() => ctx.Sets.CombineLength(SetOperation.Union, ReadOnlySpan<RedisKey>.Empty));
    }

    [Fact]
    public async Task CombineLengthCountsItsKeysForTheServer()
    {
        var (ctx, exec) = Target();

        RedisKey[] keys = ["s1", "s2"];
        await ctx.Sets.CombineLength(SetOperation.Intersect, keys);
        await ctx.Sets.CombineLength(SetOperation.Intersect, keys, limit: 10);
        await ctx.Sets.CombineLength(SetOperation.Union, keys, limit: 10, approximate: true);

        // numkeys comes FIRST here, unlike the plain combinations - the trailing LIMIT/APPROX operands are
        // exactly why the server has to be told where the key list stops
        Assert.Equal(
            new[]
            {
                "*4|$10|SINTERCARD|$1|2|$2|s1|$2|s2|",
                "*6|$10|SINTERCARD|$1|2|$2|s1|$2|s2|$5|LIMIT|$2|10|",
                "*7|$10|SUNIONCARD|$1|2|$2|s1|$2|s2|$6|APPROX|$5|LIMIT|$2|10|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task ALimitOfZeroIsNoLimitAndSoNoOperand()
    {
        var (ctx, exec) = Target();

        RedisKey[] keys = ["s1"];
        await ctx.Sets.CombineLength(SetOperation.Intersect, keys, limit: 0);

        // RespLimit writes two arguments or none; this is the "none", and it is why a fragment could not
        // have spelled it - the keyword is constant but the count is not
        Assert.Equal("*3|$10|SINTERCARD|$1|1|$2|s1|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task MoveNamesBothKeys()
    {
        var (ctx, exec) = Target();

        await ctx.WithKeyPrefix("t:").Sets.Move("src", "dst", "m");

        // both are keys, so both are prefixed; the member is not
        Assert.Equal("*4|$5|SMOVE|$5|t:src|$5|t:dst|$1|m|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task ReadsAndWritesLandInTheRightRetryCategories()
    {
        var (ctx, exec) = Target("*0\r\n", ":1\r\n", "$1\r\na\r\n");

        await ctx.Sets.Members("k");
        await ctx.Sets.Add("k", "a");
        await ctx.Sets.Pop("k");

        Assert.Equal(CommandFlags.CommandRetryReadOnly, exec.Flags[0] & Message.MaskRetryCategory);

        // SADD is idempotent - adding a member twice converges - so it is checked, not accumulating
        Assert.Equal(CommandFlags.CommandRetryWriteChecked, exec.Flags[1] & Message.MaskRetryCategory);

        // SPOP takes something away on every call, and a replay takes a DIFFERENT member
        Assert.Equal(CommandFlags.CommandRetryWriteAccumulating, exec.Flags[2] & Message.MaskRetryCategory);
    }
}
