using Xunit;
using System;
using Xunit.Abstractions;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class GeoTests : TestBase
    {
        public GeoTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

        public static GeoEntry
            palermo = new GeoEntry(13.361389, 38.115556, "Palermo"),
            catania = new GeoEntry(15.087269, 37.502669, "Catania"),
            agrigento = new GeoEntry(13.5765, 37.311, "Agrigento"),
            cefalù = new GeoEntry(14.0188, 38.0084, "Cefalù");
        public static GeoEntry[] all = { palermo, catania, agrigento, cefalù };

        [Fact]
        public void GeoAdd()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Geo), r => r.Geo);
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                // add while not there
                Assert.True(db.GeoAdd(key, cefalù.Longitude, cefalù.Latitude, cefalù.Member));
                Assert.Equal(2, db.GeoAdd(key, new [] { palermo, catania }));
                Assert.True(db.GeoAdd(key, agrigento));

                // now add again
                Assert.False(db.GeoAdd(key, cefalù.Longitude, cefalù.Latitude, cefalù.Member));
                Assert.Equal(0, db.GeoAdd(key, new [] { palermo, catania }));
                Assert.False(db.GeoAdd(key, agrigento));

                // Validate
                var pos = db.GeoPosition(key, palermo.Member);
                Assert.NotNull(pos);
                Assert.Equal(palermo.Longitude, pos.Value.Longitude, 5);
                Assert.Equal(palermo.Latitude, pos.Value.Latitude, 5);
            }
        }

        [Fact]
        public void GetDistance()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Geo), r => r.Geo);
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
        }

        [Fact]
        public void GeoHash()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Geo), r => r.Geo);
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.GeoAdd(key, all, CommandFlags.FireAndForget);

                var hashes = db.GeoHash(key, new RedisValue[] { palermo.Member, "Nowhere", agrigento.Member });
                Assert.Equal(3, hashes.Length);
                Assert.Equal("sqc8b49rny0", hashes[0]);
                Assert.Null(hashes[1]);
                Assert.Equal("sq9skbq0760", hashes[2]);

                var hash = db.GeoHash(key, "Palermo");
                Assert.Equal("sqc8b49rny0", hash);

                hash = db.GeoHash(key, "Nowhere");
                Assert.Null(hash);
            }
        }

        [Fact]
        public void GeoGetPosition()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Geo), r => r.Geo);
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
        }

        [Fact]
        public void GeoRemove()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Geo), r => r.Geo);
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
        }

        [Fact]
        public void GeoRadius()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Geo), r => r.Geo);
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.GeoAdd(key, all, CommandFlags.FireAndForget);

                var results = db.GeoRadius(key, cefalù.Member, 60, GeoUnit.Miles, 2, Order.Ascending);
                Assert.Equal(2, results.Length);

                Assert.Equal(results[0].Member, cefalù.Member);
                Assert.Equal(0, results[0].Distance.Value);
                Assert.Equal(Math.Round(results[0].Position.Value.Longitude, 5), Math.Round(cefalù.Position.Longitude, 5));
                Assert.Equal(Math.Round(results[0].Position.Value.Latitude, 5), Math.Round(cefalù.Position.Latitude, 5));
                Assert.False(results[0].Hash.HasValue);

                Assert.Equal(results[1].Member, palermo.Member);
                Assert.Equal(Math.Round(36.5319, 6), Math.Round(results[1].Distance.Value, 6));
                Assert.Equal(Math.Round(results[1].Position.Value.Longitude, 5), Math.Round(palermo.Position.Longitude, 5));
                Assert.Equal(Math.Round(results[1].Position.Value.Latitude, 5), Math.Round(palermo.Position.Latitude, 5));
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
        }

        [Fact]
        public async Task GeoRadiusOverloads()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Geo), r => r.Geo);
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
        }
    }
}
