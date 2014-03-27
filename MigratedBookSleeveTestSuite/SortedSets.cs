//using System.Linq;
//using NUnit.Framework;
//using System;
//using System.Text;
//using BookSleeve;
//using System.Collections.Generic;
//using System.Threading.Tasks;

//namespace Tests
//{
//    [TestFixture]
//    public class SortedSets // http://redis.io/commands#sorted_set
//    {
//        [Test]
//        public void SortedTrim()
//        {
//            using(var conn = Config.GetUnsecuredConnection())
//            {
//                const int db = 0;
//                const string key = "sorted-trim";
//                for(int i = 0; i < 200; i++)
//                {
//                    conn.SortedSets.Add(db, key, i.ToString(), i);
//                }
//                conn.SortedSets.RemoveRange(db, key, 0, -21);
//                var count = conn.SortedSets.GetLength(db, key);
//                Assert.AreEqual(20, conn.Wait(count));
//            }
//        }

//        [Test]
//        public void Scan()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen:true))
//            {
//                if (!conn.Features.Scan) Assert.Inconclusive();

//                const int db = 3;
//                const string key = "sorted-set-scan";
//                conn.Keys.Remove(db, key);
//                conn.SortedSets.Add(db, key, "abc", 1);
//                conn.SortedSets.Add(db, key, "def", 2);
//                conn.SortedSets.Add(db, key, "ghi", 3);

//                var t1 = conn.SortedSets.Scan(db, key);
//                var t3 = conn.SortedSets.ScanString(db, key);
//                var t4 = conn.SortedSets.ScanString(db, key, "*h*");

//                var v1 = t1.ToArray();
//                var v3 = t3.ToArray();
//                var v4 = t4.ToArray();

//                Assert.AreEqual(3, v1.Length);
//                Assert.AreEqual(3, v3.Length);
//                Assert.AreEqual(1, v4.Length);
//                Array.Sort(v1, (x, y) => string.Compare(Encoding.UTF8.GetString(x.Key), Encoding.UTF8.GetString(y.Key)));
//                Array.Sort(v3, (x, y) => string.Compare(x.Key, y.Key));
//                Array.Sort(v4, (x, y) => string.Compare(x.Key, y.Key));

//                Assert.AreEqual("abc=1,def=2,ghi=3", string.Join(",", v1.Select(pair => Encoding.UTF8.GetString(pair.Key) + "=" + pair.Value)));
//                Assert.AreEqual("abc=1,def=2,ghi=3", string.Join(",", v3.Select(pair => pair.Key + "=" + pair.Value)));
//                Assert.AreEqual("ghi=3", string.Join(",", v4.Select(pair => pair.Key + "=" + pair.Value)));
//            }
//        }
//        [Test]
//        public void Range() // http://code.google.com/p/booksleeve/issues/detail?id=12
//        {
//            using(var conn = Config.GetUnsecuredConnection())
//            {
//                const double value = 634614442154715;
//                conn.SortedSets.Add(3, "zset", "abc", value);
//                var range = conn.SortedSets.Range(3, "zset", 0, -1);

//                Assert.AreEqual(value, conn.Wait(range).Single().Value);
//            }
//        }
//        [Test]
//        public void RangeString() // http://code.google.com/p/booksleeve/issues/detail?id=18
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                const double value = 634614442154715;
//                conn.SortedSets.Add(3, "zset", "abc", value);
//                var range = conn.SortedSets.RangeString(3, "zset", 0, -1);

//                Assert.AreEqual(value, conn.Wait(range).Single().Value);
//            }
//        }

//        [Test]
//        public void Score() // http://code.google.com/p/booksleeve/issues/detail?id=23
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(0, "abc");
//                conn.SortedSets.Add(0, "abc", "def", 1.0);
//                var s1 = conn.SortedSets.Score(0, "abc", "def");
//                var s2 = conn.SortedSets.Score(0, "abc", "ghi");

//                Assert.AreEqual(1.0, conn.Wait(s1));
//                Assert.IsNull(conn.Wait(s2));
//            }
//        }

//        [Test]
//        public void Rank() // http://code.google.com/p/booksleeve/issues/detail?id=23
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(0, "abc");
//                conn.SortedSets.Add(0, "abc", "def", 1.0);
//                conn.SortedSets.Add(0, "abc", "jkl", 2.0);
//                var a1 = conn.SortedSets.Rank(0, "abc", "def", ascending: true);
//                var a2 = conn.SortedSets.Rank(0, "abc", "ghi", ascending: true);
//                var a3 = conn.SortedSets.Rank(0, "abc", "jkl", ascending: true);

//                var d1 = conn.SortedSets.Rank(0, "abc", "def", ascending: false);
//                var d2 = conn.SortedSets.Rank(0, "abc", "ghi", ascending: false);
//                var d3 = conn.SortedSets.Rank(0, "abc", "jkl", ascending: false);

//                Assert.AreEqual(0, conn.Wait(a1));
//                Assert.IsNull(conn.Wait(a2));
//                Assert.AreEqual(1, conn.Wait(a3));

//                Assert.AreEqual(1, conn.Wait(d1));
//                Assert.IsNull(conn.Wait(d2));
//                Assert.AreEqual(0, conn.Wait(d3));
//            }
//        }

//        static string SeedRange(RedisConnection connection, out double min, out double max)
//        {
//            var rand = new Random(123456);
//            const string key = "somerange";
//            connection.Keys.Remove(0, key);
//            min = max = 0;
//            for (int i = 0; i < 50; i++)
//            {
//                double value = rand.NextDouble();
//                if (i == 0)
//                {
//                    min = max = value;
//                }
//                else
//                {
//                    if (value < min) min = value;
//                    if (value > max) max= value;
//                }
//                connection.SortedSets.Add(0, key, "item " + i, value);
//            }
//            return key;
//        }
//        [Test]
//        public void GetAll()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                double minActual, maxActual;
//                string key = SeedRange(conn, out minActual, out maxActual);

//                var all = conn.Wait(conn.SortedSets.Range(0, key, 0.0, 1.0));
//                Assert.AreEqual(50, all.Length, "all between 0.0 and 1.0");

//                var subset = conn.Wait(conn.SortedSets.Range(0, key, 0.0, 1.0, offset: 2, count: 46));
//                Assert.AreEqual(46, subset.Length);

//                var subVals = new HashSet<double>(subset.Select(x => x.Value));

//                Assert.IsFalse(subVals.Contains(all[0].Value));
//                Assert.IsFalse(subVals.Contains(all[1].Value));
//                Assert.IsFalse(subVals.Contains(all[48].Value));
//                Assert.IsFalse(subVals.Contains(all[49].Value));
//                for (int i = 2; i < 48; i++)
//                {
//                    Assert.IsTrue(subVals.Contains(all[i].Value));
//                }
//            }
//        }

//        [Test]
//        public void FindMinMax()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                double minActual, maxActual;
//                string key = SeedRange(conn, out minActual, out maxActual);

//                var min = conn.SortedSets.Range(0, key, ascending: true, count: 1);
//                var max = conn.SortedSets.Range(0, key, ascending: false, count: 1);

//                var minScore = conn.Wait(min).Single().Value;
//                var maxScore = conn.Wait(max).Single().Value;

//                Assert.Less(1, 2); // I *always* get these args the wrong way around
//                Assert.Less(Math.Abs(minActual - minScore), 0.0000001, "min");
//                Assert.Less(Math.Abs(maxActual - maxScore), 0.0000001, "max");
//            }
//        }

//        [Test]
//        public void CheckInfinity()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(0, "infs");
//                conn.SortedSets.Add(0, "infs", "neg", double.NegativeInfinity);
//                conn.SortedSets.Add(0, "infs", "pos", double.PositiveInfinity);
//                conn.SortedSets.Add(0, "infs", "zero", 0.0);
//                var pairs = conn.Wait(conn.SortedSets.RangeString(0, "infs", 0, -1));
//                Assert.AreEqual(3, pairs.Length);
//                Assert.AreEqual("neg", pairs[0].Key);
//                Assert.AreEqual("zero", pairs[1].Key);
//                Assert.AreEqual("pos", pairs[2].Key);
//                Assert.IsTrue(double.IsNegativeInfinity(pairs[0].Value), "-inf");
//                Assert.AreEqual(0.0, pairs[1].Value);
//                Assert.IsTrue(double.IsPositiveInfinity(pairs[2].Value), "+inf");
//            }
//        }

//        [Test]
//        public void UnionAndStore()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "key1");
//                conn.Keys.Remove(3, "key2");
//                conn.Keys.Remove(3, "to");

//                conn.SortedSets.Add(3, "key1", "a", 1);
//                conn.SortedSets.Add(3, "key1", "b", 2);
//                conn.SortedSets.Add(3, "key1", "c", 3);

//                conn.SortedSets.Add(3, "key2", "a", 1);
//                conn.SortedSets.Add(3, "key2", "b", 2);
//                conn.SortedSets.Add(3, "key2", "c", 3);

//                var numberOfElementsT = conn.SortedSets.UnionAndStore(3, "to", new string[] { "key1", "key2" }, BookSleeve.RedisAggregate.Sum);
//                var resultSetT = conn.SortedSets.RangeString(3, "to", 0, -1);

//                var numberOfElements = conn.Wait(numberOfElementsT);
//                Assert.AreEqual(3, numberOfElements);

//                var s = conn.Wait(resultSetT);

//                Assert.AreEqual("a", s[0].Key);
//                Assert.AreEqual("b", s[1].Key);
//                Assert.AreEqual("c", s[2].Key);

//                Assert.AreEqual(2, s[0].Value);
//                Assert.AreEqual(4, s[1].Value);
//                Assert.AreEqual(6, s[2].Value);
//            }
//        }

//        [Test]
//        public void UnionAndStoreMax()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "key1");
//                conn.Keys.Remove(3, "key2");
//                conn.Keys.Remove(3, "to");

//                conn.SortedSets.Add(3, "key1", "a", 1);
//                conn.SortedSets.Add(3, "key1", "b", 2);
//                conn.SortedSets.Add(3, "key1", "c", 3);

//                conn.SortedSets.Add(3, "key2", "a", 4);
//                conn.SortedSets.Add(3, "key2", "b", 5);
//                conn.SortedSets.Add(3, "key2", "c", 6);

//                var numberOfElementsT = conn.SortedSets.UnionAndStore(3, "to", new string[] { "key1", "key2" }, BookSleeve.RedisAggregate.Max);
//                var resultSetT = conn.SortedSets.RangeString(3, "to", 0, -1);

//                var numberOfElements = conn.Wait(numberOfElementsT);
//                Assert.AreEqual(3, numberOfElements);

//                var s = conn.Wait(resultSetT);

//                Assert.AreEqual("a", s[0].Key);
//                Assert.AreEqual("b", s[1].Key);
//                Assert.AreEqual("c", s[2].Key);

//                Assert.AreEqual(4, s[0].Value);
//                Assert.AreEqual(5, s[1].Value);
//                Assert.AreEqual(6, s[2].Value);
//            }
//        }

//        [Test]
//        public void UnionAndStoreMin()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "key1");
//                conn.Keys.Remove(3, "key2");
//                conn.Keys.Remove(3, "to");

//                conn.SortedSets.Add(3, "key1", "a", 1);
//                conn.SortedSets.Add(3, "key1", "b", 2);
//                conn.SortedSets.Add(3, "key1", "c", 3);

//                conn.SortedSets.Add(3, "key2", "a", 4);
//                conn.SortedSets.Add(3, "key2", "b", 5);
//                conn.SortedSets.Add(3, "key2", "c", 6);

//                var numberOfElementsT = conn.SortedSets.UnionAndStore(3, "to", new string[] { "key1", "key2" }, BookSleeve.RedisAggregate.Min);
//                var resultSetT = conn.SortedSets.RangeString(3, "to", 0, -1);

//                var numberOfElements = conn.Wait(numberOfElementsT);
//                Assert.AreEqual(3, numberOfElements);

//                var s = conn.Wait(resultSetT);

//                Assert.AreEqual("a", s[0].Key);
//                Assert.AreEqual("b", s[1].Key);
//                Assert.AreEqual("c", s[2].Key);

//                Assert.AreEqual(1, s[0].Value);
//                Assert.AreEqual(2, s[1].Value);
//                Assert.AreEqual(3, s[2].Value);
//            }
//        }

//        [Test]
//        public void TestZUNIONSTORElimit()
//        {
//            const int SIZE = 10000;
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                for (int i = 0; i < SIZE; i++)
//                {
//                    string key = "z_" + i;
//                    conn.Keys.Remove(0, key);
//                    for (int j = 0; j < 5; j++)
//                        conn.SortedSets.Add(0, key, "s" + j.ToString(), j);
//                }
//                conn.Wait(conn.Server.Ping());

//                List<Task> results = new List<Task>(SIZE);
//                for (int i = 0; i < SIZE; i+=100)
//                {
//                    string[] keys = Enumerable.Range(0,i+1).Select(x => "z_" + x).ToArray();
//                    results.Add(conn.SortedSets.UnionAndStore(0, "zu_" + i, keys, RedisAggregate.Max));
//                }
//                foreach (var task in results)
//                    conn.WaitAll(task);
//            }
//        }

//        [Test]
//        public void SO14991819()
//        {
//            const int _db = 0;
//            const string _thisChannel = "SO14991819";
//            string thisChannel = string.Format("urn:{0}", _thisChannel);
//            const string message = "hi";
//            using (var _connection = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                _connection.Keys.Remove(_db, thisChannel); // start from known state

//                TimeSpan span = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0));
//                double val = span.TotalSeconds;

//                _connection.SortedSets.Add(_db, thisChannel, message, val, false);

//                var subset = _connection.Wait(_connection.SortedSets.RangeString(
//                    _db, thisChannel, span.TotalSeconds - 10000, span.TotalSeconds, offset: 0, count: 50));

//                Assert.AreEqual(1, subset.Length);
//                Config.AssertNearlyEqual(val, subset[0].Value);
//                Assert.AreEqual(message, subset[0].Key);
//            }
//        }
//    }
//}
