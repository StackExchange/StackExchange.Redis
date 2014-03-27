using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Scans : TestBase
    {
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void SetScan(bool supported)
        {
            string[] disabledCommands = supported ? null : new[] { "sscan" };
            using(var conn = Create(disabledCommands: disabledCommands))
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                db.SetAdd(key, "a");
                db.SetAdd(key, "b");
                db.SetAdd(key, "c");
                var arr = db.SetScan(key).ToArray();
                Assert.AreEqual(3, arr.Length);
                Assert.IsTrue(arr.Contains("a"), "a");
                Assert.IsTrue(arr.Contains("b"), "b");
                Assert.IsTrue(arr.Contains("c"), "c");
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void SortedSetScan(bool supported)
        {
            string[] disabledCommands = supported ? null : new[] { "zscan" };
            using (var conn = Create(disabledCommands: disabledCommands))
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                db.SortedSetAdd(key, "a", 1);
                db.SortedSetAdd(key, "b", 2);
                db.SortedSetAdd(key, "c", 3);

                var arr = db.SortedSetScan(key).ToArray();
                Assert.AreEqual(3, arr.Length);
                Assert.IsTrue(arr.Any(x => x.Element == "a" && x.Score == 1), "a");
                Assert.IsTrue(arr.Any(x => x.Element == "b" && x.Score == 2), "b");
                Assert.IsTrue(arr.Any(x => x.Element == "c" && x.Score == 3), "c");

                var dictionary = arr.ToDictionary();
                Assert.AreEqual(1, dictionary["a"]);
                Assert.AreEqual(2, dictionary["b"]);
                Assert.AreEqual(3, dictionary["c"]);

                var sDictionary = arr.ToStringDictionary();
                Assert.AreEqual(1, sDictionary["a"]);
                Assert.AreEqual(2, sDictionary["b"]);
                Assert.AreEqual(3, sDictionary["c"]);

                var basic = db.SortedSetRangeByRankWithScores(key, order: Order.Ascending).ToDictionary();
                Assert.AreEqual(3, basic.Count);
                Assert.AreEqual(1, basic["a"]);
                Assert.AreEqual(2, basic["b"]);
                Assert.AreEqual(3, basic["c"]);

                basic = db.SortedSetRangeByRankWithScores(key, order: Order.Descending).ToDictionary();
                Assert.AreEqual(3, basic.Count);
                Assert.AreEqual(1, basic["a"]);
                Assert.AreEqual(2, basic["b"]);
                Assert.AreEqual(3, basic["c"]);

                var basicArr = db.SortedSetRangeByScoreWithScores(key, order: Order.Ascending);
                Assert.AreEqual(3, basicArr.Length);
                Assert.AreEqual(1, basicArr[0].Score);
                Assert.AreEqual(2, basicArr[1].Score);
                Assert.AreEqual(3, basicArr[2].Score);
                basic = basicArr.ToDictionary();
                Assert.AreEqual(3, basic.Count, "asc");
                Assert.AreEqual(1, basic["a"]);
                Assert.AreEqual(2, basic["b"]);
                Assert.AreEqual(3, basic["c"]);

                basicArr = db.SortedSetRangeByScoreWithScores(key, order: Order.Descending);
                Assert.AreEqual(3, basicArr.Length);
                Assert.AreEqual(3, basicArr[0].Score);
                Assert.AreEqual(2, basicArr[1].Score);
                Assert.AreEqual(1, basicArr[2].Score);
                basic = basicArr.ToDictionary();
                Assert.AreEqual(3, basic.Count, "desc");
                Assert.AreEqual(1, basic["a"]);
                Assert.AreEqual(2, basic["b"]);
                Assert.AreEqual(3, basic["c"]);
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void HashScan(bool supported)
        {
            string[] disabledCommands = supported ? null : new[] { "hscan" };
            using (var conn = Create(disabledCommands: disabledCommands))
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                db.HashSet(key, "a", "1");
                db.HashSet(key, "b", "2");
                db.HashSet(key, "c", "3");

                var arr = db.HashScan(key).ToArray();
                Assert.AreEqual(3, arr.Length);
                Assert.IsTrue(arr.Any(x => x.Name == "a" && x.Value == "1"), "a");
                Assert.IsTrue(arr.Any(x => x.Name == "b" && x.Value == "2"), "b");
                Assert.IsTrue(arr.Any(x => x.Name == "c" && x.Value == "3"), "c");

                var dictionary = arr.ToDictionary();
                Assert.AreEqual(1, (long)dictionary["a"]);
                Assert.AreEqual(2, (long)dictionary["b"]);
                Assert.AreEqual(3, (long)dictionary["c"]);

                var sDictionary = arr.ToStringDictionary();
                Assert.AreEqual("1", sDictionary["a"]);
                Assert.AreEqual("2", sDictionary["b"]);
                Assert.AreEqual("3", sDictionary["c"]);


                var basic = db.HashGetAll(key).ToDictionary();
                Assert.AreEqual(3, basic.Count);
                Assert.AreEqual(1, (long)basic["a"]);
                Assert.AreEqual(2, (long)basic["b"]);
                Assert.AreEqual(3, (long)basic["c"]);
            }
        }

        [Test]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void HashScanLarge(int pageSize)
        {
            using (var conn = Create())
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                for(int i = 0; i < 2000;i++)
                    db.HashSet(key, "k" + i, "v" + i, flags:  CommandFlags.FireAndForget);

                int count = db.HashScan(key, pageSize: pageSize).Count();
                Assert.AreEqual(2000, count);
            }
        }

        [Test]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void SetScanLarge(int pageSize)
        {
            using (var conn = Create())
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                for (int i = 0; i < 2000; i++)
                    db.SetAdd(key, "s" + i, flags: CommandFlags.FireAndForget);

                int count = db.SetScan(key, pageSize: pageSize).Count();
                Assert.AreEqual(2000, count);
            }
        }

        [Test]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void SortedSetScanLarge(int pageSize)
        {
            using (var conn = Create())
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                for (int i = 0; i < 2000; i++)
                    db.SortedSetAdd(key, "z" + i, i, flags: CommandFlags.FireAndForget);

                int count = db.SortedSetScan(key, pageSize: pageSize).Count();
                Assert.AreEqual(2000, count);
            }
        }
    }
}
