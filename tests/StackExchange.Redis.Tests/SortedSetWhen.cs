using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class SortedSetWhenTest : TestBase
{
    public SortedSetWhenTest(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public void GreaterThanLessThan()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        var member = "a";
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.SortedSetAdd(key, member, 2);

        Assert.True(db.SortedSetUpdate(key, member, 5, when: SortedSetWhen.GreaterThan));
        Assert.False(db.SortedSetUpdate(key, member, 1, when: SortedSetWhen.GreaterThan));
        Assert.True(db.SortedSetUpdate(key, member, 1, when: SortedSetWhen.LessThan));
        Assert.False(db.SortedSetUpdate(key, member, 5, when: SortedSetWhen.LessThan));
    }

    [Fact]
    public void IllegalCombinations()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        var member = "a";
        db.KeyDelete(key, CommandFlags.FireAndForget);

        Assert.Throws<StackExchange.Redis.RedisServerException>(() => db.SortedSetAdd(key, member, 5, when: SortedSetWhen.LessThan | SortedSetWhen.GreaterThan));
        Assert.Throws<StackExchange.Redis.RedisServerException>(() => db.SortedSetAdd(key, member, 5, when: SortedSetWhen.Exists | SortedSetWhen.NotExists));
        Assert.Throws<StackExchange.Redis.RedisServerException>(() => db.SortedSetAdd(key, member, 5, when: SortedSetWhen.GreaterThan | SortedSetWhen.NotExists));
        Assert.Throws<StackExchange.Redis.RedisServerException>(() => db.SortedSetAdd(key, member, 5, when: SortedSetWhen.LessThan | SortedSetWhen.NotExists));
    }
}
