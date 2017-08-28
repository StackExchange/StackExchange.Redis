using System.Linq;
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
    }
}
