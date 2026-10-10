using System;
using System.Text;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Turning a server <c>invalidate</c> push into evictions.
/// </summary>
/// <remarks>
/// The frame is what the server sends when a key we read has changed under us, so everything here is
/// about erring toward a miss: a shape we cannot read flushes rather than quietly keeping entries we have
/// just been told are wrong. Read against real bytes, and against the one implementation both cores use -
/// the shipped connection delegates here too, because this is precisely the sort of parsing that drifts
/// when it is written twice.
/// </remarks>
public class RespCacheInvalidationFrameTests
{
    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private static ValueTask<RedisValue> Get(RespDatabaseContext context, string key)
        => context.SendAsync<RedisValue>($"{RedisCommand.GET}{(RedisKey)key}", Readable);

    /// <summary>Feed a complete push frame to the invalidation reader, as the dispatcher would.</summary>
    private static void Invalidate(RespClientCache cache, string frame)
    {
        var reader = new RespReader(Encoding.UTF8.GetBytes(frame));
        Assert.True(reader.SafeTryMoveNext()); // the push itself
        Assert.True(reader.SafeTryMoveNext()); // the "invalidate" token; the payload follows
        RespPushDispatch.ApplyInvalidation(cache, ref reader);
    }

    private static (RespClientCache Cache, RespDatabaseContext Context, FakeExecutor Executor) Cached()
    {
        var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromHours(1) } });
        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        return (cache, new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache)), executor);
    }

    [Fact]
    public async Task ANamedKeyIsEvicted()
    {
        var (cache, context, executor) = Cached();
        using var _ = cache;

        Assert.Equal("a", await Get(context, "k"));
        Assert.Equal("a", await Get(context, "k")); // served from cache
        Assert.Equal(1, executor.Sends);

        Invalidate(cache, ">2\r\n$10\r\ninvalidate\r\n*1\r\n$1\r\nk\r\n");

        Assert.Equal("b", await Get(context, "k")); // fetched again
        Assert.Equal(2, executor.Sends);
    }

    /// <summary>One write can name several keys - <c>MSET a b c</c> arrives as a single push.</summary>
    [Fact]
    public async Task EveryNamedKeyIsEvicted()
    {
        var (cache, context, executor) = Cached();
        using var _ = cache;

        Assert.Equal("a", await Get(context, "k1"));
        Assert.Equal("b", await Get(context, "k2"));
        Assert.Equal(2, executor.Sends);

        Invalidate(cache, ">2\r\n$10\r\ninvalidate\r\n*2\r\n$2\r\nk1\r\n$2\r\nk2\r\n");

        await Get(context, "k1");
        await Get(context, "k2");
        Assert.Equal(4, executor.Sends); // neither survived
    }

    /// <summary>
    /// A null payload is the flush: <c>FLUSHALL</c>/<c>FLUSHDB</c>, and also the moment tracking is turned
    /// off. It is the one invalidation that cannot be filtered by prefix, so it is never safe to ignore.
    /// </summary>
    [Fact]
    public async Task ANullPayloadFlushesEverything()
    {
        var (cache, context, executor) = Cached();
        using var _ = cache;

        Assert.Equal("a", await Get(context, "untouched"));
        Assert.Equal(1, executor.Sends);

        Invalidate(cache, ">2\r\n$10\r\ninvalidate\r\n_\r\n");

        Assert.Equal("b", await Get(context, "untouched"));
        Assert.Equal(2, executor.Sends);
    }

    /// <summary>
    /// A shape we cannot read over-flushes rather than being ignored: we already know something changed,
    /// and the only question left is how much to throw away.
    /// </summary>
    [Theory]
    [InlineData(">2\r\n$10\r\ninvalidate\r\n:1\r\n")] // not an aggregate at all
    [InlineData(">2\r\n$10\r\ninvalidate\r\n*1\r\n*1\r\n$1\r\nk\r\n")] // a "key" that is not a scalar
    public async Task AnUnreadablePayloadFlushesEverything(string frame)
    {
        var (cache, context, executor) = Cached();
        using var _ = cache;

        Assert.Equal("a", await Get(context, "unrelated"));
        Assert.Equal(1, executor.Sends);

        Invalidate(cache, frame);

        Assert.Equal("b", await Get(context, "unrelated"));
        Assert.Equal(2, executor.Sends);
    }

    /// <summary>A key nobody cached is not an error, and takes nothing else with it.</summary>
    [Fact]
    public async Task AnUnknownKeyLeavesTheRestAlone()
    {
        var (cache, context, executor) = Cached();
        using var _ = cache;

        Assert.Equal("a", await Get(context, "kept"));
        Assert.Equal(1, executor.Sends);

        Invalidate(cache, ">2\r\n$10\r\ninvalidate\r\n*1\r\n$5\r\nother\r\n");

        Assert.Equal("a", await Get(context, "kept")); // still cached
        Assert.Equal(1, executor.Sends);
    }
}
