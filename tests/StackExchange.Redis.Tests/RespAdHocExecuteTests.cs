using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The ad-hoc escape hatch: run a command this library does not model, get the raw reply back.
/// </summary>
/// <remarks>
/// How another library's commands reach this surface. The argument type is the interesting part - see
/// <see cref="ArgumentsKeepTheirKeyNess"/>.
/// </remarks>
public class RespAdHocExecuteTests
{
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        internal System.Collections.Generic.List<string> Sent { get; } = [];

        /// <summary>The keys the writer marked, captured at send time - the request is recycled after.</summary>
        internal System.Collections.Generic.List<string> Keys { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));

            var count = request.KeyCount;
            if (count > 0)
            {
                var ranges = new KeyRange[count];
                if (request.TryGetKeys(ranges) == count)
                {
                    foreach (var range in ranges) Keys.Add(Encoding.UTF8.GetString(request.GetKey(range).ToArray()));
                }
            }

            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static RespDatabaseContext Context(FakeExecutor executor, RespClientCache? cache = null)
        => new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

    [Fact]
    public async Task AnUnmodelledCommandRoundTrips()
    {
        var executor = new FakeExecutor("*2\r\n$3\r\ndoc\r\n:1\r\n");
        RedisKeyOrValue[] args = [RedisKeyOrValue.FromKey("idx"), RedisKeyOrValue.FromValue("@title:hello")];

        using var result = await Context(executor).Raw.ExecuteAsync("FT.SEARCH", args);

        Assert.Equal("*3|$9|FT.SEARCH|$3|idx|$12|@title:hello|", Assert.Single(executor.Sent));
        Assert.Equal(RespPrefix.Array, result.Prefix);
    }

    [Fact]
    public async Task ArgumentsKeepTheirKeyNess()
    {
        // the reason for RedisKeyOrValue rather than object[]: boxing loses key-ness, and with it routing,
        // invalidation, and any chance of caching an ad-hoc command correctly
        var executor = new FakeExecutor("+OK\r\n");
        RedisKeyOrValue[] args = [RedisKeyOrValue.FromKey("thekey"), RedisKeyOrValue.FromValue("thevalue")];

        using var _ = await Context(executor).Raw.ExecuteAsync("JSON.SET", args);

        // exactly one of the two arguments was marked as a key, and it was the right one
        Assert.Equal("thekey", Assert.Single(executor.Keys));
    }

    [Fact]
    public async Task ItIsReachableFromAnythingCarryingAContext()
    {
        // one extension on IRespTarget, and every database gets it without being touched
        var executor = new FakeExecutor("$3\r\nabc\r\n");
        IRespTarget target = Context(executor);

        using var result = await target.Raw.ExecuteAsync("SOME.COMMAND", new[] { RedisKeyOrValue.FromValue("x") });

        Assert.Equal("abc", result.ReadScalar().ReadString());
    }

    [Fact]
    public async Task AnAdHocReadCanBeCachedAndInvalidated()
    {
        // the payoff of keeping key-ness: an unmodelled command participates in the cache like any other
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$3\r\nabc\r\n", "$3\r\nxyz\r\n");
        var context = Context(executor, cache);
        RedisKeyOrValue[] args = [RedisKeyOrValue.FromKey("k")];

        (await context.Raw.ExecuteAsync("MODULE.GET", args, CommandFlags.CommandRetryReadOnly)).Dispose();
        (await context.Raw.ExecuteAsync("MODULE.GET", args, CommandFlags.CommandRetryReadOnly)).Dispose();
        Assert.Single(executor.Sent);   // served from cache

        Assert.True(cache.OnInvalidate(Encoding.UTF8.GetBytes("k")));

        using var fresh = await context.Raw.ExecuteAsync("MODULE.GET", args, CommandFlags.CommandRetryReadOnly);
        Assert.Equal(2, executor.Sent.Count);   // invalidated by key, and re-fetched
        Assert.Equal("xyz", fresh.ReadScalar().ReadString());
    }

    [Fact]
    public async Task TheLegacyInterfaceReachesTheSamePath()
    {
        // ExecuteResp's signature and the context method agree exactly, RedisKeyOrValue included - so the
        // adapter is a pass-through, and an IDatabase caller gets the key-marking behaviour for free
        var executor = new FakeExecutor("$3\r\nabc\r\n");
        IDatabase db = Context(executor).AsDatabase(NSubstitute.Substitute.For<IConnectionMultiplexer>());

        using var result = await db.ExecuteRespAsync("MODULE.GET", new[] { RedisKeyOrValue.FromKey("k") });

        Assert.Equal("abc", result.ReadScalar().ReadString());
        Assert.Equal("k", Assert.Single(executor.Keys));
    }

    [Fact]
    public async Task NoArgumentsIsFine()
    {
        var executor = new FakeExecutor("+PONG\r\n");
        using var result = await Context(executor).Raw.ExecuteAsync("PING", default);

        Assert.Equal("*1|$4|PING|", Assert.Single(executor.Sent));
        Assert.Equal("PONG", result.ReadScalar().ReadString());
    }
}
