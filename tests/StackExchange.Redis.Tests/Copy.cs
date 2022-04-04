using System;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Copy : TestBase
    {
        public Copy(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

        [Fact]
        public async Task Basic()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                var src = Me();
                var dest = Me() + "2";
                _ = db.KeyDelete(dest);

                _ = db.StringSetAsync(src, "Heyyyyy");
                var ke1 = db.KeyCopyAsync(src, dest).ForAwait();
                var ku1 = db.StringGet(dest);
                Assert.True(await ke1);
                Assert.True(ku1.Equals("Heyyyyy"));
            }
        }

        [Fact]
        public async Task CrossDB()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                var dbDestId = TestConfig.GetDedicatedDB(muxer);
                var dbDest = muxer.GetDatabase(dbDestId);

                var src = Me();
                var dest = Me() + "2";
                dbDest.KeyDelete(dest);

                _ = db.StringSetAsync(src, "Heyyyyy");
                var ke1 = db.KeyCopyAsync(src, dest, dbDestId).ForAwait();
                var ku1 = dbDest.StringGet(dest);
                Assert.True(await ke1);
                Assert.True(ku1.Equals("Heyyyyy"));
            }
        }

        [Fact]
        public async Task WithReplace()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                var src = Me();
                var dest = Me() + "2";
                _ = db.StringSetAsync(src, "foo1");
                _ = db.StringSetAsync(dest, "foo2");
                var ke1 = db.KeyCopyAsync(src, dest).ForAwait();
                var ke2 = db.KeyCopyAsync(src, dest, replace: true).ForAwait();
                var ku1 = db.StringGet(dest);
                Assert.False(await ke1); // Should fail when not using replace and destination key exist
                Assert.True(await ke2);
                Assert.True(ku1.Equals("foo1"));
            }
        }
    }
}
