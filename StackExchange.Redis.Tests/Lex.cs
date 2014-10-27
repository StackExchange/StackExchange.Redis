using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Lex : TestBase
    {

        [Test]
        public void QueryRangeAndLengthByLex()
        {
            using(var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key);

                db.SortedSetAdd(key,
                    new SortedSetEntry[]
                {
                    new SortedSetEntry("a", 0),
                    new SortedSetEntry("b", 0),
                    new SortedSetEntry("c", 0),
                    new SortedSetEntry("d", 0),
                    new SortedSetEntry("e", 0),
                    new SortedSetEntry("f", 0),
                    new SortedSetEntry("g", 0),
                });

                var set = db.SortedSetRangeByValue(key, default(RedisValue), "c");
                var count = db.SortedSetLengthByValue(key, default(RedisValue), "c");
                Equate(set, count, "a", "b", "c");

                set = db.SortedSetRangeByValue(key, default(RedisValue), "c", Exclude.Stop);
                count = db.SortedSetLengthByValue(key, default(RedisValue), "c", Exclude.Stop);
                Equate(set, count, "a", "b");

                set = db.SortedSetRangeByValue(key, "aaa", "g", Exclude.Stop);
                count = db.SortedSetLengthByValue(key, "aaa", "g", Exclude.Stop);
                Equate(set, count, "b", "c", "d", "e", "f");

                set = db.SortedSetRangeByValue(key, "aaa", "g", Exclude.Stop, 1, 3);
                Equate(set, set.Length, "c", "d", "e");
            }
        }

        [Test]
        public void RemoveRangeByLex()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key);

                db.SortedSetAdd(key,
                    new SortedSetEntry[]
                {
                    new SortedSetEntry("aaaa", 0),
                    new SortedSetEntry("b", 0),
                    new SortedSetEntry("c", 0),
                    new SortedSetEntry("d", 0),
                    new SortedSetEntry("e", 0),
                });
                db.SortedSetAdd(key,
                    new SortedSetEntry[]
                {
                    new SortedSetEntry("foo", 0),
                    new SortedSetEntry("zap", 0),
                    new SortedSetEntry("zip", 0),
                    new SortedSetEntry("ALPHA", 0),
                    new SortedSetEntry("alpha", 0),
                });

                var set = db.SortedSetRangeByRank(key);
                Equate(set, set.Length, "ALPHA", "aaaa", "alpha", "b", "c", "d", "e", "foo", "zap", "zip");

                long removed = db.SortedSetRemoveRangeByValue(key, "alpha", "omega");
                Assert.AreEqual(6, removed);

                set = db.SortedSetRangeByRank(key);
                Equate(set, set.Length, "ALPHA", "aaaa", "zap", "zip");
            }
        }



        private void Equate(RedisValue[] actual, long count, params string[] expected)
        {
            Assert.AreEqual(count, expected.Length);
            Assert.AreEqual(expected.Length, actual.Length);
            for(int i = 0; i < actual.Length; i++)
            {
                Assert.AreEqual(expected[i], (string)actual[i]);
            }
        }
    }
}
