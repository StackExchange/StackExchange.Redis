﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue2176Tests(ITestOutputHelper output) : TestBase(output)
    {
        [Fact]
        public async Task Execute_Batch()
        {
            await using var conn = Create();
            var db = conn.GetDatabase();

            var me = Me();
            var key = me + ":1";
            var key2 = me + ":2";
            var keyIntersect = me + ":result";

            db.KeyDelete(key);
            db.KeyDelete(key2);
            db.KeyDelete(keyIntersect);
            db.SortedSetAdd(key, "a", 1345);

            var tasks = new List<Task>();
            var batch = db.CreateBatch();
            tasks.Add(batch.SortedSetAddAsync(key2, "a", 4567));
            tasks.Add(batch.SortedSetCombineAndStoreAsync(SetOperation.Intersect, keyIntersect, [key, key2]));
            var rangeByRankTask = batch.SortedSetRangeByRankAsync(keyIntersect);
            tasks.Add(rangeByRankTask);
            batch.Execute();

            await Task.WhenAll(tasks.ToArray());

            var rangeByRankSortedSetValues = rangeByRankTask.Result;

            int size = rangeByRankSortedSetValues.Length;
            Assert.Equal(1, size);
            string firstRedisValue = rangeByRankSortedSetValues.FirstOrDefault().ToString();
            Assert.Equal("a", firstRedisValue);
        }

        [Fact]
        public async Task Execute_Transaction()
        {
            await using var conn = Create();
            var db = conn.GetDatabase();

            var me = Me();
            var key = me + ":1";
            var key2 = me + ":2";
            var keyIntersect = me + ":result";

            db.KeyDelete(key);
            db.KeyDelete(key2);
            db.KeyDelete(keyIntersect);
            db.SortedSetAdd(key, "a", 1345);

            var tasks = new List<Task>();
            var batch = db.CreateTransaction();
            tasks.Add(batch.SortedSetAddAsync(key2, "a", 4567));
            tasks.Add(batch.SortedSetCombineAndStoreAsync(SetOperation.Intersect, keyIntersect, [key, key2]));
            var rangeByRankTask = batch.SortedSetRangeByRankAsync(keyIntersect);
            tasks.Add(rangeByRankTask);
            batch.Execute();

            await Task.WhenAll(tasks.ToArray());

            var rangeByRankSortedSetValues = rangeByRankTask.Result;

            int size = rangeByRankSortedSetValues.Length;
            Assert.Equal(1, size);
            string firstRedisValue = rangeByRankSortedSetValues.FirstOrDefault().ToString();
            Assert.Equal("a", firstRedisValue);
        }
    }
}
