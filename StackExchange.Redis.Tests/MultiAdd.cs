using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class MultiAdd : TestBase
    {
        [Test]
        public void AddSortedSetEveryWay()
        {
            using(var conn = Create())
            {
                var db = conn.GetDatabase(3);

                RedisKey key = Me();
                db.KeyDelete(key);
                db.SortedSetAdd(key, "a", 1);
                db.SortedSetAdd(key, new[] {
                    new SortedSetEntry("b", 2) });
                db.SortedSetAdd(key, new[] {
                    new SortedSetEntry("c", 3),
                    new SortedSetEntry("d", 4)});
                db.SortedSetAdd(key, new[] {
                    new SortedSetEntry("e", 5),
                    new SortedSetEntry("f", 6),
                    new SortedSetEntry("g", 7)});
                db.SortedSetAdd(key, new[] {
                    new SortedSetEntry("h", 8),
                    new SortedSetEntry("i", 9),
                    new SortedSetEntry("j", 10),
                    new SortedSetEntry("k", 11)});
                var vals = db.SortedSetRangeByScoreWithScores(key);
                string s = string.Join(",", vals.OrderByDescending(x => x.Score).Select(x => x.Element));
                Assert.AreEqual("k,j,i,h,g,f,e,d,c,b,a", s);
                s = string.Join(",", vals.OrderBy(x => x.Score).Select(x => x.Score));
                Assert.AreEqual("1,2,3,4,5,6,7,8,9,10,11", s);
            }
        }

        [Test]
        public void AddHashEveryWay()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(3);

                RedisKey key = Me();
                db.KeyDelete(key);
                db.HashSet(key, "a", 1);
                db.HashSet(key, new[] {
                    new HashEntry("b", 2) });
                db.HashSet(key, new[] {
                    new HashEntry("c", 3),
                    new HashEntry("d", 4)});
                db.HashSet(key, new[] {
                    new HashEntry("e", 5),
                    new HashEntry("f", 6),
                    new HashEntry("g", 7)});
                db.HashSet(key, new[] {
                    new HashEntry("h", 8),
                    new HashEntry("i", 9),
                    new HashEntry("j", 10),
                    new HashEntry("k", 11)});
                var vals = db.HashGetAll(key);
                string s = string.Join(",", vals.OrderByDescending(x => (double)x.Value).Select(x => x.Name));
                Assert.AreEqual("k,j,i,h,g,f,e,d,c,b,a", s);
                s = string.Join(",", vals.OrderBy(x => (double)x.Value).Select(x => x.Value));
                Assert.AreEqual("1,2,3,4,5,6,7,8,9,10,11", s);
            }
        }

        [Test]
        public void AddSetEveryWay()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(3);

                RedisKey key = Me();
                db.KeyDelete(key);
                db.SetAdd(key, "a");
                db.SetAdd(key, new RedisValue[] { "b" });
                db.SetAdd(key, new RedisValue[] { "c", "d" });
                db.SetAdd(key, new RedisValue[] { "e", "f", "g" });
                db.SetAdd(key, new RedisValue[] { "h", "i", "j","k" });

                var vals = db.SetMembers(key);
                string s = string.Join(",", vals.OrderByDescending(x => x));
                Assert.AreEqual("k,j,i,h,g,f,e,d,c,b,a", s);
            }
        }

        [Test]
        public void AddSetEveryWayNumbers()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase(3);

                RedisKey key = Me();
                db.KeyDelete(key);
                db.SetAdd(key, "a");
                db.SetAdd(key, new RedisValue[] { "1" });
                db.SetAdd(key, new RedisValue[] { "11", "2" });
                db.SetAdd(key, new RedisValue[] { "10", "3", "1.5" });
                db.SetAdd(key, new RedisValue[] { "2.2", "-1", "s", "t" });

                var vals = db.SetMembers(key);
                string s = string.Join(",", vals.OrderByDescending(x => x));
                Assert.AreEqual("t,s,a,11,10,3,2.2,2,1.5,1,-1", s);
            }
        }
    }
}
