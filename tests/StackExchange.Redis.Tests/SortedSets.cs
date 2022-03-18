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
                Assert.NotNull(arr);
                Assert.Empty(arr);

                Assert.Equal(10, db.SortedSetLength(key));
            }
        }
    }
}
