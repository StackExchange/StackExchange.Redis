using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A command that carries a key must report that key's hash slot, or a cluster client sends it to an arbitrary node
/// and leans on <c>-MOVED</c>: an extra round trip in the usual case, and a failure under
/// <see cref="CommandFlags.NoRedirect"/>. The v3 core's <c>INCREX</c> and <c>ARGREP</c> messages both shipped
/// without one (#3352); in this core the key append itself folds the slot, and these pin that.
/// </summary>
public class KeyedMessageSlotTests(ITestOutputHelper output) : TestBase(output)
{
    private static (RespDatabaseContext Context, FakeExecutor Executor) CreateCluster(params string[] replies)
    {
        var executor = new FakeExecutor(replies);
        var context = new RespDatabaseContext(new RespContext(serverType: ServerType.Cluster).WithExecutor(executor));
        return (context, executor);
    }

    private static void AssertRoutedByKey(FakeExecutor executor, RedisKey key)
    {
        var expected = ServerSelectionStrategy.GetHashSlot(key);
        Assert.NotEqual(ServerSelectionStrategy.NoSlot, expected);
        Assert.Equal(expected, Assert.Single(executor.Slots));
    }

    [Fact]
    public async Task Increx_IsRoutedByKey()
    {
        var (db, executor) = CreateCluster("*2\r\n:5\r\n:5\r\n");
        var expiry = Expiration.CreateOrKeepTtl(TimeSpan.FromMinutes(1), keepTtl: false);

        var result = await db.Strings.IncrementAsync("some-counter", 5, expiry);

        Assert.Equal(5, result.Value);
        AssertRoutedByKey(executor, "some-counter");
    }

    [Fact]
    public async Task ArrayGrep_IsRoutedByKey()
    {
        var (db, executor) = CreateCluster("*1\r\n:0\r\n");

        using var found = await db.Arrays.GrepAsync("some-array", CreateGrep());

        Assert.Equal(1, found.Span.Length);
        AssertRoutedByKey(executor, "some-array");
    }

    private static ArrayGrepRequest CreateGrep()
    {
        var request = new ArrayGrepRequest();
        request.AddPredicate(ArrayGrepRequest.Predicate.Match("alpha"));
        return request;
    }

    // enough keys that, sent to an arbitrary node, some would certainly miss their owner
    private const int KeyCount = 32;

    [Fact]
    public async Task Increx_OnCluster_NeedsNoRedirect()
    {
        await using var conn = Create(configuration: GetClusterConfiguration(), require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var expiry = Expiration.CreateOrKeepTtl(TimeSpan.FromMinutes(1), keepTtl: false);

        for (int i = 0; i < KeyCount; i++)
        {
            RedisKey key = Me() + ":" + i;
            await db.KeyDeleteAsync(key);

            // NoRedirect: lands on the slot owner first time, or throws
            var result = await db.StringIncrementAsync(key, 5, expiry, flags: CommandFlags.NoRedirect);
            Assert.Equal(5, result.Value);

            await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
        }
    }

    [Fact]
    public async Task ArrayGrep_OnCluster_NeedsNoRedirect()
    {
        await using var conn = Create(configuration: GetClusterConfiguration(), require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var grep = CreateGrep();

        for (int i = 0; i < KeyCount; i++)
        {
            RedisKey key = Me() + ":" + i;
            await db.KeyDeleteAsync(key);
            await db.ArraySetAsync(key, 0, "alpha");

            var found = await db.ArrayGrepAsync(key, grep, CommandFlags.NoRedirect);
            Assert.Equal(0, Assert.Single(found).Index);

            await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
        }
    }
}
