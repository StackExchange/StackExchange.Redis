//using System;
//using NUnit.Framework;
//using System.Text;
//using System.Linq;
//namespace Tests
//{
//    [TestFixture]
//    public class Sets // http://redis.io/commands#set
//    {
//        [Test]
//        public void AddSingle()
//        {
//            using(var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");
//                var r0 = conn.Sets.Add(3, "set", "abc");
//                var r1 = conn.Sets.Add(3, "set", "abc");
//                var len = conn.Sets.GetLength(3, "set");

//                Assert.AreEqual(true, r0.Result);
//                Assert.AreEqual(false, r1.Result);
//                Assert.AreEqual(1, len.Result);
//            }
//        }
//        [Test]
//        public void Scan()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                if (!conn.Features.Scan) Assert.Inconclusive();

//                const int db = 3;
//                const string key = "set-scan";
//                conn.Keys.Remove(db, key);
//                conn.Sets.Add(db, key, "abc");
//                conn.Sets.Add(db, key, "def");
//                conn.Sets.Add(db, key, "ghi");

//                var t1 = conn.Sets.Scan(db, key);
//                var t3 = conn.Sets.ScanString(db, key);
//                var t4 = conn.Sets.ScanString(db, key, "*h*");

//                var v1 = t1.ToArray();
//                var v3 = t3.ToArray();
//                var v4 = t4.ToArray();

//                Assert.AreEqual(3, v1.Length);
//                Assert.AreEqual(3, v3.Length);
//                Assert.AreEqual(1, v4.Length);
//                Array.Sort(v1, (x, y) => string.Compare(Encoding.UTF8.GetString(x), Encoding.UTF8.GetString(y)));
//                Array.Sort(v3);
//                Array.Sort(v4);

//                Assert.AreEqual("abc,def,ghi", string.Join(",", v1.Select(x => Encoding.UTF8.GetString(x))));
//                Assert.AreEqual("abc,def,ghi", string.Join(",", v3));
//                Assert.AreEqual("ghi", string.Join(",", v4));
//            }
//        }
//        [Test]
//        public void AddSingleBinary()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");
//                var r0 = conn.Sets.Add(3, "set", Encode("abc"));
//                var r1 = conn.Sets.Add(3, "set", Encode("abc"));
//                var len = conn.Sets.GetLength(3, "set");

//                Assert.AreEqual(true, r0.Result);
//                Assert.AreEqual(false, r1.Result);
//                Assert.AreEqual(1, len.Result);
//            }
//        }
//        static byte[] Encode(string value) { return Encoding.UTF8.GetBytes(value); }
//        static string Decode(byte[] value) { return Encoding.UTF8.GetString(value); }
//        [Test]
//        public void RemoveSingle()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");
//                conn.Sets.Add(3, "set", "abc");
//                conn.Sets.Add(3, "set", "def");

//                var r0 = conn.Sets.Remove(3, "set", "abc");
//                var r1 = conn.Sets.Remove(3, "set", "abc");
//                var len = conn.Sets.GetLength(3, "set");

//                Assert.AreEqual(true, r0.Result);
//                Assert.AreEqual(false, r1.Result);
//                Assert.AreEqual(1, len.Result);
//            }
//        }

//        [Test]
//        public void RemoveSingleBinary()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");
//                conn.Sets.Add(3, "set", Encode("abc"));
//                conn.Sets.Add(3, "set", Encode("def"));

//                var r0 = conn.Sets.Remove(3, "set", Encode("abc"));
//                var r1 = conn.Sets.Remove(3, "set", Encode("abc"));
//                var len = conn.Sets.GetLength(3, "set");

//                Assert.AreEqual(true, r0.Result);
//                Assert.AreEqual(false, r1.Result);
//                Assert.AreEqual(1, len.Result);
//            }
//        }

//        [Test]
//        public void AddMulti()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                conn.Keys.Remove(3, "set");
//                var r0 = conn.Sets.Add(3, "set", "abc");
//                var r1 = conn.Sets.Add(3, "set", new[] {"abc", "def"});
//                var len = conn.Sets.GetLength(3, "set");

//                Assert.AreEqual(true, r0.Result);
//                Assert.AreEqual(1, r1.Result);
//                Assert.AreEqual(2, len.Result);
//            }
//        }

//        [Test]
//        public void RemoveMulti()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                conn.Keys.Remove(3, "set");
//                conn.Sets.Add(3, "set", "abc");
//                conn.Sets.Add(3, "set", "ghi");

//                var r0 = conn.Sets.Remove(3, "set", new[] {"abc", "def"});
//                var len = conn.Sets.GetLength(3, "set");

//                Assert.AreEqual(1, r0.Result);
//                Assert.AreEqual(1, len.Result);
//            }
//        }

//        [Test]
//        public void AddMultiBinary()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen:true))
//            {
//                conn.Keys.Remove(3, "set");
//                var r0 = conn.Sets.Add(3, "set", Encode("abc"));
//                var r1 = conn.Sets.Add(3, "set", new[] { Encode("abc"), Encode("def") });
//                var len = conn.Sets.GetLength(3, "set");

//                Assert.AreEqual(true, r0.Result);
//                Assert.AreEqual(1, r1.Result);
//                Assert.AreEqual(2, len.Result);
//            }
//        }

//        [Test]
//        public void RemoveMultiBinary()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                conn.Keys.Remove(3, "set");
//                conn.Sets.Add(3, "set", Encode("abc"));
//                conn.Sets.Add(3, "set", Encode("ghi"));

//                var r0 = conn.Sets.Remove(3, "set", new[] { Encode("abc"), Encode("def") });
//                var len = conn.Sets.GetLength(3, "set");

//                Assert.AreEqual(1, r0.Result);
//                Assert.AreEqual(1, len.Result);
//            }
//        }

//        [Test]
//        public void Exists()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");
//                var r0 = conn.Sets.Contains(3, "set", "def");

//                conn.Sets.Add(3, "set", "abc");
//                var r1 = conn.Sets.Contains(3, "set", "def");

//                conn.Sets.Add(3, "set", "def");
//                var r2 = conn.Sets.Contains(3, "set", "def");

//                conn.Sets.Remove(3, "set", "def");
//                var r3 = conn.Sets.Contains(3, "set", "def");

//                Assert.AreEqual(false, r0.Result);
//                Assert.AreEqual(false, r1.Result);
//                Assert.AreEqual(true, r2.Result);
//                Assert.AreEqual(false, r3.Result);
//            }
//        }


//        [Test]
//        public void ExistsBinary()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");
//                var r0 = conn.Sets.Contains(3, "set", Encode("def"));

//                conn.Sets.Add(3, "set", "abc");
//                var r1 = conn.Sets.Contains(3, "set", Encode("def"));

//                conn.Sets.Add(3, "set", "def");
//                var r2 = conn.Sets.Contains(3, "set", Encode("def"));

//                conn.Sets.Remove(3, "set", "def");
//                var r3 = conn.Sets.Contains(3, "set", Encode("def"));

//                Assert.AreEqual(false, r0.Result);
//                Assert.AreEqual(false, r1.Result);
//                Assert.AreEqual(true, r2.Result);
//                Assert.AreEqual(false, r3.Result);
//            }
//        }

//        [Test]
//        public void GetRandom()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");

//                Assert.IsNull(conn.Sets.GetRandomString(3, "set").Result);
//                Assert.IsNull(conn.Sets.GetRandom(3, "set").Result);

//                conn.Sets.Add(3, "set", "abc");
//                Assert.AreEqual("abc", conn.Sets.GetRandomString(3, "set").Result);
//                Assert.AreEqual("abc", Decode(conn.Sets.GetRandom(3, "set").Result));

//                conn.Sets.Add(3, "set", Encode("def"));
//                var result = conn.Sets.GetRandomString(3, "set").Result;
//                Assert.IsTrue(result == "abc" || result == "def");
//                result = Decode(conn.Sets.GetRandom(3, "set").Result);
//                Assert.IsTrue(result == "abc" || result == "def");
//            }
//        }

//        [Test]
//        public void GetRandomMulti()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                if (conn.Features.MultipleRandom)
//                {
//                    conn.Keys.Remove(3, "set");

//                    Assert.AreEqual(0, conn.Sets.GetRandomString(3, "set", 2).Result.Length);
//                    Assert.AreEqual(0, conn.Sets.GetRandom(3, "set", 2).Result.Length);

//                    conn.Sets.Add(3, "set", "abc");
//                    var a1 = conn.Sets.GetRandomString(3, "set", 2).Result;
//                    var a2 = conn.Sets.GetRandom(3, "set", 2).Result;
//                    Assert.AreEqual(1, a1.Length);
//                    Assert.AreEqual(1, a2.Length);
//                    Assert.AreEqual("abc", a1[0]);
//                    Assert.AreEqual("abc", Decode(a2[0]));

//                    conn.Sets.Add(3, "set", Encode("def"));
//                    var a3 = conn.Sets.GetRandomString(3, "set", 3).Result;
//                    var a4 = Array.ConvertAll(conn.Sets.GetRandom(3, "set", 3).Result, Decode);

//                    Assert.AreEqual(2, a3.Length);
//                    Assert.AreEqual(2, a4.Length);
//                    Assert.Contains("abc", a3);
//                    Assert.Contains("def", a3);
//                    Assert.Contains("abc", a4);
//                    Assert.Contains("def", a4);

//                    var a5 = conn.Sets.GetRandomString(3, "set", -3).Result;
//                    var a6 = Array.ConvertAll(conn.Sets.GetRandom(3, "set", -3).Result, Decode);
//                    Assert.AreEqual(3, a5.Length);
//                    Assert.AreEqual(3, a6.Length);
//                    Assert.IsTrue(a5.All(x => x == "abc" || x == "def"));
//                    Assert.IsTrue(a6.All(x => x == "abc" || x == "def"));
//                }
//            }
//        }

//        [Test]
//        public void RemoveRandom()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");

//                Assert.IsNull(conn.Sets.RemoveRandomString(3, "set").Result);
//                Assert.IsNull(conn.Sets.RemoveRandom(3, "set").Result);

//                conn.Sets.Add(3, "set", "abc");
//                Assert.AreEqual("abc", conn.Sets.RemoveRandomString(3, "set").Result);
//                Assert.AreEqual(0, conn.Sets.GetLength(3, "set").Result);

//                conn.Sets.Add(3, "set", "abc");
//                Assert.AreEqual("abc", Decode(conn.Sets.RemoveRandom(3, "set").Result));
//                Assert.AreEqual(0, conn.Sets.GetLength(3, "set").Result);

//                conn.Sets.Add(3, "set", "abc");
//                conn.Sets.Add(3, "set", Encode("def"));
//                var result1 = conn.Sets.RemoveRandomString(3, "set").Result;
//                var result2 = Decode(conn.Sets.RemoveRandom(3, "set").Result);
//                Assert.AreEqual(0, conn.Sets.GetLength(3, "set").Result);

//                Assert.AreNotEqual(result1, result2);
//                Assert.IsTrue(result1 == "abc" || result1 == "def");
//                Assert.IsTrue(result2 == "abc" || result2 == "def");
//            }
//        }

//        [Test]
//        public void GetAll()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "set");

//                var b0 = conn.Sets.GetAll(3, "set");
//                var s0 = conn.Sets.GetAllString(3, "set");
//                conn.Sets.Add(3, "set", "abc");
//                conn.Sets.Add(3, "set", "def");
//                var b1 = conn.Sets.GetAll(3, "set");
//                var s1 = conn.Sets.GetAllString(3, "set");

//                Assert.AreEqual(0, conn.Wait(b0).Length);
//                Assert.AreEqual(0, conn.Wait(s0).Length);
//                // check strings
//                var s = conn.Wait(s1);
//                Assert.AreEqual(2, s.Length);
//                Array.Sort(s);
//                Assert.AreEqual("abc", s[0]);
//                Assert.AreEqual("def", s[1]);
//                // check binary
//                s = Array.ConvertAll(conn.Wait(b1), Decode);
//                Assert.AreEqual(2, s.Length);
//                Array.Sort(s);
//                Assert.AreEqual("abc", s[0]);
//                Assert.AreEqual("def", s[1]);
//            }
//        }

//        [Test]
//        public void Move()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "from");
//                conn.Keys.Remove(3, "to");
//                conn.Sets.Add(3, "from", "abc");
//                conn.Sets.Add(3, "from", "def");

//                Assert.AreEqual(2, conn.Sets.GetLength(3, "from").Result);
//                Assert.AreEqual(0, conn.Sets.GetLength(3, "to").Result);

//                Assert.IsFalse(conn.Sets.Move(3, "from", "to", "nix").Result);
//                Assert.IsFalse(conn.Sets.Move(3, "from", "to", Encode("nix")).Result);

//                Assert.IsTrue(conn.Sets.Move(3, "from", "to", "abc").Result);
//                Assert.IsTrue(conn.Sets.Move(3, "from", "to", Encode("def")).Result);

//                Assert.AreEqual(0, conn.Sets.GetLength(3, "from").Result);
//                Assert.AreEqual(2, conn.Sets.GetLength(3, "to").Result);
//            }
//        }

//        [Test]
//        public void Diff()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "key1");
//                conn.Keys.Remove(3, "key2");
//                conn.Keys.Remove(3, "key3");
//                conn.Keys.Remove(3, "to");
//                conn.Sets.Add(3, "key1", "a");
//                conn.Sets.Add(3, "key1", "b");
//                conn.Sets.Add(3, "key1", "c");
//                conn.Sets.Add(3, "key1", "d");
//                conn.Sets.Add(3, "key2", "c");
//                conn.Sets.Add(3, "key3", "a");
//                conn.Sets.Add(3, "key3", "c");
//                conn.Sets.Add(3, "key3", "e");

//                var diff1 = conn.Sets.Difference(3, new[] {"key1", "key2", "key3"});
//                var diff2 = conn.Sets.DifferenceString(3, new[] { "key1", "key2", "key3" });
//                var len = conn.Sets.DifferenceAndStore(3, "to", new[] { "key1", "key2", "key3" });
//                var diff3 = conn.Sets.GetAllString(3, "to");

//                var s = Array.ConvertAll(conn.Wait(diff1), Decode);
//                Assert.AreEqual(2, s.Length);
//                Array.Sort(s);
//                Assert.AreEqual("b", s[0]);
//                Assert.AreEqual("d", s[1]);

//                s = conn.Wait(diff2);
//                Assert.AreEqual(2, s.Length);
//                Array.Sort(s);
//                Assert.AreEqual("b", s[0]);
//                Assert.AreEqual("d", s[1]);

//                Assert.AreEqual(2, conn.Wait(len));
//                s = conn.Wait(diff3);
//                Assert.AreEqual(2, s.Length);
//                Array.Sort(s);
//                Assert.AreEqual("b", s[0]);
//                Assert.AreEqual("d", s[1]);
//            }
//        }


//        [Test]
//        public void Intersect()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "key1");
//                conn.Keys.Remove(3, "key2");
//                conn.Keys.Remove(3, "key3");
//                conn.Keys.Remove(3, "to");
//                conn.Sets.Add(3, "key1", "a");
//                conn.Sets.Add(3, "key1", "b");
//                conn.Sets.Add(3, "key1", "c");
//                conn.Sets.Add(3, "key1", "d");
//                conn.Sets.Add(3, "key2", "c");
//                conn.Sets.Add(3, "key3", "a");
//                conn.Sets.Add(3, "key3", "c");
//                conn.Sets.Add(3, "key3", "e");

//                var diff1 = conn.Sets.Intersect(3, new[] { "key1", "key2", "key3" });
//                var diff2 = conn.Sets.IntersectString(3, new[] { "key1", "key2", "key3" });
//                var len = conn.Sets.IntersectAndStore(3, "to", new[] { "key1", "key2", "key3" });
//                var diff3 = conn.Sets.GetAllString(3, "to");

//                var s = Array.ConvertAll(conn.Wait(diff1), Decode);
//                Assert.AreEqual(1, s.Length);
//                Assert.AreEqual("c", s[0]);

//                s = conn.Wait(diff2);
//                Assert.AreEqual(1, s.Length);
//                Assert.AreEqual("c", s[0]);

//                Assert.AreEqual(1, conn.Wait(len));
//                s = conn.Wait(diff3);
//                Assert.AreEqual(1, s.Length);
//                Assert.AreEqual("c", s[0]);
//            }
//        }

//        [Test]
//        public void Union()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(3, "key1");
//                conn.Keys.Remove(3, "key2");
//                conn.Keys.Remove(3, "key3");
//                conn.Keys.Remove(3, "to");
//                conn.Sets.Add(3, "key1", "a");
//                conn.Sets.Add(3, "key1", "b");
//                conn.Sets.Add(3, "key1", "c");
//                conn.Sets.Add(3, "key1", "d");
//                conn.Sets.Add(3, "key2", "c");
//                conn.Sets.Add(3, "key3", "a");
//                conn.Sets.Add(3, "key3", "c");
//                conn.Sets.Add(3, "key3", "e");

//                var diff1 = conn.Sets.Union(3, new[] { "key1", "key2", "key3" });
//                var diff2 = conn.Sets.UnionString(3, new[] { "key1", "key2", "key3" });
//                var len = conn.Sets.UnionAndStore(3, "to", new[] { "key1", "key2", "key3" });
//                var diff3 = conn.Sets.GetAllString(3, "to");

//                var s = Array.ConvertAll(conn.Wait(diff1), Decode);
//                Assert.AreEqual(5, s.Length);
//                Array.Sort(s);
//                Assert.AreEqual("a", s[0]);
//                Assert.AreEqual("b", s[1]);
//                Assert.AreEqual("c", s[2]);
//                Assert.AreEqual("d", s[3]);
//                Assert.AreEqual("e", s[4]);

//                s = conn.Wait(diff2);
//                Assert.AreEqual(5, s.Length);
//                Array.Sort(s);
//                Assert.AreEqual("a", s[0]);
//                Assert.AreEqual("b", s[1]);
//                Assert.AreEqual("c", s[2]);
//                Assert.AreEqual("d", s[3]);
//                Assert.AreEqual("e", s[4]);

//                Assert.AreEqual(5, conn.Wait(len));
//                s = conn.Wait(diff3);
//                Assert.AreEqual(5, s.Length);
//                Array.Sort(s);
//                Assert.AreEqual("a", s[0]);
//                Assert.AreEqual("b", s[1]);
//                Assert.AreEqual("c", s[2]);
//                Assert.AreEqual("d", s[3]);
//                Assert.AreEqual("e", s[4]);
//            }
//        }
//    }
//}
