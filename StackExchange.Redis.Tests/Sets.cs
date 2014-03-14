using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Sets : TestBase
    {
        [Test]
        public void SScan()
        {
            using (var conn = Create())
            {
                var server = GetServer(conn);
                

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
                Assert.AreEqual(totalUnfiltered, unfilteredActual);
                if (server.Features.Scan)
                {
                    var filteredActual = db.SetScan(key, "*3*").Select(x => (int)x).Sum();
                    Assert.AreEqual(totalFiltered, filteredActual);
                }               
                
            }
        }
    }
}
