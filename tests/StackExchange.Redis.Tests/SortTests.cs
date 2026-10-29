using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for <see href="https://redis.io/commands/sort/">SORT</see> and its read-only spelling.
/// </summary>
/// <remarks>
/// SORT had one end-to-end assertion before this file - a bare <c>db.Sort(key)</c> in SetTests - which is
/// thin for a command whose whole difficulty is the optional operands and the order they go in.
/// </remarks>
[RunPerProtocol]
public class SortTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task SortsNumericallyByDefault()
    {
        await using var conn = Create();

        var db = GetDatabase(conn);
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListRightPush(key, [3, 1, 2], CommandFlags.FireAndForget);

        Assert.Equal(new RedisValue[] { 1, 2, 3 }, db.Sort(key));
        Assert.Equal(new RedisValue[] { 3, 2, 1 }, await db.SortAsync(key, order: Order.Descending));
    }

    [Fact]
    public async Task AlphabeticSortIsADifferentOrder()
    {
        await using var conn = Create();

        var db = GetDatabase(conn);
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListRightPush(key, ["c", "a", "b"], CommandFlags.FireAndForget);

        // and the numeric default would be an error on these, which is the point of the operand
        Assert.Equal(new RedisValue[] { "a", "b", "c" }, db.Sort(key, sortType: SortType.Alphabetic));
    }

    [Fact]
    public async Task LimitSkipsAndTakes()
    {
        await using var conn = Create();

        var db = GetDatabase(conn);
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListRightPush(key, [5, 4, 3, 2, 1], CommandFlags.FireAndForget);

        Assert.Equal(new RedisValue[] { 2, 3 }, db.Sort(key, skip: 1, take: 2));

        // the defaults mean "everything" and are not written, so this also pins that skip/take of 0/-1
        // is not accidentally sent as a LIMIT of nothing
        Assert.Equal(new RedisValue[] { 1, 2, 3, 4, 5 }, db.Sort(key, skip: 0, take: -1));
    }

    [Fact]
    public async Task ByAndGetReachOtherKeys()
    {
        await using var conn = Create();

        var db = GetDatabase(conn);
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.KeyDelete(Me() + "_w_a", CommandFlags.FireAndForget);
        db.KeyDelete(Me() + "_w_b", CommandFlags.FireAndForget);
        db.KeyDelete(Me() + "_d_a", CommandFlags.FireAndForget);
        db.KeyDelete(Me() + "_d_b", CommandFlags.FireAndForget);

        db.ListRightPush(key, ["a", "b"], CommandFlags.FireAndForget);
        db.StringSet(Me() + "_w_a", 2, flags: CommandFlags.FireAndForget);
        db.StringSet(Me() + "_w_b", 1, flags: CommandFlags.FireAndForget);
        db.StringSet(Me() + "_d_a", "A", flags: CommandFlags.FireAndForget);
        db.StringSet(Me() + "_d_b", "B", flags: CommandFlags.FireAndForget);

        // sorted by an external weight, so "b" comes first despite sorting after "a" itself
        Assert.Equal(
            new RedisValue[] { "b", "a" },
            db.Sort(key, by: Me() + "_w_*"));

        // and GET fetches per element, with "#" meaning the element; two patterns interleave, which is
        // the part a single-pattern implementation would get away with
        Assert.Equal(
            new RedisValue[] { "b", "B", "a", "A" },
            db.Sort(key, by: Me() + "_w_*", get: ["#", Me() + "_d_*"]));
    }

    [Fact]
    public async Task ByNoSortKeepsTheStoredOrder()
    {
        await using var conn = Create();

        var db = GetDatabase(conn);
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListRightPush(key, ["c", "a", "b"], CommandFlags.FireAndForget);

        // a BY pattern with no "*" cannot vary, so the server skips sorting entirely - the documented way
        // to say "do not sort, just apply LIMIT and GET"
        Assert.Equal(new RedisValue[] { "c", "a", "b" }, db.Sort(key, by: "nosort"));
    }

    [Fact]
    public async Task StoreWritesAListAndReportsItsLength()
    {
        await using var conn = Create();

        var db = GetDatabase(conn);
        RedisKey key = Me();
        RedisKey destination = Me() + "_dest";
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.KeyDelete(destination, CommandFlags.FireAndForget);
        db.ListRightPush(key, [3, 1, 2], CommandFlags.FireAndForget);

        Assert.Equal(3, db.SortAndStore(destination, key));
        Assert.Equal(new RedisValue[] { 1, 2, 3 }, db.ListRange(destination));

        // STORE always writes a list, whatever the source was
        Assert.Equal(RedisType.List, db.KeyType(destination));

        Assert.Equal(2, await db.SortAndStoreAsync(destination, key, skip: 1, take: 2));
        Assert.Equal(new RedisValue[] { 2, 3 }, db.ListRange(destination));
    }

    [Fact]
    public async Task SortingASetNeedsAnOrderToBeStable()
    {
        await using var conn = Create();

        var db = GetDatabase(conn);
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.SetAdd(key, [3, 1, 2], CommandFlags.FireAndForget);

        // the command takes any of the three collection types, which is why it lives on the key group
        Assert.Equal(new RedisValue[] { 1, 2, 3 }, db.Sort(key));
    }
}
