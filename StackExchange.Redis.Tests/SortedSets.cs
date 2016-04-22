using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class SortedSets : TestBase
    {
        [Test]
        public void ZCount()
        {
            using (var conn = Create())
            {
                var server = GetServer(conn);

                RedisKey key = "testzcount";
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                db.SortedSetAdd(key, "one", 1);
                db.SortedSetAdd(key, "two", 2);
                db.SortedSetAdd(key, "three", 3);
                db.SortedSetAdd(key, "four", 4);
                db.SortedSetAdd(key, "five", 5);
                db.SortedSetAdd(key, "seven", 7);
                db.SortedSetAdd(key, "eight", 8);
                db.SortedSetAdd(key, "nine", 9);

                long count = db.SortedSetCount("testzcount", 1, 9);

                Assert.AreEqual(count, 8);

            }
        }
    }
}
