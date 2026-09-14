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
/// The context-based surface: <c>target.Strings.Set(...)</c>, with nothing but a fake executor underneath.
/// See design notes section 9.4.
/// </summary>
public class RespSurfaceTests
{
    /// <summary>Records what was sent and replies from a script.</summary>
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public int Database => 0;

        /// <summary>The flags each request carried, so the tests can assert what reached the wire.</summary>
        public List<CommandFlags> Flags { get; } = [];

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            Flags.Add(request.Flags);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static RespDatabase Target(FakeExecutor executor, RespClientCache? cache = null)
        => new(new RespContext().WithExecutor(executor).WithCache(cache));

    [Fact]
    public async Task AResultLessSendStillSurfacesAServerError()
    {
        // the whole reason the result-less form reads the reply at all: with nothing returned, an error is
        // the ONLY thing the call can report, so discarding the reply would discard the failure too
        var executor = new FakeExecutor("-ERR no such key\r\n");
        var context = new RespContext().WithExecutor(executor);

        await Assert.ThrowsAsync<RespException>(
            async () => await context.SendAsync($"{RedisCommand.DEL}{(RedisKey)"k"}"));
    }

    [Fact]
    public async Task AResultLessSendSendsTheSameBytes()
    {
        var executor = new FakeExecutor(":1\r\n");
        var context = new RespContext().WithExecutor(executor);

        await context.SendAsync($"{RedisCommand.DEL}{(RedisKey)"k"}", CommandFlags.FireAndForget);

        Assert.Equal("*2|$3|DEL|$1|k|", Assert.Single(executor.Sent));
        Assert.Equal(CommandFlags.FireAndForget, Assert.Single(executor.Flags) & CommandFlags.FireAndForget);
    }

    [Fact]
    public void AResultLessSendThatCompletesSynchronouslyAllocatesNoTask()
    {
        // the generic overload promises a synchronous completion costs no state machine and no Task;
        // wrapping it in a plain `async ValueTask` would have quietly given that up
        var executor = new FakeExecutor(":1\r\n");
        var context = new RespContext().WithExecutor(executor);

        var pending = context.SendAsync($"{RedisCommand.DEL}{(RedisKey)"k"}");
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal(default, pending);
    }

    [Fact]
    public void AGroupStructCostsNothingOverTheContext()
    {
        // RespStrings holds exactly one RespContext, so it is the same size - the wrapper IS the pun, and
        // this is what says so. If it ever diverges, someone has added a field to a grouping type.
        Assert.Equal(
            System.Runtime.CompilerServices.Unsafe.SizeOf<RespContext>(),
            System.Runtime.CompilerServices.Unsafe.SizeOf<RespStrings>());

        // 48 bytes once ChannelPrefix moved into the service slot - it was 64, of which RedisChannel was
        // 16, carried on every copy for pub/sub's benefit alone. See design notes 3.3.
        Assert.Equal(48, System.Runtime.CompilerServices.Unsafe.SizeOf<RespContext>());
    }

    [Fact]
    public void ServicesComposeRatherThanReplaceEachOther()
    {
        using var cache = new RespClientCache();
        var ctx = new RespContext()
            .WithCache(cache)
            .WithChannelPrefix(RedisChannel.Literal("app:"));

        // the second service must not evict the first - the slot is a chain, not a variable
        Assert.Same(cache, ctx.Cache);
        Assert.Equal("app:", (string?)ctx.ChannelPrefix);

        // and the newest of a given type wins, by lookup order, with no replace logic
        var rebound = ctx.WithChannelPrefix(RedisChannel.Literal("other:"));
        Assert.Equal("other:", (string?)rebound.ChannelPrefix);
        Assert.Same(cache, rebound.Cache);

        // setting it back to nothing shadows rather than removes, and still reads as absent
        Assert.True(rebound.WithChannelPrefix(default).ChannelPrefix.IsNull);
    }

    [Fact]
    public void ChannelPrefixSurvivesUnrelatedClones()
    {
        var ctx = new RespContext().WithChannelPrefix(RedisChannel.Literal("app:")).WithDatabase(4).WithKeyPrefix("t7:");

        // it travels in services now, so every With* has to carry it without naming it
        Assert.Equal("app:", (string?)ctx.ChannelPrefix);
        Assert.Equal(4, ctx.Database);
    }

    [Fact]
    public async Task FlagsAreCumulativeRatherThanReplacing()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var target = Target(executor);

        // FireAndForget must not cost the command its retry category. It would have, when the category
        // lived in the parameter's DEFAULT value - passing any flag replaced it with nothing.
        await target.Strings.Set("k", "v", flags: CommandFlags.FireAndForget);

        var sent = Assert.Single(executor.Flags);
        Assert.Equal(CommandFlags.FireAndForget, sent & CommandFlags.FireAndForget);
        Assert.Equal(CommandFlags.CommandRetryWriteLastWins, sent & Message.MaskRetryCategory);
    }

    [Fact]
    public async Task AnExplicitCategoryStillWins()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var target = Target(executor);

        // WithRetryCategory is first-wins, so a caller who names one keeps it
        await target.Strings.Set("k", "v", flags: CommandFlags.CommandRetryNever);
        Assert.Equal(CommandFlags.CommandRetryNever, Assert.Single(executor.Flags) & Message.MaskRetryCategory);
    }

    [Fact]
    public async Task TheHandlerCanBeOmitted()
    {
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var ctx = new RespContext().WithExecutor(executor);

        // no handler named: resolved from TResult, which is what lets a command surface be one expression
        Assert.Equal("hello", await ctx.SendAsync<RedisValue>(
            $"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.CommandRetryReadOnly));
    }

    [Fact]
    public async Task AnUnregisteredResultTypeSaysSoAtTheCallSite()
    {
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var ctx = new RespContext().WithExecutor(executor);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ctx.SendAsync<Uri>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.CommandRetryReadOnly));
        Assert.Contains("Uri", ex.Message);
    }

    [Fact]
    public async Task SetAndGetThroughTheGroupedSurface()
    {
        var executor = new FakeExecutor("+OK\r\n", "$5\r\nhello\r\n");
        var target = Target(executor);

        Assert.True(await target.Strings.Set("mykey", "hello"));
        Assert.Equal("hello", await target.Strings.Get("mykey"));

        Assert.Equal(
            new[] { "*3|$3|SET|$5|mykey|$5|hello|", "*2|$3|GET|$5|mykey|" },
            executor.Sent);
    }

    [Fact]
    public async Task NullRepliesSurfaceAsRedisValueNull()
    {
        var target = Target(new FakeExecutor("$-1\r\n"));
        Assert.True((await target.Strings.Get("missing")).IsNull);
    }

    [Fact]
    public async Task TheContextItselfIsAlsoAnEntryPoint()
    {
        // both shapes exist: from the root object, and from a context someone already holds
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var ctx = new RespContext().WithExecutor(executor);
        Assert.Equal("hello", await ctx.Strings.Get("mykey"));
    }

    [Fact]
    public async Task WithKeyPrefixIsJustAContextClone()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var target = Target(executor);

        // this is the whole of KeyPrefixedDatabase's write half - no per-method forwarding
        var tenant = target.WithKeyPrefix("t7:");
        await tenant.Strings.Set("user:1", "marc");

        Assert.Equal("*3|$3|SET|$9|t7:user:1|$4|marc|", Assert.Single(executor.Sent));
    }

    [Fact]
    public async Task TheCacheServesASecondReadWithoutSending()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var target = Target(executor, cache);

        Assert.Equal("hello", await target.Strings.Get("mykey"));
        Assert.Equal("hello", await target.Strings.Get("mykey"));
        Assert.Single(executor.Sent); // the second read never reached the executor

        cache.OnInvalidate(Encoding.UTF8.GetBytes("mykey"));
        Assert.Equal("hello", await target.Strings.Get("mykey"));
        Assert.Equal(2, executor.Sent.Count);
    }

    [Fact]
    public async Task WritesAreNotCached()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("+OK\r\n");
        var target = Target(executor, cache);

        await target.Strings.Set("mykey", "hello");
        await target.Strings.Set("mykey", "hello");

        // SET defaults to a write retry category, which the flag gate rejects - so both were sent
        Assert.Equal(2, executor.Sent.Count);
        Assert.Equal(0, cache.Count);
        Assert.Equal(2, cache.RefusedByFlags);
    }

    [Fact]
    public async Task NoClientCacheOptsASingleCallOut()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var target = Target(executor, cache);

        await target.Strings.Get("mykey");
        await target.Strings.Get("mykey", CommandFlags.CommandRetryReadOnly | CommandFlags.NoClientCache);

        Assert.Equal(2, executor.Sent.Count); // the opted-out call did not read the cached entry
    }

    [Fact]
    public void ConnectionBackedTypesThrowForNow()
    {
        // IRedis carries the member, so IDatabase/IServer/ISubscriber all have it - but wiring it to a live
        // multiplexer is separate work, so those throw while RespDatabase is what actually runs
        IRespTarget target = (IRespTarget)(object)new RespDatabase(new RespContext());
        Assert.Equal(0, target.Context.Database); // the minimal one works
    }

    [Fact]
    public void MissingExecutorFailsLoudlyRatherThanSilently()
    {
        var target = new RespDatabase(new RespContext());
        Assert.Throws<InvalidOperationException>(() => target.Strings.Get("mykey"));
    }
}
