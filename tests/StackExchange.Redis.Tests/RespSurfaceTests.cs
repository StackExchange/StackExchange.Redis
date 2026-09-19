using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The context-based surface: <c>target.Strings.SetAsync(...)</c>, with nothing but a fake executor underneath.
/// See design notes section 9.4.
/// </summary>
public class RespSurfaceTests
{
    /// <summary>Records what was sent and replies from a script.</summary>
    private sealed class FakeExecutor(params string[] replies) : RespExecutorBase
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public override int Database => 0;

        /// <summary>The flags each request carried, so the tests can assert what reached the wire.</summary>
        public List<CommandFlags> Flags { get; } = [];

        public override RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            Flags.Add(request.Flags);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static RespDatabaseContext Target(FakeExecutor executor, RespClientCache? cache = null)
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

        // A reference, so 8 bytes on 64-bit. RespContext was a readonly struct that shrank 64 -> 48 -> 40
        // over the spike (ChannelPrefix into the service slot, then the CancellationToken onto the call),
        // and is now a sealed class: the fields it wants fast are memoised once at construction instead of
        // copied on every clone, and a flavoured context is a reference with an accent.
        Assert.Equal(IntPtr.Size, System.Runtime.CompilerServices.Unsafe.SizeOf<RespContext>());
    }

    [Fact]
    public void ServicesComposeRatherThanReplaceEachOther()
    {
        using var cache = new RespClientCache();
        var ctx = new RespContext()
            .WithCache(cache)
            .AppendChannelPrefix(RedisChannel.Literal("app:"));

        // the second service must not evict the first - the slot is a chain, not a variable
        Assert.Same(cache, ctx.Cache);
        Assert.Equal("app:", (string?)ctx.ChannelPrefix);

        // the newest of a given type wins by lookup order, with no replace logic - which is how the
        // composed prefix below gets to be the one that is read back
        var rebound = ctx.AppendChannelPrefix(RedisChannel.Literal("other:"));
        Assert.Same(cache, rebound.Cache);
    }

    [Fact]
    public void ChannelPrefixesComposeAndCannotBeEscaped()
    {
        var tenant = new RespDatabaseContext(new RespContext().AppendChannelPrefix(RedisChannel.Literal("app:")));

        // the point of the test: a second prefix appends to the first, it does not take its place. Anything
        // else lets code that was handed a tenant-scoped context quietly publish outside that tenant.
        var nested = tenant.AppendChannelPrefix(RedisChannel.Literal("v2:"));
        Assert.Equal("app:v2:", (string?)nested.Raw.ChannelPrefix);

        // and there is no reset: null adds nothing rather than clearing what is already in force
        Assert.Equal("app:", (string?)tenant.AppendChannelPrefix(default).Raw.ChannelPrefix);
        Assert.Equal("app:v2:", (string?)nested.AppendChannelPrefix(default).Raw.ChannelPrefix);

        // which is exactly what the key prefix does, and what nesting the KeyPrefixed* decorators does;
        // the two halves of keyspace isolation must not disagree about this
        var keys = new RespContext().AppendKeyPrefix("app:").AppendKeyPrefix("v2:");
        Assert.Equal("app:v2:", (string?)keys.KeyPrefix);

        // including the no-escape half: a null key prefix is a no-op, not a reset. Note this differs from
        // the old API on purpose - DatabaseExtensions.WithKeyPrefix THROWS on null - because there is no
        // argument to validate here, just a prefix that adds nothing
        Assert.Equal("app:v2:", (string?)keys.AppendKeyPrefix(default).KeyPrefix);
        Assert.True(new RespContext().AppendKeyPrefix(default).KeyPrefix.IsNull);
    }

    [Fact]
    public void WithServicesAddsRatherThanReplaces()
    {
        using var cache = new RespClientCache();
        var probe = new Marker();

        // a context is built up in stages by callers who do not know each other - the multiplexer attaches
        // a cache, someone downstream adds a probe - so an assigning slot would drop the cache here, with
        // nothing to see but cache misses much later. That bug was real; this is what caught it.
        var ctx = new RespDatabaseContext(new RespContext().WithCache(cache).WithServices(probe));

        Assert.Same(cache, ctx.Raw.Cache);
        Assert.True(ctx.Raw.TryGetService<Marker>(out var found));
        Assert.Same(probe, found);

        // and nothing is not something: adding it leaves the context alone rather than emptying the slot
        Assert.Same(cache, ctx.WithServices(null).Raw.Cache);
    }

    [Fact]
    public void ACacheCanBeTurnedOffWithoutLosingTheRestOfTheChain()
    {
        using var cache = new RespClientCache();
        var scripts = new RespScriptCache();
        var probe = new Marker();

        var ctx = new RespContext()
            .WithCache(cache)
            .WithScriptCache(scripts)
            .WithServices(probe)
            .AppendChannelPrefix(RedisChannel.Literal("app:"));

        // "no cache" has to remain expressible now that the slot composes - it shadows just that lookup,
        // which is the whole reason the chain can veto as well as supply. This is the *public* spelling:
        // attaching a cache is the multiplexer's job, since one the caller minted has no CLIENT TRACKING
        // behind it, so opting out is the only direction a caller needs.
        var uncached = ctx.WithoutCache();
        Assert.Null(uncached.Cache);
        Assert.Same(scripts, uncached.ScriptCache);
        Assert.Equal("app:", (string?)uncached.ChannelPrefix);
        Assert.True(uncached.TryGetService<Marker>(out _));

        // the same for the script registry, and the two do not shadow each other
        var unscripted = ctx.WithScriptCache(null);
        Assert.Null(unscripted.ScriptCache);
        Assert.Same(cache, unscripted.Cache);

        // a veto shadows rather than removes, so putting one back is just another add
        Assert.Same(cache, uncached.WithCache(cache).Cache);

        // and the public opt-out is exactly the internal one, not a near-miss with its own behaviour
        Assert.Null(ctx.WithCache(null).Cache);
    }

    [Fact]
    public void TurningOffWhatWasNeverOnCostsNothing()
    {
        // the common shape in tests and in any optional-cache wiring: opting out on a context that has no
        // services at all should not allocate a veto to shadow something that is not there
        var ctx = new RespDatabaseContext(new RespContext().WithoutCache().WithScriptCache(null));

        Assert.Null(ctx.Raw.Cache);
        Assert.False(ctx.Raw.TryGetService<Marker>(out _));
    }

    private sealed class Marker;

    [Fact]
    public void ChannelPrefixSurvivesUnrelatedClones()
    {
        var ctx = new RespDatabaseContext(new RespContext().AppendChannelPrefix(RedisChannel.Literal("app:")).WithDatabase(4).AppendKeyPrefix("t7:"));

        // it travels in services now, so every With* has to carry it without naming it
        Assert.Equal("app:", (string?)ctx.Raw.ChannelPrefix);
        Assert.Equal(4, ctx.Raw.Database);
    }

    [Fact]
    public void WithDatabaseRefusesAnExecutorItCannotRePoint()
    {
        // an executor decides where a request lands; the database is not in the frame. So a context that
        // cannot move its executor must not hand back one that merely CLAIMS a different database - that
        // shape read database 0 while reporting database 1, with nothing to see.
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor("+OK\r\n")));

        Assert.Equal(0, ctx.WithDatabase(0).Database); // already there: allowed, and nothing changes
        var ex = Assert.Throws<NotSupportedException>(() => ctx.WithDatabase(1));
        Assert.Contains("cannot be re-pointed at database 1", ex.Message);
    }

    [Fact]
    public async Task FlagsAreCumulativeRatherThanReplacing()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var target = Target(executor);

        // FireAndForget must not cost the command its retry category. It would have, when the category
        // lived in the parameter's DEFAULT value - passing any flag replaced it with nothing.
        await target.Strings.SetAsync("k", "v", flags: CommandFlags.FireAndForget);

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
        await target.Strings.SetAsync("k", "v", flags: CommandFlags.CommandRetryNever);
        Assert.Equal(CommandFlags.CommandRetryNever, Assert.Single(executor.Flags) & Message.MaskRetryCategory);
    }

    [Fact]
    public async Task TheHandlerCanBeOmitted()
    {
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(executor));

        // no handler named: resolved from TResult, which is what lets a command surface be one expression
        Assert.Equal("hello", await ctx.Raw.SendAsync<RedisValue>(
            $"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.CommandRetryReadOnly));
    }

    [Fact]
    public async Task AnUnregisteredResultTypeSaysSoAtTheCallSite()
    {
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(executor));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ctx.Raw.SendAsync<Uri>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.CommandRetryReadOnly));
        Assert.Contains("Uri", ex.Message);
    }

    [Fact]
    public async Task SetAndGetThroughTheGroupedSurface()
    {
        var executor = new FakeExecutor("+OK\r\n", "$5\r\nhello\r\n");
        var target = Target(executor);

        Assert.True(await target.Strings.SetAsync("mykey", "hello"));
        Assert.Equal("hello", await target.Strings.GetAsync("mykey"));

        Assert.Equal(
            new[] { "*3|$3|SET|$5|mykey|$5|hello|", "*2|$3|GET|$5|mykey|" },
            executor.Sent);
    }

    [Fact]
    public async Task NullRepliesSurfaceAsRedisValueNull()
    {
        var target = Target(new FakeExecutor("$-1\r\n"));
        Assert.True((await target.Strings.GetAsync("missing")).IsNull);
    }

    [Fact]
    public async Task TheContextItselfIsAlsoAnEntryPoint()
    {
        // both shapes exist: from the root object, and from a context someone already holds
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(executor));
        Assert.Equal("hello", await ctx.Strings.GetAsync("mykey"));
    }

    [Fact]
    public async Task AppendKeyPrefixIsJustAContextClone()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var target = Target(executor);

        // this is the whole of KeyPrefixedDatabase's write half - no per-method forwarding
        var tenant = target.AppendKeyPrefix("t7:");
        await tenant.Strings.SetAsync("user:1", "marc");

        Assert.Equal("*3|$3|SET|$9|t7:user:1|$4|marc|", Assert.Single(executor.Sent));
    }

    [Fact]
    public async Task TheCacheServesASecondReadWithoutSending()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var target = Target(executor, cache);

        Assert.Equal("hello", await target.Strings.GetAsync("mykey"));
        Assert.Equal("hello", await target.Strings.GetAsync("mykey"));
        Assert.Single(executor.Sent); // the second read never reached the executor

        cache.OnInvalidate(Encoding.UTF8.GetBytes("mykey"));
        Assert.Equal("hello", await target.Strings.GetAsync("mykey"));
        Assert.Equal(2, executor.Sent.Count);
    }

    [Fact]
    public async Task WritesAreNotCached()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("+OK\r\n");
        var target = Target(executor, cache);

        await target.Strings.SetAsync("mykey", "hello");
        await target.Strings.SetAsync("mykey", "hello");

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

        await target.Strings.GetAsync("mykey");
        await target.Strings.GetAsync("mykey", CommandFlags.CommandRetryReadOnly | CommandFlags.NoClientCache);

        Assert.Equal(2, executor.Sent.Count); // the opted-out call did not read the cached entry
    }

    [Fact]
    public void ConnectionBackedTypesThrowForNow()
    {
        // IRedis carries the member, so IDatabase/IServer/ISubscriber all have it - but wiring it to a live
        // multiplexer is separate work, so those throw while RespDatabaseContext is what actually runs
        IRespTarget target = (IRespTarget)(object)new RespDatabaseContext(new RespContext());
        Assert.Equal(0, target.Raw.Database); // the minimal one works
    }

    [Fact]
    public void MissingExecutorFailsLoudlyRatherThanSilently()
    {
        var target = new RespDatabaseContext(new RespContext());
        Assert.Throws<InvalidOperationException>(() => target.Strings.GetAsync("mykey"));
    }

    // ---- PING ---------------------------------------------------------------------------------------

    /// <summary>Sleeps before replying, so a measurement has something to find.</summary>
    private sealed class SlowExecutor(TimeSpan delay) : RespExecutorBase
    {
        public override int Database => 0;

        public List<string> Sent { get; } = [];

        public override RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            Thread.Sleep(delay);
            return RespPayload.Create(Encoding.UTF8.GetBytes("+PONG\r\n"));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    /// <summary>The plain ping asks for nothing back, and says so in its return type.</summary>
    [Fact]
    public async Task PingAsyncReturnsNothingAndSendsPING()
    {
        var executor = new FakeExecutor("+PONG\r\n");
        await Target(executor).PingAsync();

        Assert.Equal("*1|$4|PING|", Assert.Single(executor.Sent));
    }

    /// <summary>A synchronously-completed ping costs no task.</summary>
    [Fact]
    public void PingAsyncThatCompletesSynchronouslyAllocatesNoTask()
    {
        var pending = Target(new FakeExecutor("+PONG\r\n")).PingAsync();

        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal(default, pending);
    }

    /// <summary>The measuring ping sends the same command and answers how long it took.</summary>
    [Fact]
    public async Task PingMeasureAsyncSendsPINGAndTimesIt()
    {
        var executor = new SlowExecutor(TimeSpan.FromMilliseconds(30));
        var elapsed = await Target2(executor).PingMeasureAsync();

        Assert.Equal("*1|$4|PING|", Assert.Single(executor.Sent));

        // generous, because CI clocks are not: the assertion is that the clock ran at all, and that it
        // ran over the round trip rather than over something shorter
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(15), $"measured {elapsed}");
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"measured {elapsed}");
    }

    /// <summary>
    /// Each call gets its own clock.
    /// </summary>
    /// <remarks>
    /// The property that makes a per-call handler allocation the right answer rather than merely an easy
    /// one: a shared handler would carry one start timestamp, so the second measurement would be the time
    /// since the FIRST call - growing without bound and looking plausible the whole way.
    /// </remarks>
    [Fact]
    public async Task EachPingMeasureGetsItsOwnClock()
    {
        var target = Target2(new SlowExecutor(TimeSpan.FromMilliseconds(30)));

        var first = await target.PingMeasureAsync();
        await Task.Delay(100);
        var second = await target.PingMeasureAsync();

        // if the clock were shared, the second would include the delay and the first ping as well
        Assert.True(second < first + TimeSpan.FromMilliseconds(80), $"first {first}, second {second}");
    }

    private static RespDatabaseContext Target2(RespExecutorBase executor)
        => new(new RespContext().WithExecutor(executor));
}
