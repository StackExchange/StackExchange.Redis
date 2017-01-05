using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class SO23949477 : TestBase
    {
        [Test]
        public void Execute()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(0);
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.SortedSetAdd(key, "c", 3, When.Always, CommandFlags.FireAndForget);
                db.SortedSetAdd(key,
                    new[] {
                        new SortedSetEntry("a", 1),
                        new SortedSetEntry("b", 2),
                        new SortedSetEntry("d", 4),
                        new SortedSetEntry("e", 5)
                    },
                    When.Always,
                    CommandFlags.FireAndForget);
                var pairs = db.SortedSetRangeByScoreWithScores(
                    key, order: Order.Descending, take: 3);
                Assert.AreEqual(3, pairs.Length);
                Assert.AreEqual(5, pairs[0].Score);
                Assert.AreEqual("e", (string)pairs[0].Element);
                Assert.AreEqual(4, pairs[1].Score);
                Assert.AreEqual("d", (string)pairs[1].Element);
                Assert.AreEqual(3, pairs[2].Score);
                Assert.AreEqual("c", (string)pairs[2].Element);
            }
        }
    }
}
