using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue2176Tests : TestBase
    {
        public Issue2176Tests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Execute_Batch()
        {
            using var conn = Create();
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
            tasks.Add(batch.SortedSetCombineAndStoreAsync(SetOperation.Intersect,
                keyIntersect, new RedisKey[] { key, key2 }));
            var rangeByRankTask = batch.SortedSetRangeByRankAsync(keyIntersect);
            tasks.Add(rangeByRankTask);
            batch.Execute();

            Task.WhenAll(tasks.ToArray());

            var rangeByRankSortedSetValues = rangeByRankTask.Result;

            int size = rangeByRankSortedSetValues.Length;
            Assert.Equal(1, size);
            string firstRedisValue = rangeByRankSortedSetValues.FirstOrDefault().ToString();
            Assert.Equal("a", firstRedisValue);
        }

        [Fact]
        public void Execute_Transaction()
        {
            using var conn = Create();
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
            tasks.Add(batch.SortedSetCombineAndStoreAsync(SetOperation.Intersect,
                keyIntersect, new RedisKey[] { key, key2 }));
            var rangeByRankTask = batch.SortedSetRangeByRankAsync(keyIntersect);
            tasks.Add(rangeByRankTask);
            batch.Execute();

            Task.WhenAll(tasks.ToArray());

            var rangeByRankSortedSetValues = rangeByRankTask.Result;

            int size = rangeByRankSortedSetValues.Length;
            Assert.Equal(1, size);
            string firstRedisValue = rangeByRankSortedSetValues.FirstOrDefault().ToString();
            Assert.Equal("a", firstRedisValue);
        }
    }
}
