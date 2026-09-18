using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Losing the connection must empty the cache.
/// </summary>
/// <remarks>
/// Server-assisted invalidation only works while somebody is listening: anything that changes during a
/// disconnect is never announced, because the server forgets a client it has lost. An entry that survives
/// the gap is wrong with nothing left in the system that will ever say so.
/// </remarks>
public class RespCacheDisconnectTests
{
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        internal int Sends { get; private set; }

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sends++;
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static ConnectionFailedEventArgs Failure(object sender) => new(
        sender,
        new DnsEndPoint("localhost", 6379),
        ConnectionType.Interactive,
        ConnectionFailureType.SocketClosed,
        new Exception("boom"),
        "physical");

    private static ValueTask<RedisValue> Get(RespDatabaseContext context)
        => context.SendAsync<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.CommandRetryReadOnly);

    [Fact]
    public async Task ADisconnectEmptiesTheCache()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        using var cache = new RespClientCache();
        using var _ = cache.FlushOnDisconnect(multiplexer);

        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

        Assert.Equal("a", await Get(context));
        Assert.Equal("a", await Get(context));
        Assert.Equal(1, executor.Sends);          // served from cache

        multiplexer.ConnectionFailed += Raise.EventWith(multiplexer, Failure(multiplexer));

        // anything could have changed while nobody was listening, so nothing survives
        Assert.Equal("b", await Get(context));
        Assert.Equal(2, executor.Sends);
    }

    [Fact]
    public async Task WithoutTheHookAnEntrySurvivesADisconnect()
    {
        // the failure this guards against, made explicit: the entry lives on, and no invalidation is ever
        // coming for it, because the server forgot us
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        using var cache = new RespClientCache();

        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

        Assert.Equal("a", await Get(context));
        multiplexer.ConnectionFailed += Raise.EventWith(multiplexer, Failure(multiplexer));

        Assert.Equal("a", await Get(context));    // stale, indefinitely
        Assert.Equal(1, executor.Sends);
    }

    [Fact]
    public async Task DisposingTheSubscriptionStopsTheFlushing()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        using var cache = new RespClientCache();
        var subscription = cache.FlushOnDisconnect(multiplexer);

        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

        Assert.Equal("a", await Get(context));
        subscription.Dispose();

        multiplexer.ConnectionFailed += Raise.EventWith(multiplexer, Failure(multiplexer));

        Assert.Equal("a", await Get(context));    // no longer listening
        Assert.Equal(1, executor.Sends);

        subscription.Dispose();                   // and disposing twice is not an error
    }

    [Fact]
    public void ItFlushesWhicheverConnectionFailed()
    {
        // deliberately not trying to decide whether THAT connection was carrying invalidations: getting
        // that judgement wrong is silent, and over-flushing only costs a round trip per key
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        using var cache = new RespClientCache();
        using var _ = cache.FlushOnDisconnect(multiplexer);

        foreach (var type in new[] { ConnectionType.Interactive, ConnectionType.Subscription })
        {
            var args = new ConnectionFailedEventArgs(
                multiplexer,
                new DnsEndPoint("localhost", 6379),
                type,
                ConnectionFailureType.SocketClosed,
                new Exception("boom"),
                "physical");

            multiplexer.ConnectionFailed += Raise.EventWith(multiplexer, args);
        }

        Assert.Equal(0, cache.Count);
    }
}
