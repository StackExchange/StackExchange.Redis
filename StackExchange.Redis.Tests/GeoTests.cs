using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class GeoTests : TestBase
    {
        [Test]
        public void GeoAddEveryWay()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(3);
                var added1 = db.GeoAdd("Sicily", 14.361389, 39.115556, "PalermoPlusOne");
                var geo1 = new GeoEntry(13.361389, 38.115556, "Palermo");
                var geo2 = new GeoEntry(15.087269, 37.502669, "Catania");
                var added2 = db.GeoAdd("Sicily",new GeoEntry[] {geo1,geo2});
                Assert.IsTrue(added1 & (added2==2));
            }
        }

        [Test]
        public void GetGeoDist()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(3);
                var geo1 = new GeoEntry(13.361389, 38.115556, "Palermo");
                var geo2 = new GeoEntry(15.087269, 37.502669, "Catania");
                var added2 = db.GeoAdd("Sicily", new GeoEntry[] { geo1, geo2 });
                var val = db.GeoDistance("Sicily", "Palermo", "Catania",GeoUnit.Meters);
                Assert.Equals(166274.15156960039, (double) val);
            }
        }
    }
}
