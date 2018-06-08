using Xunit;
using System;
using System.IO;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class AzureTestAttribute : FactAttribute
    {
    }

    public class GeoTests : TestBase
    {
        public GeoTests(ITestOutputHelper output) : base (output) { }

        private ConnectionMultiplexer Create()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCachePassword), TestConfig.Current.AzureCachePassword);

            var options = new ConfigurationOptions();
            options.EndPoints.Add(TestConfig.Current.AzureCacheServer);
            options.Ssl = true;
            options.ConnectTimeout = 5000;
            options.Password = TestConfig.Current.AzureCachePassword;
            options.TieBreaker = "";
            var log = new StringWriter();
            var conn = ConnectionMultiplexer.Connect(options, log);
            var s = log.ToString();
            Output.WriteLine(s);
            Skip.IfMissingFeature(conn, nameof(RedisFeatures.Geo), r => r.Geo);
            return conn;
        }

        public const int Db = 0;

        public static GeoEntry
            palermo = new GeoEntry(13.361389, 38.115556, "Palermo"),
            catania = new GeoEntry(15.087269, 37.502669, "Catania"),
            agrigento = new GeoEntry(13.5765, 37.311, "Agrigento"),
            cefalù = new GeoEntry(14.0188, 38.0084, "Cefalù");
        public static GeoEntry[] all = { palermo, catania, agrigento, cefalù };

        [AzureTest]
        public void GeoAdd()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(Db);
                RedisKey key = Me();
                db.KeyDelete(key);

                // add while not there
                Assert.True(db.GeoAdd(key, cefalù.Longitude, cefalù.Latitude, cefalù.Member));
                Assert.Equal(2, db.GeoAdd(key, new GeoEntry[] { palermo, catania }));
                Assert.True(db.GeoAdd(key, agrigento));

                // now add again
                Assert.False(db.GeoAdd(key, cefalù.Longitude, cefalù.Latitude, cefalù.Member));
                Assert.Equal(0, db.GeoAdd(key, new GeoEntry[] { palermo, catania }));
                Assert.False(db.GeoAdd(key, agrigento));
            }
        }

        [AzureTest]
        public void GetDistance()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(Db);
                RedisKey key = Me();
                db.KeyDelete(key);
                db.GeoAdd(key, all);
                var val = db.GeoDistance(key, "Palermo", "Catania", GeoUnit.Meters);
                Assert.True(val.HasValue);
                var rounded = Math.Round(val.Value, 10);
                Assert.Equal(166274.1516, val);

                val = db.GeoDistance(key, "Palermo", "Nowhere", GeoUnit.Meters);
                Assert.False(val.HasValue);
            }
        }

        [AzureTest]
        public void GeoHash()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(Db);
                RedisKey key = Me();
                db.KeyDelete(key);
                db.GeoAdd(key, all);

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

        [AzureTest]
        public void GeoGetPosition()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(Db);
                RedisKey key = Me();
                db.KeyDelete(key);
                db.GeoAdd(key, all);

                var pos = db.GeoPosition(key, palermo.Member);
                Assert.True(pos.HasValue);
                Assert.Equal(Math.Round(palermo.Longitude, 6), Math.Round(pos.Value.Longitude, 6));
                Assert.Equal(Math.Round(palermo.Latitude, 6), Math.Round(pos.Value.Latitude, 6));

                pos = db.GeoPosition(key, "Nowhere");
                Assert.False(pos.HasValue);
            }
        }

        [AzureTest]
        public void GeoRemove()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(Db);
                RedisKey key = Me();
                db.KeyDelete(key);
                db.GeoAdd(key, all);

                var pos = db.GeoPosition(key, "Palermo");
                Assert.True(pos.HasValue);

                Assert.False(db.GeoRemove(key, "Nowhere"));
                Assert.True(db.GeoRemove(key, "Palermo"));
                Assert.False(db.GeoRemove(key, "Palermo"));

                pos = db.GeoPosition(key, "Palermo");
                Assert.False(pos.HasValue);
            }
        }

        [AzureTest]
        public void GeoRadius()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(Db);
                RedisKey key = Me();
                db.KeyDelete(key);
                db.GeoAdd(key, all);

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
    }
}