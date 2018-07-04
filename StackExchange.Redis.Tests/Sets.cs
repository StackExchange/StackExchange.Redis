using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Sets : TestBase
    {
        public Sets(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void SScan()
        {
            using (var conn = Create())
            {
                var server = GetAnyMaster(conn);

                RedisKey key = "a";
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                int totalUnfiltered = 0, totalFiltered = 0;
                for (int i = 0; i < 1000; i++)
                {
                    db.SetAdd(key, i);
                    totalUnfiltered += i;
                    if (i.ToString().Contains("3")) totalFiltered += i;
                }
                var unfilteredActual = db.SetScan(key).Select(x => (int)x).Sum();
                Assert.Equal(totalUnfiltered, unfilteredActual);
                if (server.Features.Scan)
                {
                    var filteredActual = db.SetScan(key, "*3*").Select(x => (int)x).Sum();
                    Assert.Equal(totalFiltered, filteredActual);
                }
            }
        }

        [Fact]
        public async Task SetRemoveArgTests()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var key = Me();

                RedisValue[] values = null;
                Assert.Throws<ArgumentNullException>(() => db.SetRemove(key, values, CommandFlags.HighPriority));
                await Assert.ThrowsAsync<ArgumentNullException>(async () => await db.SetRemoveAsync(key, values, CommandFlags.HighPriority).ForAwait()).ForAwait();

                values = new RedisValue[0];
                Assert.Equal(0, db.SetRemove(key, values, CommandFlags.HighPriority));
                Assert.Equal(0, await db.SetRemoveAsync(key, values, CommandFlags.HighPriority).ForAwait());
            }
        }

        [Fact]
        public void SetPopMulti_Multi()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.SetPopMultiple), r => r.SetPopMultiple);

                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i);
                }

                var random = db.SetPop(key);
                Assert.False(random.IsNull);
                Assert.True((int)random > 0);
                Assert.True((int)random <= 10);
                Assert.Equal(9, db.SetLength(key));

                var moreRandoms = db.SetPop(key, 2);
                Assert.Equal(2, moreRandoms.Length);
                Assert.False(moreRandoms[0].IsNull);
                Assert.Equal(7, db.SetLength(key));
            }
        }
        [Fact]
        public void SetPopMulti_Single()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i);
                }

                var random = db.SetPop(key);
                Assert.False(random.IsNull);
                Assert.True((int)random > 0);
                Assert.True((int)random <= 10);
                Assert.Equal(9, db.SetLength(key));

                var moreRandoms = db.SetPop(key, 1);
                Assert.Single(moreRandoms);
                Assert.False(moreRandoms[0].IsNull);
                Assert.Equal(8, db.SetLength(key));
            }
        }

        [Fact]
        public async Task SetPopMulti_Multi_Async()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.SetPopMultiple), r => r.SetPopMultiple);

                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i);
                }

                var random = await db.SetPopAsync(key).ForAwait();
                Assert.False(random.IsNull);
                Assert.True((int)random > 0);
                Assert.True((int)random <= 10);
                Assert.Equal(9, db.SetLength(key));

                var moreRandoms = await db.SetPopAsync(key, 2).ForAwait();
                Assert.Equal(2, moreRandoms.Length);
                Assert.False(moreRandoms[0].IsNull);
                Assert.Equal(7, db.SetLength(key));
            }
        }

        [Fact]
        public async Task SetPopMulti_Single_Async()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i);
                }

                var random = await db.SetPopAsync(key).ForAwait();
                Assert.False(random.IsNull);
                Assert.True((int)random > 0);
                Assert.True((int)random <= 10);
                Assert.Equal(9, db.SetLength(key));

                var moreRandoms = db.SetPop(key, 1);
                Assert.Single(moreRandoms);
                Assert.False(moreRandoms[0].IsNull);
                Assert.Equal(8, db.SetLength(key));
            }
        }

        [Fact]
        public async Task SetPopMulti_Zero_Async()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i);
                }

                var t = db.SetPopAsync(key, count: 0);
                Assert.True(t.IsCompleted); // sync
                var arr = await t;
                Assert.Empty(arr);

                Assert.Equal(10, db.SetLength(key));
            }
        }
    }
}
