using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class SortedSets : TestBase
    {
        public SortedSets(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        private static readonly SortedSetEntry[] entries = new SortedSetEntry[]
        {
            new SortedSetEntry("a", 1),
            new SortedSetEntry("b", 2),
            new SortedSetEntry("c", 3),
            new SortedSetEntry("d", 4),
            new SortedSetEntry("e", 5),
            new SortedSetEntry("f", 6),
            new SortedSetEntry("g", 7),
            new SortedSetEntry("h", 8),
            new SortedSetEntry("i", 9),
            new SortedSetEntry("j", 10)
        };

        private static readonly SortedSetEntry[] entriesPow2 = new SortedSetEntry[]
        {
            new SortedSetEntry("a", 1),
            new SortedSetEntry("b", 2),
            new SortedSetEntry("c", 4),
            new SortedSetEntry("d", 8),
            new SortedSetEntry("e", 16),
            new SortedSetEntry("f", 32),
            new SortedSetEntry("g", 64),
            new SortedSetEntry("h", 128),
            new SortedSetEntry("i", 256),
            new SortedSetEntry("j", 512)
        };

        private static readonly SortedSetEntry[] entriesPow3 = new SortedSetEntry[]
        {
            new SortedSetEntry("a", 1),
            new SortedSetEntry("c", 4),
            new SortedSetEntry("e", 16),
            new SortedSetEntry("g", 64),
            new SortedSetEntry("i", 256),
        };

        private static readonly SortedSetEntry[] lexEntries = new SortedSetEntry[]
        {
            new SortedSetEntry("a", 0),
            new SortedSetEntry("b", 0),
            new SortedSetEntry("c", 0),
            new SortedSetEntry("d", 0),
            new SortedSetEntry("e", 0),
            new SortedSetEntry("f", 0),
            new SortedSetEntry("g", 0),
            new SortedSetEntry("h", 0),
            new SortedSetEntry("i", 0),
            new SortedSetEntry("j", 0)
        };

        [Fact]
        public void SortedSetCombine()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            var diff = db.SortedSetCombine(SetOperation.Difference, new RedisKey[]{ key1, key2});
            Assert.Equal(5, diff.Length);
            Assert.Equal("b", diff[0]);

            var inter = db.SortedSetCombine(SetOperation.Intersect, new RedisKey[]{ key1, key2});
            Assert.Equal(5, inter.Length);
            Assert.Equal("a", inter[0]);

            var union = db.SortedSetCombine(SetOperation.Union, new RedisKey[]{ key1, key2});
            Assert.Equal(10, union.Length);
            Assert.Equal("a", union[0]);
        }


        [Fact]
        public async Task SortedSetCombineAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            var diff = await db.SortedSetCombineAsync(SetOperation.Difference, new RedisKey[] { key1, key2 });
            Assert.Equal(5, diff.Length);
            Assert.Equal("b", diff[0]);

            var inter = await db.SortedSetCombineAsync(SetOperation.Intersect, new RedisKey[] { key1, key2 });
            Assert.Equal(5, inter.Length);
            Assert.Equal("a", inter[0]);

            var union = await db.SortedSetCombineAsync(SetOperation.Union, new RedisKey[] { key1, key2 });
            Assert.Equal(10, union.Length);
            Assert.Equal("a", union[0]);
        }

        [Fact]
        public void SortedSetCombineWithScores()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            var diff = db.SortedSetCombineWithScores(SetOperation.Difference, new RedisKey[]{ key1, key2});
            Assert.Equal(5, diff.Length);
            Assert.Equal(new SortedSetEntry("b", 2), diff[0]);

            var inter = db.SortedSetCombineWithScores(SetOperation.Intersect, new RedisKey[]{ key1, key2});
            Assert.Equal(5, inter.Length);
            Assert.Equal(new SortedSetEntry("a", 2), inter[0]);

            var union = db.SortedSetCombineWithScores(SetOperation.Union, new RedisKey[]{ key1, key2});
            Assert.Equal(10, union.Length);
            Assert.Equal(new SortedSetEntry("a", 2), union[0]);
        }


        [Fact]
        public async Task SortedSetCombineWithScoresAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            var diff = await db.SortedSetCombineWithScoresAsync(SetOperation.Difference, new RedisKey[] { key1, key2 });
            Assert.Equal(5, diff.Length);
            Assert.Equal(new SortedSetEntry("b", 2), diff[0]);

            var inter = await db.SortedSetCombineWithScoresAsync(SetOperation.Intersect, new RedisKey[] { key1, key2 });
            Assert.Equal(5, inter.Length);
            Assert.Equal(new SortedSetEntry("a", 2), inter[0]);

            var union = await db.SortedSetCombineWithScoresAsync(SetOperation.Union, new RedisKey[] { key1, key2 });
            Assert.Equal(10, union.Length);
            Assert.Equal(new SortedSetEntry("a", 2), union[0]);
        }

        [Fact]
        public void SortedSetCombineAndStore()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);
            var destination = Me() + "dest";
            db.KeyDelete(destination, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            var diff = db.SortedSetCombineAndStore(SetOperation.Difference, destination, new RedisKey[]{ key1, key2});
            Assert.Equal(5, diff);

            var inter = db.SortedSetCombineAndStore(SetOperation.Intersect, destination, new RedisKey[]{ key1, key2});
            Assert.Equal(5, inter);

            var union = db.SortedSetCombineAndStore(SetOperation.Union, destination, new RedisKey[]{ key1, key2});
            Assert.Equal(10, union);
        }


        [Fact]
        public async Task SortedSetCombineAndStoreAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);
            var destination = Me() + "dest";
            db.KeyDelete(destination, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            var diff = await db.SortedSetCombineAndStoreAsync(SetOperation.Difference, destination, new RedisKey[] { key1, key2 });
            Assert.Equal(5, diff);

            var inter = await db.SortedSetCombineAndStoreAsync(SetOperation.Intersect, destination, new RedisKey[] { key1, key2 });
            Assert.Equal(5, inter);

            var union = await db.SortedSetCombineAndStoreAsync(SetOperation.Union, destination, new RedisKey[] { key1, key2 });
            Assert.Equal(10, union);
        }

        [Fact]
        public async Task SortedSetCombineErrors()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);
            var destination = Me() + "dest";
            db.KeyDelete(destination, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            // ZDIFF can't be used with weights
            var ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombine(SetOperation.Difference, new RedisKey[] { key1, key2 }, new double[] { 1, 2 }));
            Assert.Equal("ZDIFF cannot be used with weights or aggregation.", ex.Message);
            ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombineWithScores(SetOperation.Difference, new RedisKey[] { key1, key2 }, new double[] { 1, 2 }));
            Assert.Equal("ZDIFF cannot be used with weights or aggregation.", ex.Message);
            ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombineAndStore(SetOperation.Difference, destination, new RedisKey[] { key1, key2 }, new double[] { 1, 2 }));
            Assert.Equal("ZDIFFSTORE cannot be used with weights or aggregation.", ex.Message);
            // and Async...
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineAsync(SetOperation.Difference, new RedisKey[] { key1, key2 }, new double[] { 1, 2 }));
            Assert.Equal("ZDIFF cannot be used with weights or aggregation.", ex.Message);
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineWithScoresAsync(SetOperation.Difference, new RedisKey[] { key1, key2 }, new double[] { 1, 2 }));
            Assert.Equal("ZDIFF cannot be used with weights or aggregation.", ex.Message);
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineAndStoreAsync(SetOperation.Difference, destination, new RedisKey[] { key1, key2 }, new double[] { 1, 2 }));
            Assert.Equal("ZDIFFSTORE cannot be used with weights or aggregation.", ex.Message);

            // ZDIFF can't be used with aggregation
            ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombine(SetOperation.Difference, new RedisKey[] { key1, key2 }, aggregate: Aggregate.Max));
            Assert.Equal("ZDIFF cannot be used with weights or aggregation.", ex.Message);
            ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombineWithScores(SetOperation.Difference, new RedisKey[] { key1, key2 }, aggregate: Aggregate.Max));
            Assert.Equal("ZDIFF cannot be used with weights or aggregation.", ex.Message);
            ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombineAndStore(SetOperation.Difference, destination, new RedisKey[] { key1, key2 }, aggregate: Aggregate.Max));
            Assert.Equal("ZDIFFSTORE cannot be used with weights or aggregation.", ex.Message);
            // and Async...
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineAsync(SetOperation.Difference, new RedisKey[] { key1, key2 }, aggregate: Aggregate.Max));
            Assert.Equal("ZDIFF cannot be used with weights or aggregation.", ex.Message);
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineWithScoresAsync(SetOperation.Difference, new RedisKey[] { key1, key2 }, aggregate: Aggregate.Max));
            Assert.Equal("ZDIFF cannot be used with weights or aggregation.", ex.Message);
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineAndStoreAsync(SetOperation.Difference, destination, new RedisKey[] { key1, key2 }, aggregate: Aggregate.Max));
            Assert.Equal("ZDIFFSTORE cannot be used with weights or aggregation.", ex.Message);

            // Too many weights
            ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombine(SetOperation.Union, new RedisKey[] { key1, key2 }, new double[] { 1, 2, 3 }));
            Assert.StartsWith("Keys and weights should have the same number of elements.", ex.Message);
            ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombineWithScores(SetOperation.Union, new RedisKey[] { key1, key2 }, new double[] { 1, 2, 3 }));
            Assert.StartsWith("Keys and weights should have the same number of elements.", ex.Message);
            ex = Assert.Throws<ArgumentException>(() => db.SortedSetCombineAndStore(SetOperation.Union, destination, new RedisKey[] { key1, key2 }, new double[] { 1, 2, 3 }));
            Assert.StartsWith("Keys and weights should have the same number of elements.", ex.Message);
            // and Async...
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineAsync(SetOperation.Union, new RedisKey[] { key1, key2 }, new double[] { 1, 2, 3 }));
            Assert.StartsWith("Keys and weights should have the same number of elements.", ex.Message);
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineWithScoresAsync(SetOperation.Union, new RedisKey[] { key1, key2 }, new double[] { 1, 2, 3 }));
            Assert.StartsWith("Keys and weights should have the same number of elements.", ex.Message);
            ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SortedSetCombineAndStoreAsync(SetOperation.Union, destination, new RedisKey[] { key1, key2 }, new double[] { 1, 2, 3 }));
            Assert.StartsWith("Keys and weights should have the same number of elements.", ex.Message);
        }

        [Fact]
        public void SortedSetIntersectionLength()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v7_0_0_rc1);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            var inter = db.SortedSetIntersectionLength(new RedisKey[]{ key1, key2});
            Assert.Equal(5, inter);

            // with limit
            inter = db.SortedSetIntersectionLength(new RedisKey[]{ key1, key2}, 3);
            Assert.Equal(3, inter);
        }

        [Fact]
        public async Task SortedSetIntersectionLengthAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v7_0_0_rc1);

            var db = conn.GetDatabase();
            var key1 = Me();
            db.KeyDelete(key1, CommandFlags.FireAndForget);
            var key2 = Me() + "2";
            db.KeyDelete(key2, CommandFlags.FireAndForget);

            db.SortedSetAdd(key1, entries);
            db.SortedSetAdd(key2, entriesPow3);

            var inter = await db.SortedSetIntersectionLengthAsync(new RedisKey[] { key1, key2 });
            Assert.Equal(5, inter);

            // with limit
            inter = await db.SortedSetIntersectionLengthAsync(new RedisKey[] { key1, key2 }, 3);
            Assert.Equal(3, inter);
        }

        [Fact]
        public void SortedSetPopMulti_Multi()
        {
            using (var conn = Create())
            {
                Skip.IfBelow(conn, RedisFeatures.v5_0_0);

                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.SortedSetAdd(key, entries, CommandFlags.FireAndForget);

                var first = db.SortedSetPop(key, Order.Ascending);
                Assert.True(first.HasValue);
                Assert.Equal(entries[0], first.Value);
                Assert.Equal(9, db.SortedSetLength(key));

                var lasts = db.SortedSetPop(key, 2, Order.Descending);
                Assert.Equal(2, lasts.Length);
                Assert.Equal(entries[9], lasts[0]);
                Assert.Equal(entries[8], lasts[1]);
                Assert.Equal(7, db.SortedSetLength(key));
            }
        }

        [Fact]
        public void SortedSetPopMulti_Single()
        {
            using (var conn = Create())
            {
                Skip.IfBelow(conn, RedisFeatures.v5_0_0);

                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.SortedSetAdd(key, entries, CommandFlags.FireAndForget);

                var last = db.SortedSetPop(key, Order.Descending);
                Assert.True(last.HasValue);
                Assert.Equal(entries[9], last.Value);
                Assert.Equal(9, db.SortedSetLength(key));

                var firsts = db.SortedSetPop(key, 1, Order.Ascending);
                Assert.Single(firsts);
                Assert.Equal(entries[0], firsts[0]);
                Assert.Equal(8, db.SortedSetLength(key));
            }
        }

        [Fact]
        public async Task SortedSetPopMulti_Multi_Async()
        {
            using (var conn = Create())
            {
                Skip.IfBelow(conn, RedisFeatures.v5_0_0);

                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.SortedSetAdd(key, entries, CommandFlags.FireAndForget);

                var last = await db.SortedSetPopAsync(key, Order.Descending).ForAwait();
                Assert.True(last.HasValue);
                Assert.Equal(entries[9], last.Value);
                Assert.Equal(9, db.SortedSetLength(key));

                var moreLasts = await db.SortedSetPopAsync(key, 2, Order.Descending).ForAwait();
                Assert.Equal(2, moreLasts.Length);
                Assert.Equal(entries[8], moreLasts[0]);
                Assert.Equal(entries[7], moreLasts[1]);
                Assert.Equal(7, db.SortedSetLength(key));
            }
        }

        [Fact]
        public async Task SortedSetPopMulti_Single_Async()
        {
            using (var conn = Create())
            {
                Skip.IfBelow(conn, RedisFeatures.v5_0_0);

                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.SortedSetAdd(key, entries, CommandFlags.FireAndForget);

                var first = await db.SortedSetPopAsync(key).ForAwait();
                Assert.True(first.HasValue);
                Assert.Equal(entries[0], first.Value);
                Assert.Equal(9, db.SortedSetLength(key));

                var moreFirsts = await db.SortedSetPopAsync(key, 1).ForAwait();
                Assert.Single(moreFirsts);
                Assert.Equal(entries[1], moreFirsts[0]);
                Assert.Equal(8, db.SortedSetLength(key));
            }
        }

        [Fact]
        public async Task SortedSetPopMulti_Zero_Async()
        {
            using (var conn = Create())
            {
                Skip.IfBelow(conn, RedisFeatures.v5_0_0);

                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.SortedSetAdd(key, entries, CommandFlags.FireAndForget);

                var t = db.SortedSetPopAsync(key, count: 0);
                Assert.True(t.IsCompleted); // sync
                var arr = await t;
                Assert.NotNull(arr);
                Assert.Empty(arr);

                Assert.Equal(10, db.SortedSetLength(key));
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByRankAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);
            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, entries, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, 0, -1);
            Assert.Equal(entries.Length, res);
        }

        [Fact]
        public async Task SortedSetRangeStoreByRankLimitedAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, entries, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, 1, 4);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(4, res);
            for (var i = 1; i < 5; i++)
            {
                Assert.Equal(entries[i], range[i-1]);
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByScoreAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, 64, 128, SortedSetOrder.ByScore);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(2, res);
            for (var i = 6; i < 8; i++)
            {
                Assert.Equal(entriesPow2[i], range[i-6]);
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByScoreAsyncDefault()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, double.NegativeInfinity, double.PositiveInfinity, SortedSetOrder.ByScore);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(10, res);
            for (var i = 0; i < entriesPow2.Length; i++)
            {
                Assert.Equal(entriesPow2[i], range[i]);
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByScoreAsyncLimited()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, double.NegativeInfinity, double.PositiveInfinity, SortedSetOrder.ByScore, skip: 1, take: 6);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(6, res);
            for (var i = 1; i < 7; i++)
            {
                Assert.Equal(entriesPow2[i], range[i-1]);
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByScoreAsyncExclusiveRange()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, 32, 256, SortedSetOrder.ByScore, exclude: Exclude.Both);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(2, res);
            for (var i = 6; i < 8; i++)
            {
                Assert.Equal(entriesPow2[i], range[i-6]);
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByScoreAsyncReverse()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, start: double.PositiveInfinity, double.NegativeInfinity, SortedSetOrder.ByScore, order: Order.Descending);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(10, res);
            for (var i = 0; i < entriesPow2.Length; i++)
            {
                Assert.Equal(entriesPow2[i], range[i]);
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByLexAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, lexEntries, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, "a", "j", SortedSetOrder.ByLex);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(10, res);
            for (var i = 0; i <lexEntries.Length; i++)
            {
                Assert.Equal(lexEntries[i], range[i]);
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByLexExclusiveRangeAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, lexEntries, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, "a", "j", SortedSetOrder.ByLex, Exclude.Both);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(8, res);
            for (var i = 1; i <lexEntries.Length-1; i++)
            {
                Assert.Equal(lexEntries[i], range[i-1]);
            }
        }

        [Fact]
        public async Task SortedSetRangeStoreByLexRevRangeAsync()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            await db.SortedSetAddAsync(sourceKey, lexEntries, CommandFlags.FireAndForget);
            var res = await db.SortedSetRangeAndStoreAsync(sourceKey, destinationKey, "j", "a", SortedSetOrder.ByLex, exclude:Exclude.None, order: Order.Descending);
            var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
            Assert.Equal(10, res);
            for (var i = 0; i < lexEntries.Length; i++)
            {
                Assert.Equal(lexEntries[i], range[i]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByRank()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, entries, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, 0, -1);
            Assert.Equal(entries.Length, res);
        }

        [Fact]
        public void SortedSetRangeStoreByRankLimited()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, entries, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, 1, 4);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(4, res);
            for (var i = 1; i < 5; i++)
            {
                Assert.Equal(entries[i], range[i-1]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByScore()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, 64, 128, SortedSetOrder.ByScore);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(2, res);
            for (var i = 6; i < 8; i++)
            {
                Assert.Equal(entriesPow2[i], range[i-6]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByScoreDefault()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, double.NegativeInfinity, double.PositiveInfinity, SortedSetOrder.ByScore);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(10, res);
            for (var i = 0; i < entriesPow2.Length; i++)
            {
                Assert.Equal(entriesPow2[i], range[i]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByScoreLimited()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey,double.NegativeInfinity, double.PositiveInfinity, SortedSetOrder.ByScore, skip: 1, take: 6);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(6, res);
            for (var i = 1; i < 7; i++)
            {
                Assert.Equal(entriesPow2[i], range[i-1]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByScoreExclusiveRange()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, 32, 256, SortedSetOrder.ByScore, exclude: Exclude.Both);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(2, res);
            for (var i = 6; i < 8; i++)
            {
                Assert.Equal(entriesPow2[i], range[i-6]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByScoreReverse()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, start: double.PositiveInfinity, double.NegativeInfinity, SortedSetOrder.ByScore, order: Order.Descending);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(10, res);
            for (var i = 0; i < entriesPow2.Length; i++)
            {
                Assert.Equal(entriesPow2[i], range[i]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByLex()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, lexEntries, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, "a", "j", SortedSetOrder.ByLex);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(10, res);
            for (var i = 0; i <lexEntries.Length; i++)
            {
                Assert.Equal(lexEntries[i], range[i]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByLexExclusiveRange()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, lexEntries, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, "a", "j", SortedSetOrder.ByLex, Exclude.Both);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(8, res);
            for (var i = 1; i <lexEntries.Length-1; i++)
            {
                Assert.Equal(lexEntries[i], range[i-1]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreByLexRevRange()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, lexEntries, CommandFlags.FireAndForget);
            var res = db.SortedSetRangeAndStore(sourceKey, destinationKey, "j", "a", SortedSetOrder.ByLex, Exclude.None, Order.Descending);
            var range = db.SortedSetRangeByRankWithScores(destinationKey);
            Assert.Equal(10, res);
            for (var i = 0; i < lexEntries.Length; i++)
            {
                Assert.Equal(lexEntries[i], range[i]);
            }
        }

        [Fact]
        public void SortedSetRangeStoreFailErroneousTake()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, lexEntries, CommandFlags.FireAndForget);
            var exception = Assert.Throws<ArgumentException>(()=>db.SortedSetRangeAndStore(sourceKey, destinationKey,0,-1, take:5));
            Assert.Equal("take", exception.ParamName);
        }

        [Fact]
        public void SortedSetRangeStoreFailExclude()
        {
            using var conn = Create();
            Skip.IfBelow(conn, RedisFeatures.v6_2_0);

            var db = conn.GetDatabase();
            var me = Me();
            var sourceKey = $"{me}:ZSetSource";
            var destinationKey = $"{me}:ZSetDestination";

            db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
            db.SortedSetAdd(sourceKey, lexEntries, CommandFlags.FireAndForget);
            var exception = Assert.Throws<ArgumentException>(()=>db.SortedSetRangeAndStore(sourceKey, destinationKey,0,-1, exclude: Exclude.Both));
            Assert.Equal("exclude", exception.ParamName);
        }
    }
}
