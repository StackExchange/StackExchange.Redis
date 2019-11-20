using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Sets : TestBase
    {
        public Sets(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

        [Fact]
        public void SScan()
        {
            using (var conn = Create())
            {
                var server = GetAnyMaster(conn);

                RedisKey key = Me();
                var db = conn.GetDatabase();
                int totalUnfiltered = 0, totalFiltered = 0;
                for (int i = 1; i < 1001; i++)
                {
                    db.SetAdd(key, i, CommandFlags.FireAndForget);
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
                Assert.Throws<ArgumentNullException>(() => db.SetRemove(key, values));
                await Assert.ThrowsAsync<ArgumentNullException>(async () => await db.SetRemoveAsync(key, values).ForAwait()).ForAwait();

                values = new RedisValue[0];
                Assert.Equal(0, db.SetRemove(key, values));
                Assert.Equal(0, await db.SetRemoveAsync(key, values).ForAwait());
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

                db.KeyDelete(key, CommandFlags.FireAndForget);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAddAsync(key, i, CommandFlags.FireAndForget);
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

                db.KeyDelete(key, CommandFlags.FireAndForget);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i, CommandFlags.FireAndForget);
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

                db.KeyDelete(key, CommandFlags.FireAndForget);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i, CommandFlags.FireAndForget);
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

                db.KeyDelete(key, CommandFlags.FireAndForget);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i, CommandFlags.FireAndForget);
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

                db.KeyDelete(key, CommandFlags.FireAndForget);
                for (int i = 1; i < 11; i++)
                {
                    db.SetAdd(key, i, CommandFlags.FireAndForget);
                }

                var t = db.SetPopAsync(key, count: 0);
                Assert.True(t.IsCompleted); // sync
                var arr = await t;
                Assert.Empty(arr);

                Assert.Equal(10, db.SetLength(key));
            }
        }

        [Fact]
        public void SetAdd_Zero()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);

                var result = db.SetAdd(key, new RedisValue[0]);
                Assert.Equal(0, result);

                Assert.Equal(0, db.SetLength(key));
            }
        }

        [Fact]
        public async Task SetAdd_Zero_Async()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);

                var t = db.SetAddAsync(key, new RedisValue[0]);
                Assert.True(t.IsCompleted); // sync
                var count = await t;
                Assert.Equal(0, count);

                Assert.Equal(0, db.SetLength(key));
            }
        }
    }
}
