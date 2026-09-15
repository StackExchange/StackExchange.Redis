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
/// What the HyperLogLog group puts on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks><inheritdoc cref="RespSurfaceStringsTests" path="/remarks"/></remarks>
public class RespSurfaceHyperLogLogTests
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

    private sealed class FakeFeatures(RedisFeatures features) : IRespServerFeatures
    {
        public bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures result)
        {
            result = features;
            return true;
        }
    }

    private static (RespContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? [":1\r\n"] : replies);
        return (new RespContext().WithExecutor(executor), executor);
    }

    private static RespContext Server(in RespContext context, int major, int minor, int build)
        => context.WithServices(new FakeFeatures(new RedisFeatures(new Version(major, minor, build))));

    [Fact]
    public async Task AddTakesOneElementOrMany()
    {
        var (ctx, exec) = Target();

        await ctx.HyperLogLog.Add("k", "a");
        await ctx.HyperLogLog.Add("k", ["a", "b"]);

        Assert.Equal(
            new[] { "*3|$5|PFADD|$1|k|$1|a|", "*4|$5|PFADD|$1|k|$1|a|$1|b|" },
            exec.Sent);
    }

    [Fact]
    public async Task AddingNothingStillAsksTheServer()
    {
        var (ctx, exec) = Target();

        // unlike SADD, an arity-zero PFADD is a real request: it creates the structure if absent and says
        // whether it had to. There is an answer, so there is nothing to invent by short-circuiting.
        await ctx.HyperLogLog.Add("k", ReadOnlySpan<RedisValue>.Empty);

        Assert.Equal("*2|$5|PFADD|$1|k|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task CountAndMergeTakeTheirKeys()
    {
        var (ctx, exec) = Target(":7\r\n", ":9\r\n", "+OK\r\n");

        Assert.Equal(7, await ctx.HyperLogLog.Length("k"));
        Assert.Equal(9, await ctx.HyperLogLog.Length(["a", "b"]));
        await ctx.HyperLogLog.Merge("dst", ["a", "b"]);

        Assert.Equal(
            new[]
            {
                "*2|$7|PFCOUNT|$1|k|",
                "*3|$7|PFCOUNT|$1|a|$1|b|",
                "*4|$7|PFMERGE|$3|dst|$1|a|$1|b|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task CountIsPrimaryOnlyOnAServerWhereItWrites()
    {
        var (bare, exec) = Target(":7\r\n", ":7\r\n");

        // PFCOUNT caches the cardinality back into the key, so before 2.8.18 it is a write however much
        // it reads - which means a replica cannot serve it, whatever the caller preferred
        await Server(bare, 2, 8, 0).HyperLogLog.Length("k", CommandFlags.PreferReplica);
        await Server(bare, 2, 8, 0).HyperLogLog.Length(["a", "b"], CommandFlags.PreferReplica);

        Assert.All(exec.Flags, f => Assert.Equal(CommandFlags.DemandMaster, Message.GetPrimaryReplicaFlags(f)));
    }

    [Fact]
    public async Task CountLeavesRoutingAloneWhenItIsSafeOrUnknown()
    {
        var (bare, exec) = Target(":7\r\n", ":7\r\n");

        await Server(bare, 2, 8, 18).HyperLogLog.Length("k", CommandFlags.PreferReplica);

        // and with no probe at all, the caller's routing stands: the old surface demotes only when it
        // actually selected a server, so "not sure" has to leave this alone rather than guess
        await bare.HyperLogLog.Length("k", CommandFlags.PreferReplica);

        Assert.All(exec.Flags, f => Assert.Equal(CommandFlags.PreferReplica, Message.GetPrimaryReplicaFlags(f)));
    }

    [Fact]
    public void DemandingAReplicaForAWritingCountIsAnError()
    {
        var (bare, exec) = Target(":7\r\n");

        // the one case that cannot be honoured: the caller demanded a replica and the command may not go
        // to one. Overriding silently would send the write somewhere they excluded on purpose.
        var ex = Assert.Throws<RedisCommandException>(
            () => Server(bare, 2, 8, 0).HyperLogLog.Length("k", CommandFlags.DemandReplica));

        Assert.Contains("Command cannot be issued to a replica", ex.Message);
        Assert.Empty(exec.Sent);
    }

    [Fact]
    public async Task TheGroupLandsInTheRightRetryCategories()
    {
        var (ctx, exec) = Target(":1\r\n", ":7\r\n", "+OK\r\n");

        await ctx.HyperLogLog.Add("k", "a");
        await ctx.HyperLogLog.Length("k");
        await ctx.HyperLogLog.Merge("dst", ["a"]);

        // PFADD sits with SADD and HDEL: a replay adds the same elements again, and the set of observed
        // elements is unchanged - only the "did this alter the estimate" answer can differ
        Assert.Equal(CommandFlags.CommandRetryWriteChecked, exec.Flags[0] & Message.MaskRetryCategory);
        Assert.Equal(CommandFlags.CommandRetryReadOnly, exec.Flags[1] & Message.MaskRetryCategory);

        // PFMERGE folds the destination into the union too, so each call accumulates
        Assert.Equal(CommandFlags.CommandRetryWriteAccumulating, exec.Flags[2] & Message.MaskRetryCategory);
    }
}
