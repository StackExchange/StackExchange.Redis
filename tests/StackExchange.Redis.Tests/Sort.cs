using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class Sort : TestBase
{
    public Sort(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task BasicSort()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, new RedisValue[]{ "1", "3", "2" });

        var res = db.Sort(key);
        Assert.Equal(new RedisValue[]{"1", "2", "3"}, res);

        res = await db.SortAsync(key);
        Assert.Equal(new RedisValue[]{"1", "2", "3"}, res);
    }

    [Fact]
    public void SortAlphabetic()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, new RedisValue[]{ "1", "2", "10" });

        var res = db.Sort(key, sortType: SortType.Alphabetic);
        Assert.Equal(new RedisValue[]{"1", "10", "2"}, res);
    }

    [Fact]
    public async Task SortAndStore()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        var dest = Me() + "dest";
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, new RedisValue[]{ "1", "3", "2" });

        var res = db.SortAndStore(dest, key);
        Assert.Equal(3, res);
        Assert.Equal(new RedisValue[]{"1", "2", "3"}, db.ListRange(dest, 0, 5));

        res = await db.SortAndStoreAsync(dest, key);
        Assert.Equal(3, res);
        Assert.Equal(new RedisValue[]{"1", "2", "3"}, db.ListRange(dest, 0, 5));
    }

    [Fact]
    public void SortBy()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, new RedisValue[]{ "1", "3", "2" });
        db.StringSet("bar:1", 3);
        db.StringSet("bar:2", 2);
        db.StringSet("bar:3", 1);

        var res = db.Sort(key, by: "bar:*");
        Assert.Equal(new RedisValue[]{"3", "2", "1"}, res);
    }

    [Fact]
    public void SortDesc()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, new RedisValue[]{ "1", "3", "2" });

        var res = db.Sort(key, order: Order.Descending);
        Assert.Equal(new RedisValue[]{"3", "2", "1"}, res);
    }

    [Fact]
    public void SortGet()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, new RedisValue[]{ "1", "2", "10" });

        db.StringSet("bar1", "bar1");
        db.StringSet("bar2", "bar2");
        db.StringSet("bar10", "bar10");

        db.StringSet("car1", "car1");
        db.StringSet("car2", "car2");
        db.StringSet("car10", "car10");

        var res = db.Sort(key, get: new RedisValue[] {"car*", "bar*"});
        Assert.Equal(new RedisValue[]{"car1", "bar1",  "car2", "bar2", "car10", "bar10"}, res);
    }

    [Fact]
    public void SortLimit()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, new RedisValue[]{ "2", "3", "5", "1", "4", "0" });

        var res = db.Sort(key, skip: 1, take: 2);
        Assert.Equal(new RedisValue[]{"1", "2"}, res);
    }

    [Fact]
    public async Task SortReadOnly()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, new RedisValue[]{ "2", "3", "1"});

        var res = db.SortReadOnly(key);
        Assert.Equal(new RedisValue[]{"1", "2", "3"}, res);

        res = await db.SortReadOnlyAsync(key);
        Assert.Equal(new RedisValue[]{"1", "2", "3"}, res);
    }
}
