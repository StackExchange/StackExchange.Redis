using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SortedSetWhenTest(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task GreaterThanLessThan()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

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
    public async Task Increment()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        var member = "a";
        var missingMember = "b";
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.SortedSetAdd(key, member, 2);

        Assert.Equal(5, db.SortedSetIncrement(key, member, 3, ValueCondition.Always, CommandFlags.None));
        Assert.Equal(6, db.SortedSetIncrement(key, member, 1, ValueCondition.Exists, CommandFlags.None));
        Assert.Null(db.SortedSetIncrement(key, missingMember, 1, ValueCondition.Exists, CommandFlags.None));
        Assert.Equal(1, db.SortedSetIncrement(key, missingMember, 1, ValueCondition.NotExists, CommandFlags.None));
        Assert.Null(db.SortedSetIncrement(key, member, 1, ValueCondition.NotExists, CommandFlags.None));
        Assert.Equal(8, await db.SortedSetIncrementAsync(key, member, 2, ValueCondition.Exists, CommandFlags.None));
    }

    [Fact]
    public async Task IllegalCombinations()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

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
