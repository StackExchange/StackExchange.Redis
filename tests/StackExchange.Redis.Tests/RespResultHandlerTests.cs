using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="RespResult"/> as a built-in result type: the undecoded reply, for commands this library does
/// not model - which is how another library's commands reach the new surface at all.
/// </summary>
public class RespResultHandlerTests
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

    private static RespContext Context(FakeExecutor executor, RespClientCache? cache = null)
        => new RespContext().WithExecutor(executor).WithCache(cache);

    [Fact]
    public async Task AnyCommandCanComeBackAsARespResult()
    {
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        using var result = await Context(executor).SendAsync<RespResult>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None);

        Assert.Equal(RespPrefix.BulkString, result.Prefix);
        Assert.False(result.IsNull);

        var reader = result.ReadScalar();
        Assert.Equal("hello", reader.ReadString());
    }

    [Fact]
    public async Task AggregatesSurviveUndecoded()
    {
        // the point of the undecoded reply: we do not need to know the shape. This is what a module command
        // looks like to us.
        var executor = new FakeExecutor("*3\r\n$1\r\na\r\n:42\r\n$-1\r\n");
        using var result = await Context(executor).SendAsync<RespResult>($"{RedisCommand.MGET}{(RedisKey)"k"}", CommandFlags.None);

        Assert.Equal(RespPrefix.Array, result.Prefix);

        var reader = result.Read();
        Assert.Equal(3, reader.AggregateLength());
        Assert.True(reader.TryMoveNext(false));
        Assert.Equal("a", reader.ReadString());
        Assert.True(reader.TryMoveNext(false));
        Assert.Equal(42, reader.ReadInt64());
        Assert.True(reader.TryMoveNext(false));
        Assert.True(reader.IsNull);
    }

    [Fact]
    public async Task ANullReplyIsARespResultToo()
    {
        var executor = new FakeExecutor("_\r\n");
        using var result = await Context(executor).SendAsync<RespResult>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None);

        Assert.True(result.IsNull);
    }

    [Fact]
    public async Task ARespResultServedFromCacheIsIndistinguishable()
    {
        // whether the bytes came from the wire or the cache must not be observable except in timing
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var context = Context(executor, cache);

        using (var first = await context.SendAsync<RespResult>(
            $"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.CommandRetryReadOnly))
        {
            Assert.Equal("hello", first.ReadScalar().ReadString());
        }

        using (var second = await context.SendAsync<RespResult>(
            $"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.CommandRetryReadOnly))
        {
            Assert.Equal("hello", second.ReadScalar().ReadString());
        }

        Assert.Equal(1, executor.Sends);   // the second was a cache hit
    }

    [Fact]
    public async Task TheResultOutlivesTheReplyItCameFrom()
    {
        // the pipeline releases its own reference as soon as Parse returns, so a result that did not own
        // its bytes would be reading a recycled buffer by the time the caller looked
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var result = await Context(executor).SendAsync<RespResult>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None);

        for (var i = 0; i < 32; i++) _ = new byte[4096];   // churn the pool a little
        GC.Collect();

        Assert.Equal("hello", result.ReadScalar().ReadString());
        result.Dispose();
    }
}
