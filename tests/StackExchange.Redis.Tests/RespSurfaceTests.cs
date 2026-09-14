using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    /// <summary>A minimal root object, standing in for what IDatabase would become.</summary>
    private sealed class FakeTarget(RespContext context) : IRespTarget
    {
        public RespContext Context { get; } = context;
    }

    private static FakeTarget Target(FakeExecutor executor, RespClientCache? cache = null)
        => new(new RespContext().WithExecutor(executor).WithCache(cache));

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
        var tenant = new FakeTarget(target.Context.WithKeyPrefix("t7:"));
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
    public void MissingExecutorFailsLoudlyRatherThanSilently()
    {
        var target = new FakeTarget(new RespContext());
        Assert.Throws<InvalidOperationException>(() => target.Strings.Get("mykey"));
    }
}
