using System.Linq;
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
        public void SortedSetPopMulti_Multi()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.SortedSetPop), r => r.SortedSetPop);

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
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.SortedSetPop), r => r.SortedSetPop);

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
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.SortedSetPop), r => r.SortedSetPop);

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
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.SortedSetPop), r => r.SortedSetPop);

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
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.SortedSetPop), r => r.SortedSetPop);

                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.SortedSetAdd(key, entries, CommandFlags.FireAndForget);

                var t = db.SortedSetPopAsync(key, count: 0);
                Assert.True(t.IsCompleted); // sync
                var arr = await t;
                Assert.Empty(arr);

                Assert.Equal(10, db.SortedSetLength(key));
            }
        }

        [Fact]
        public async Task SortedSetRangeByRankAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, entries, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByRankAndStoreAsync(destinationKey, sourceKey);
                Assert.Equal(entries.Length, res);
            }
        }

        [Fact]
        public async Task SortedSetRangeByRankLimitedAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, entries, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByRankAndStoreAsync(destinationKey, sourceKey, 1, 4);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(4, res);
                for (var i = 1; i < 5; i++)
                {
                    Assert.Equal(entries[i], range[i-1]);
                }
            }
        }

        [Fact]
        public async Task SortedSetRangeByScoreAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByScoreAndStoreAsync(destinationKey, sourceKey, 64, 128);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(2, res);
                for (var i = 6; i < 8; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i-6]);
                }
            }
        }

        [Fact]
        public async Task SortedSetRangeByScoreAsyncDefault()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByScoreAndStoreAsync(destinationKey, sourceKey);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(10, res);
                for (var i = 0; i < entriesPow2.Length; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i]);
                }
            }
        }

        [Fact]
        public async Task SortedSetRangeByScoreAsyncLimited()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByScoreAndStoreAsync(destinationKey, sourceKey, skip: 1, take: 6);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(6, res);
                for (var i = 1; i < 7; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i-1]);
                }
            }
        }

        [Fact]
        public async Task SortedSetRangeByScoreAsyncExclusiveRange()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByScoreAndStoreAsync(destinationKey, sourceKey, 32, 256, Exclude.Both);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(2, res);
                for (var i = 6; i < 8; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i-6]);
                }
            }
        }

        [Fact]
        public async Task SortedSetRangeByScoreAsyncReverse()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByScoreAndStoreAsync(destinationKey, sourceKey, start: double.PositiveInfinity, double.NegativeInfinity, order: Order.Descending);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(10, res);
                for (var i = 0; i < entriesPow2.Length; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i]);
                }
            }
        }

        [Fact]
        public async Task SortedSetRangeByLexAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, lexEntries, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByValueAndStoreAsync(destinationKey, sourceKey, "a", "j", Exclude.None);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(10, res);
                for (var i = 0; i <lexEntries.Length; i++)
                {
                    Assert.Equal(lexEntries[i], range[i]);
                }
            }
        }

        [Fact]
        public async Task SortedSetRangeByLexExlusiveRangeAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, lexEntries, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByValueAndStoreAsync(destinationKey, sourceKey, "a", "j", Exclude.Both);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(8, res);
                for (var i = 1; i <lexEntries.Length-1; i++)
                {
                    Assert.Equal(lexEntries[i], range[i-1]);
                }
            }
        }

        [Fact]
        public async Task SortedSetRangeByLexRevRangeAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                await db.SortedSetAddAsync(sourceKey, lexEntries, CommandFlags.FireAndForget);
                var res = await db.SortedSetRangeByValueAndStoreAsync(destinationKey, sourceKey, "j", "a", Exclude.None, Order.Descending);
                var range = await db.SortedSetRangeByRankWithScoresAsync(destinationKey);
                Assert.Equal(10, res);
                for (var i = 0; i < lexEntries.Length; i++)
                {
                    Assert.Equal(lexEntries[i], range[i]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByRank()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, entries, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByRankAndStore(destinationKey, sourceKey);
                Assert.Equal(entries.Length, res);
            }
        }

        [Fact]
        public void SortedSetRangeByRankLimited()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, entries, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByRankAndStore(destinationKey, sourceKey, 1, 4);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(4, res);
                for (var i = 1; i < 5; i++)
                {
                    Assert.Equal(entries[i], range[i-1]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByScore()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByScoreAndStore(destinationKey, sourceKey, 64, 128);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(2, res);
                for (var i = 6; i < 8; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i-6]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByScoreDefault()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByScoreAndStore(destinationKey, sourceKey);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(10, res);
                for (var i = 0; i < entriesPow2.Length; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByScoreLimited()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByScoreAndStore(destinationKey, sourceKey, skip: 1, take: 6);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(6, res);
                for (var i = 1; i < 7; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i-1]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByScoreExclusiveRange()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByScoreAndStore(destinationKey, sourceKey, 32, 256, Exclude.Both);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(2, res);
                for (var i = 6; i < 8; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i-6]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByScoreReverse()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, entriesPow2, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByScoreAndStore(destinationKey, sourceKey, start: double.PositiveInfinity, double.NegativeInfinity, order: Order.Descending);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(10, res);
                for (var i = 0; i < entriesPow2.Length; i++)
                {
                    Assert.Equal(entriesPow2[i], range[i]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByLex()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, lexEntries, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByValueAndStore(destinationKey, sourceKey, "a", "j", Exclude.None);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(10, res);
                for (var i = 0; i <lexEntries.Length; i++)
                {
                    Assert.Equal(lexEntries[i], range[i]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByLexExlusiveRange()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, lexEntries, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByValueAndStore(destinationKey, sourceKey, "a", "j", Exclude.Both);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(8, res);
                for (var i = 1; i <lexEntries.Length-1; i++)
                {
                    Assert.Equal(lexEntries[i], range[i-1]);
                }
            }
        }

        [Fact]
        public void SortedSetRangeByLexRevRange()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var me = Me();
                var sourceKey = $"{me}:ZSetSource";
                var destinationKey = $"{me}:ZSetDestination";

                db.KeyDelete(new RedisKey[] {sourceKey, destinationKey}, CommandFlags.FireAndForget);
                db.SortedSetAdd(sourceKey, lexEntries, CommandFlags.FireAndForget);
                var res = db.SortedSetRangeByValueAndStore(destinationKey, sourceKey, "j", "a", Exclude.None, Order.Descending);
                var range = db.SortedSetRangeByRankWithScores(destinationKey);
                Assert.Equal(10, res);
                for (var i = 0; i < lexEntries.Length; i++)
                {
                    Assert.Equal(lexEntries[i], range[i]);
                }
            }
        }
    }
}
