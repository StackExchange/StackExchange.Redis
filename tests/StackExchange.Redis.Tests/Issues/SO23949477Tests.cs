﻿using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class SO23949477Tests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task Execute()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.SortedSetAdd(key, "c", 3, When.Always, CommandFlags.FireAndForget);
        db.SortedSetAdd(
            key,
            [
                new SortedSetEntry("a", 1),
                new SortedSetEntry("b", 2),
                new SortedSetEntry("d", 4),
                new SortedSetEntry("e", 5),
            ],
            When.Always,
            CommandFlags.FireAndForget);
        var pairs = db.SortedSetRangeByScoreWithScores(
            key, order: Order.Descending, take: 3);
        Assert.Equal(3, pairs.Length);
        Assert.Equal(5, pairs[0].Score);
        Assert.Equal("e", pairs[0].Element);
        Assert.Equal(4, pairs[1].Score);
        Assert.Equal("d", pairs[1].Element);
        Assert.Equal(3, pairs[2].Score);
        Assert.Equal("c", pairs[2].Element);
    }
}
