using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class CopyTests : TestBase
{
    public CopyTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

    [Fact]
    public async Task Basic()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var src = Me();
        var dest = Me() + "2";
        _ = db.KeyDelete(dest);

        _ = db.StringSetAsync(src, "Heyyyyy");
        var ke1 = db.KeyCopyAsync(src, dest).ForAwait();
        var ku1 = db.StringGet(dest);
        Assert.True(await ke1);
        Assert.True(ku1.Equals("Heyyyyy"));
    }

    [Fact]
    public async Task CrossDB()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var dbDestId = TestConfig.GetDedicatedDB(conn);
        var dbDest = conn.GetDatabase(dbDestId);

        var src = Me();
        var dest = Me() + "2";
        dbDest.KeyDelete(dest);

        _ = db.StringSetAsync(src, "Heyyyyy");
        var ke1 = db.KeyCopyAsync(src, dest, dbDestId).ForAwait();
        var ku1 = dbDest.StringGet(dest);
        Assert.True(await ke1);
        Assert.True(ku1.Equals("Heyyyyy"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.KeyCopyAsync(src, dest, destinationDatabase: -10));
    }

    [Fact]
    public async Task WithReplace()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
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
