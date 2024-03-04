using Xunit;
using System;
using Xunit.Abstractions;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class GeoTests : TestBase
{
    public GeoTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

    private static readonly GeoEntry
        palermo = new GeoEntry(13.361389, 38.115556, "Palermo"),
        catania = new GeoEntry(15.087269, 37.502669, "Catania"),
        agrigento = new GeoEntry(13.5765, 37.311, "Agrigento"),
        cefalù = new GeoEntry(14.0188, 38.0084, "Cefalù");
    private static readonly GeoEntry[] all = { palermo, catania, agrigento, cefalù };

    [Fact]
    public void GeoAdd()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        // add while not there
        Assert.True(db.GeoAdd(key, cefalù.Longitude, cefalù.Latitude, cefalù.Member));
        Assert.Equal(2, db.GeoAdd(key, new[] { palermo, catania }));
        Assert.True(db.GeoAdd(key, agrigento));

        // now add again
        Assert.False(db.GeoAdd(key, cefalù.Longitude, cefalù.Latitude, cefalù.Member));
        Assert.Equal(0, db.GeoAdd(key, new[] { palermo, catania }));
        Assert.False(db.GeoAdd(key, agrigento));

        // Validate
        var pos = db.GeoPosition(key, palermo.Member);
        Assert.NotNull(pos);
        Assert.Equal(palermo.Longitude, pos!.Value.Longitude, 5);
        Assert.Equal(palermo.Latitude, pos!.Value.Latitude, 5);
    }

    [Fact]
    public void GetDistance()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, all, CommandFlags.FireAndForget);
        var val = db.GeoDistance(key, "Palermo", "Catania", GeoUnit.Meters);
        Assert.True(val.HasValue);
        Assert.Equal(166274.1516, val);

        val = db.GeoDistance(key, "Palermo", "Nowhere", GeoUnit.Meters);
        Assert.False(val.HasValue);
    }

    [Fact]
    public void GeoHash()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, all, CommandFlags.FireAndForget);

        var hashes = db.GeoHash(key, new RedisValue[] { palermo.Member, "Nowhere", agrigento.Member });
        Assert.NotNull(hashes);
        Assert.Equal(3, hashes.Length);
        Assert.Equal("sqc8b49rny0", hashes[0]);
        Assert.Null(hashes[1]);
        Assert.Equal("sq9skbq0760", hashes[2]);

        var hash = db.GeoHash(key, "Palermo");
        Assert.Equal("sqc8b49rny0", hash);

        hash = db.GeoHash(key, "Nowhere");
        Assert.Null(hash);
    }

    [Fact]
    public void GeoGetPosition()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, all, CommandFlags.FireAndForget);

        var pos = db.GeoPosition(key, palermo.Member);
        Assert.True(pos.HasValue);
        Assert.Equal(Math.Round(palermo.Longitude, 6), Math.Round(pos.Value.Longitude, 6));
        Assert.Equal(Math.Round(palermo.Latitude, 6), Math.Round(pos.Value.Latitude, 6));

        pos = db.GeoPosition(key, "Nowhere");
        Assert.False(pos.HasValue);
    }

    [Fact]
    public void GeoRemove()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, all, CommandFlags.FireAndForget);

        var pos = db.GeoPosition(key, "Palermo");
        Assert.True(pos.HasValue);

        Assert.False(db.GeoRemove(key, "Nowhere"));
        Assert.True(db.GeoRemove(key, "Palermo"));
        Assert.False(db.GeoRemove(key, "Palermo"));

        pos = db.GeoPosition(key, "Palermo");
        Assert.False(pos.HasValue);
    }

    [Fact]
    public void GeoRadius()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.GeoAdd(key, all, CommandFlags.FireAndForget);

        var results = db.GeoRadius(key, cefalù.Member, 60, GeoUnit.Miles, 2, Order.Ascending);
        Assert.Equal(2, results.Length);

        Assert.Equal(results[0].Member, cefalù.Member);
        Assert.Equal(0, results[0].Distance);
        var position0 = results[0].Position;
        Assert.NotNull(position0);
        Assert.Equal(Math.Round(position0!.Value.Longitude, 5), Math.Round(cefalù.Position.Longitude, 5));
        Assert.Equal(Math.Round(position0!.Value.Latitude, 5), Math.Round(cefalù.Position.Latitude, 5));
        Assert.False(results[0].Hash.HasValue);

        Assert.Equal(results[1].Member, palermo.Member);
        var distance1 = results[1].Distance;
        Assert.NotNull(distance1);
        Assert.Equal(Math.Round(36.5319, 6), Math.Round(distance1!.Value, 6));
        var position1 = results[1].Position;
        Assert.NotNull(position1);
        Assert.Equal(Math.Round(position1!.Value.Longitude, 5), Math.Round(palermo.Position.Longitude, 5));
        Assert.Equal(Math.Round(position1!.Value.Latitude, 5), Math.Round(palermo.Position.Latitude, 5));
        Assert.False(results[1].Hash.HasValue);

        results = db.GeoRadius(key, cefalù.Member, 60, GeoUnit.Miles, 2, Order.Ascending, GeoRadiusOptions.None);
        Assert.Equal(2, results.Length);
        Assert.Equal(results[0].Member, cefalù.Member);
        Assert.False(results[0].Position.HasValue);
        Assert.False(results[0].Distance.HasValue);
        Assert.False(results[0].Hash.HasValue);

        Assert.Equal(results[1].Member, palermo.Member);
        Assert.False(results[1].Position.HasValue);
        Assert.False(results[1].Distance.HasValue);
        Assert.False(results[1].Hash.HasValue);
    }

    [Fact]
    public async Task GeoRadiusOverloads()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        Assert.True(db.GeoAdd(key, -1.759925, 52.19493, "steve"));
        Assert.True(db.GeoAdd(key, -3.360655, 54.66395, "dave"));

        // Invalid overload
        // Since this would throw ERR could not decode requested zset member, we catch and return something more useful to the user earlier.
        var ex = Assert.Throws<ArgumentException>(() => db.GeoRadius(key, -1.759925, 52.19493, GeoUnit.Miles, 500, Order.Ascending, GeoRadiusOptions.WithDistance));
        Assert.StartsWith("Member should not be a double, you likely want the GeoRadius(RedisKey, double, double, ...) overload.", ex.Message);
        Assert.Equal("member", ex.ParamName);
        ex = await Assert.ThrowsAsync<ArgumentException>(() => db.GeoRadiusAsync(key, -1.759925, 52.19493, GeoUnit.Miles, 500, Order.Ascending, GeoRadiusOptions.WithDistance)).ForAwait();
        Assert.StartsWith("Member should not be a double, you likely want the GeoRadius(RedisKey, double, double, ...) overload.", ex.Message);
        Assert.Equal("member", ex.ParamName);

        // The good stuff
        GeoRadiusResult[] result = db.GeoRadius(key, -1.759925, 52.19493, 500, unit: GeoUnit.Miles, order: Order.Ascending, options: GeoRadiusOptions.WithDistance);
        Assert.NotNull(result);

        result = await db.GeoRadiusAsync(key, -1.759925, 52.19493, 500, unit: GeoUnit.Miles, order: Order.Ascending, options: GeoRadiusOptions.WithDistance).ForAwait();
        Assert.NotNull(result);
    }

    private async Task GeoSearchSetupAsync(RedisKey key, IDatabase db)
    {
        await db.KeyDeleteAsync(key);
        await db.GeoAddAsync(key, 82.6534, 27.7682, "rays");
        await db.GeoAddAsync(key, 79.3891, 43.6418, "blue jays");
        await db.GeoAddAsync(key, 76.6217, 39.2838, "orioles");
        await db.GeoAddAsync(key, 71.0927, 42.3467, "red sox");
        await db.GeoAddAsync(key, 73.9262, 40.8296, "yankees");
    }

    private void GeoSearchSetup(RedisKey key, IDatabase db)
    {
        db.KeyDelete(key);
        db.GeoAdd(key, 82.6534, 27.7682, "rays");
        db.GeoAdd(key, 79.3891, 43.6418, "blue jays");
        db.GeoAdd(key, 76.6217, 39.2838, "orioles");
        db.GeoAdd(key, 71.0927, 42.3467, "red sox");
        db.GeoAdd(key, 73.9262, 40.8296, "yankees");
    }

    [Fact]
    public async Task GeoSearchCircleMemberAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Miles);
        var res = await db.GeoSearchAsync(key, "yankees", circle);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.NotNull(res[0].Distance);
        Assert.NotNull(res[0].Position);
        Assert.Null(res[0].Hash);
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchCircleMemberAsyncOnlyHash()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Miles);
        var res = await db.GeoSearchAsync(key, "yankees", circle, options: GeoRadiusOptions.WithGeoHash);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.Null(res[0].Distance);
        Assert.Null(res[0].Position);
        Assert.NotNull(res[0].Hash);
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchCircleMemberAsyncHashAndDistance()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Miles);
        var res = await db.GeoSearchAsync(key, "yankees", circle, options: GeoRadiusOptions.WithGeoHash | GeoRadiusOptions.WithDistance);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.NotNull(res[0].Distance);
        Assert.Null(res[0].Position);
        Assert.NotNull(res[0].Hash);
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchCircleLonLatAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Miles);
        var res = await db.GeoSearchAsync(key, 73.9262, 40.8296, circle);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public void GeoSearchCircleMember()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var circle = new GeoSearchCircle(500 * 1609);
        var res = db.GeoSearch(key, "yankees", circle);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public void GeoSearchCircleLonLat()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var circle = new GeoSearchCircle(500 * 5280, GeoUnit.Feet);
        var res = db.GeoSearch(key, 73.9262, 40.8296, circle);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Contains(res, x => x.Member == "blue jays");
        Assert.Equal(4, res.Length);
    }

    [Fact]
    public async Task GeoSearchBoxMemberAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAsync(key, "yankees", box);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
    }

    [Fact]
    public async Task GeoSearchBoxLonLatAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAsync(key, 73.9262, 40.8296, box);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
    }

    [Fact]
    public void GeoSearchBoxMember()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = db.GeoSearch(key, "yankees", box);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
    }

    [Fact]
    public void GeoSearchBoxLonLat()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = db.GeoSearch(key, 73.9262, 40.8296, box);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
    }

    [Fact]
    public void GeoSearchLimitCount()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = db.GeoSearch(key, 73.9262, 40.8296, box, count: 2);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(2, res.Length);
    }

    [Fact]
    public void GeoSearchLimitCountMakeNoDemands()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        GeoSearchSetup(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = db.GeoSearch(key, 73.9262, 40.8296, box, count: 2, demandClosest: false);
        Assert.Contains(res, x => x.Member == "red sox"); // this order MIGHT not be fully deterministic, seems to work for our purposes.
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(2, res.Length);
    }

    [Fact]
    public async Task GeoSearchBoxLonLatDescending()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await GeoSearchSetupAsync(key, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAsync(key, 73.9262, 40.8296, box, order: Order.Descending);
        Assert.Contains(res, x => x.Member == "yankees");
        Assert.Contains(res, x => x.Member == "red sox");
        Assert.Contains(res, x => x.Member == "orioles");
        Assert.Equal(3, res.Length);
        Assert.Equal("red sox", res[0].Member);
    }

    [Fact]
    public async Task GeoSearchBoxMemberAndStoreAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        await db.KeyDeleteAsync(destinationKey);
        await GeoSearchSetupAsync(sourceKey, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAndStoreAsync(sourceKey, destinationKey, "yankees", box);
        var set = await db.GeoSearchAsync(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchBoxLonLatAndStoreAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        await db.KeyDeleteAsync(destinationKey);
        await GeoSearchSetupAsync(sourceKey, db);

        var box = new GeoSearchBox(500, 500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAndStoreAsync(sourceKey, destinationKey, 73.9262, 40.8296, box);
        var set = await db.GeoSearchAsync(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchCircleMemberAndStoreAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        await db.KeyDeleteAsync(destinationKey);
        await GeoSearchSetupAsync(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAndStoreAsync(sourceKey, destinationKey, "yankees", circle);
        var set = await db.GeoSearchAsync(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public async Task GeoSearchCircleLonLatAndStoreAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        await db.KeyDeleteAsync(destinationKey);
        await GeoSearchSetupAsync(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = await db.GeoSearchAndStoreAsync(sourceKey, destinationKey, 73.9262, 40.8296, circle);
        var set = await db.GeoSearchAsync(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public void GeoSearchCircleMemberAndStore()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        db.KeyDelete(destinationKey);
        GeoSearchSetup(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = db.GeoSearchAndStore(sourceKey, destinationKey, "yankees", circle);
        var set = db.GeoSearch(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public void GeoSearchCircleLonLatAndStore()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        db.KeyDelete(destinationKey);
        GeoSearchSetup(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = db.GeoSearchAndStore(sourceKey, destinationKey, 73.9262, 40.8296, circle);
        var set = db.GeoSearch(destinationKey, "yankees", new GeoSearchCircle(10000, GeoUnit.Miles));
        Assert.Contains(set, x => x.Member == "yankees");
        Assert.Contains(set, x => x.Member == "red sox");
        Assert.Contains(set, x => x.Member == "orioles");
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public void GeoSearchCircleAndStoreDistOnly()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var me = Me();
        var db = conn.GetDatabase();
        RedisKey sourceKey = $"{me}:source";
        RedisKey destinationKey = $"{me}:destination";
        db.KeyDelete(destinationKey);
        GeoSearchSetup(sourceKey, db);

        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var res = db.GeoSearchAndStore(sourceKey, destinationKey, 73.9262, 40.8296, circle, storeDistances: true);
        var set = db.SortedSetRangeByRankWithScores(destinationKey);
        Assert.Contains(set, x => x.Element == "yankees");
        Assert.Contains(set, x => x.Element == "red sox");
        Assert.Contains(set, x => x.Element == "orioles");
        Assert.InRange(Array.Find(set, x => x.Element == "yankees").Score, 0, .2);
        Assert.InRange(Array.Find(set, x => x.Element == "orioles").Score, 286, 287);
        Assert.InRange(Array.Find(set, x => x.Element == "red sox").Score, 289, 290);
        Assert.Equal(3, set.Length);
        Assert.Equal(3, res);
    }

    [Fact]
    public void GeoSearchBadArgs()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key);
        var circle = new GeoSearchCircle(500, GeoUnit.Kilometers);
        var exception = Assert.Throws<ArgumentException>(() =>
            db.GeoSearch(key, "irrelevant", circle, demandClosest: false));

        Assert.Contains("demandClosest must be true if you are not limiting the count for a GEOSEARCH",
            exception.Message);
    }
}
