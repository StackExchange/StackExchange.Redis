using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class MultiAddTests : TestBase
{
    public MultiAddTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public void AddSortedSetEveryWay()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.SortedSetAdd(key, "a", 1, CommandFlags.FireAndForget);
        db.SortedSetAdd(key, new[] {
                new SortedSetEntry("b", 2) }, CommandFlags.FireAndForget);
        db.SortedSetAdd(key, new[] {
                new SortedSetEntry("c", 3),
                new SortedSetEntry("d", 4)}, CommandFlags.FireAndForget);
        db.SortedSetAdd(key, new[] {
                new SortedSetEntry("e", 5),
                new SortedSetEntry("f", 6),
                new SortedSetEntry("g", 7)}, CommandFlags.FireAndForget);
        db.SortedSetAdd(key, new[] {
                new SortedSetEntry("h", 8),
                new SortedSetEntry("i", 9),
                new SortedSetEntry("j", 10),
                new SortedSetEntry("k", 11)}, CommandFlags.FireAndForget);
        var vals = db.SortedSetRangeByScoreWithScores(key);
        string s = string.Join(",", vals.OrderByDescending(x => x.Score).Select(x => x.Element));
        Assert.Equal("k,j,i,h,g,f,e,d,c,b,a", s);
        s = string.Join(",", vals.OrderBy(x => x.Score).Select(x => x.Score));
        Assert.Equal("1,2,3,4,5,6,7,8,9,10,11", s);
    }

    [Fact]
    public void AddHashEveryWay()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.HashSet(key, "a", 1, flags: CommandFlags.FireAndForget);
        db.HashSet(key, new[] {
                new HashEntry("b", 2) }, CommandFlags.FireAndForget);
        db.HashSet(key, new[] {
                new HashEntry("c", 3),
                new HashEntry("d", 4)}, CommandFlags.FireAndForget);
        db.HashSet(key, new[] {
                new HashEntry("e", 5),
                new HashEntry("f", 6),
                new HashEntry("g", 7)}, CommandFlags.FireAndForget);
        db.HashSet(key, new[] {
                new HashEntry("h", 8),
                new HashEntry("i", 9),
                new HashEntry("j", 10),
                new HashEntry("k", 11)}, CommandFlags.FireAndForget);
        var vals = db.HashGetAll(key);
        string s = string.Join(",", vals.OrderByDescending(x => (double)x.Value).Select(x => x.Name));
        Assert.Equal("k,j,i,h,g,f,e,d,c,b,a", s);
        s = string.Join(",", vals.OrderBy(x => (double)x.Value).Select(x => x.Value));
        Assert.Equal("1,2,3,4,5,6,7,8,9,10,11", s);
    }

    [Fact]
    public void AddSetEveryWay()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.SetAdd(key, "a", CommandFlags.FireAndForget);
        db.SetAdd(key, new RedisValue[] { "b" }, CommandFlags.FireAndForget);
        db.SetAdd(key, new RedisValue[] { "c", "d" }, CommandFlags.FireAndForget);
        db.SetAdd(key, new RedisValue[] { "e", "f", "g" }, CommandFlags.FireAndForget);
        db.SetAdd(key, new RedisValue[] { "h", "i", "j", "k" }, CommandFlags.FireAndForget);

        var vals = db.SetMembers(key);
        string s = string.Join(",", vals.OrderByDescending(x => x));
        Assert.Equal("k,j,i,h,g,f,e,d,c,b,a", s);
    }

    [Fact]
    public void AddSetEveryWayNumbers()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.SetAdd(key, "a", CommandFlags.FireAndForget);
        db.SetAdd(key, new RedisValue[] { "1" }, CommandFlags.FireAndForget);
        db.SetAdd(key, new RedisValue[] { "11", "2" }, CommandFlags.FireAndForget);
        db.SetAdd(key, new RedisValue[] { "10", "3", "1.5" }, CommandFlags.FireAndForget);
        db.SetAdd(key, new RedisValue[] { "2.2", "-1", "s", "t" }, CommandFlags.FireAndForget);

        var vals = db.SetMembers(key);
        string s = string.Join(",", vals.OrderByDescending(x => x));
        Assert.Equal("t,s,a,11,10,3,2.2,2,1.5,1,-1", s);
    }
}
