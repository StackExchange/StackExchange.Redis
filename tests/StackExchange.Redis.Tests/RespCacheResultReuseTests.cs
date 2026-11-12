using System;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Deserialization reuse on cache hits: a result the rules declare shareable is memoized on the entry,
/// and later hits are served the parsed result itself rather than a fresh parse of the payload.
/// </summary>
/// <remarks>
/// <para>
/// The sequence every test leans on: the FIRST read is the fill (its parse happens on the fill path and
/// is not memoized), the SECOND read is the first hit (it parses, and offers the result to the slot), and
/// the THIRD read onward is served from the memo. So "same instance" is asserted between the second and
/// third reads, and <c>cache.Reused</c> counts reads from the third on.
/// </para>
/// <para>
/// What the core auto-enrolls is deliberately narrow - strings and no-reference value types - and the
/// richer rules belong to the handler via <see cref="IRespResultReuse{TResult}"/>; these tests pin both
/// the enrollments and the refusals.
/// </para>
/// </remarks>
public class RespCacheResultReuseTests
{
    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private static (RespDatabaseContext Context, RespClientCache Cache, FakeExecutor Executor) Create(params string[] replies)
    {
        var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss });
        var executor = new FakeExecutor(replies);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));
        return (context, cache, executor);
    }

    [Fact]
    public async Task AStringIsHandedBackAsTheSameInstance()
    {
        var (context, cache, executor) = Create("$5\r\nhello\r\n");
        using (cache)
        {
            var filled = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            var parsed = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            var reused = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            Assert.Equal("hello", filled);
            Assert.Equal("hello", parsed);
            Assert.Same(parsed, reused); // the memo, not a re-decode
            Assert.Equal(1, executor.Sends);
            Assert.Equal(1L, cache.Reused);
        }
    }

    [Fact]
    public async Task ANoReferenceStructIsReused()
    {
        var (context, cache, executor) = Create(":42\r\n");
        using (cache)
        {
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(42, await context.SendAsync<long>($"{RedisCommand.STRLEN}{(RedisKey)"k"}", Readable));
            }

            Assert.Equal(1, executor.Sends);

            // fill, first hit (parses + memoizes), then two served from the box; a primitive qualifies
            // under both the IsReferenceOrContainsReferences detection and the down-level fallback
            Assert.Equal(2L, cache.Reused);
        }
    }

    [Fact]
    public async Task ARedisValueIsNotAutoEnrolled()
    {
        // RedisValue is a struct that CARRIES a reference, so handing out the same box could hand out
        // shared mutable state (the object it wraps); it must re-parse on every hit
        var (context, cache, executor) = Create("$5\r\nhello\r\n");
        using (cache)
        {
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal("hello", (string?)await context.SendAsync<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable));
            }

            Assert.Equal(1, executor.Sends); // still served from cache...
            Assert.Equal(0L, cache.Reused);  // ...but parsed each time
        }
    }

    [Fact]
    public async Task AHandlerCanDeclareItsResultsShareable()
    {
        var (context, cache, executor) = Create("$5\r\nhello\r\n");
        using (cache)
        {
            var filled = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, ReusablePocoHandler.Instance);
            var parsed = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, ReusablePocoHandler.Instance);
            var reused = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, ReusablePocoHandler.Instance);

            Assert.Equal(5, filled.Length);
            Assert.Same(parsed, reused); // a mutable-looking class, shared because the handler vouched for it
            Assert.NotSame(filled, parsed); // the fill's own parse is not memoized; the first HIT's is
            Assert.Equal(1, executor.Sends);
            Assert.Equal(1L, cache.Reused);
        }
    }

    [Fact]
    public async Task AHandlerThatDoesNotOptInReparsesEveryHit()
    {
        var (context, cache, executor) = Create("$5\r\nhello\r\n");
        using (cache)
        {
            var first = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, PlainPocoHandler.Instance);
            var second = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, PlainPocoHandler.Instance);
            var third = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, PlainPocoHandler.Instance);

            Assert.NotSame(second, third);
            Assert.Equal(1, executor.Sends);
            Assert.Equal(0L, cache.Reused);
        }
    }

    [Fact]
    public async Task AHandlerCanRefusePerValue()
    {
        // the opt-in is asked per value, so a handler can trust some shapes and not others; this one
        // refuses everything at runtime despite implementing the interface
        var (context, cache, executor) = Create("$5\r\nhello\r\n");
        using (cache)
        {
            _ = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, SelectivePocoHandler.Instance);
            var second = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, SelectivePocoHandler.Instance);
            var third = await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, SelectivePocoHandler.Instance);

            Assert.NotSame(second, third);
            Assert.Equal(0L, cache.Reused);

            // consulted on both HIT parses; the fill's parse is never memoized, so it is never asked
            Assert.Equal(2, SelectivePocoHandler.Instance.Asked);
        }
    }

    [Fact]
    public async Task TheMemoIsKeyedByHandlerInstance()
    {
        // two handlers, same frame, same TResult: the memo one handler stored must never be served
        // through the other, because "same type" does not mean "same parse"
        var (context, cache, executor) = Create("$5\r\nhello\r\n");
        using (cache)
        {
            var plain = new ScalingHandler(1);
            var scaled = new ScalingHandler(10);

            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(5, await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, plain));
            }

            // a different handler on the same cached entry: it must parse for itself, not inherit 5
            Assert.Equal(50, await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, scaled));
            Assert.Equal(50, await context.SendAsync($"{RedisCommand.GET}{(RedisKey)"k"}", Readable, scaled));

            Assert.Equal(1, executor.Sends);
        }
    }

    [Fact]
    public async Task ALocalWriteDropsTheMemoWithTheEntry()
    {
        var (context, cache, executor) = Create("$1\r\na\r\n", "+OK\r\n", "$1\r\nb\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            var before = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable); // memoized
            Assert.Equal("a", before);

            // our own write: the entry - memo and all - must never be served again
            _ = await context.SendAsync<string>($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"b"}", CommandFlags.None);

            var after = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            Assert.Equal("b", after);
            Assert.Equal(3, executor.Sends);
        }
    }

    private sealed class Poco(int length)
    {
        public int Length => length;
    }

    private sealed class ReusablePocoHandler : IRespHandler<Poco>, IRespResultReuse<Poco>
    {
        public static readonly ReusablePocoHandler Instance = new();

        public Poco Parse(ref RespReader reader) => new(reader.ScalarLength());

        public bool MayReuse(in Poco value) => true;
    }

    private sealed class PlainPocoHandler : IRespHandler<Poco>
    {
        public static readonly PlainPocoHandler Instance = new();

        public Poco Parse(ref RespReader reader) => new(reader.ScalarLength());
    }

    private sealed class SelectivePocoHandler : IRespHandler<Poco>, IRespResultReuse<Poco>
    {
        public static readonly SelectivePocoHandler Instance = new();

        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public Poco Parse(ref RespReader reader) => new(reader.ScalarLength());

        public bool MayReuse(in Poco value)
        {
            Interlocked.Increment(ref _asked);
            return false;
        }
    }

    private sealed class ScalingHandler(int factor) : IRespHandler<int>
    {
        public int Parse(ref RespReader reader) => reader.ScalarLength() * factor;
    }
}
