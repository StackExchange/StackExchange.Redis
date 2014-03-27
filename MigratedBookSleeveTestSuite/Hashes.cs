//using System;
//using System.Collections.Generic;
//using System.Text;
//using System.Linq;
//using NUnit.Framework;

//namespace Tests
//{
//    [TestFixture]
//    public class Hashes // http://redis.io/commands#hash
//    {
//        [Test]
//        public void TestIncrBy()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(5, "hash-test");
//                for (int i = 1; i < 1000; i++)
//                {
//                    Assert.AreEqual(i, conn.Hashes.Increment(5, "hash-test", "a", 1).Result);
//                    Assert.AreEqual(-i, conn.Hashes.Increment(5, "hash-test", "b", -1).Result);
//                    //Assert.AreEqual(i, conn.Wait(conn.Hashes.Increment(5, "hash-test", "a", 1)));
//                    //Assert.AreEqual(-i, conn.Wait(conn.Hashes.Increment(5, "hash-test", "b", -1)));
//                }
//            }
//        }

//        [Test]
//        public void Scan()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                if (!conn.Features.Scan) Assert.Inconclusive();
//                const int db = 3;
//                const string key = "hash-scan";
//                conn.Keys.Remove(db, key);
//                conn.Hashes.Set(db, key, "abc", "def");
//                conn.Hashes.Set(db, key, "ghi", "jkl");
//                conn.Hashes.Set(db, key, "mno", "pqr");

//                var t1 = conn.Hashes.Scan(db, key);
//                var t2 = conn.Hashes.Scan(db, key, "*h*");
//                var t3 = conn.Hashes.ScanString(db, key);
//                var t4 = conn.Hashes.ScanString(db, key, "*h*");

//                var v1 = t1.ToArray();
//                var v2 = t2.ToArray();
//                var v3 = t3.ToArray();
//                var v4 = t4.ToArray();

//                Assert.AreEqual(3, v1.Length);
//                Assert.AreEqual(1, v2.Length);
//                Assert.AreEqual(3, v3.Length);
//                Assert.AreEqual(1, v4.Length);
//                Array.Sort(v1, (x, y) => string.Compare(x.Key, y.Key));
//                Array.Sort(v2, (x, y) => string.Compare(x.Key, y.Key));
//                Array.Sort(v3, (x, y) => string.Compare(x.Key, y.Key));
//                Array.Sort(v4, (x, y) => string.Compare(x.Key, y.Key));

//                Assert.AreEqual("abc=def,ghi=jkl,mno=pqr", string.Join(",", v1.Select(pair => pair.Key + "=" + Encoding.UTF8.GetString(pair.Value))));
//                Assert.AreEqual("ghi=jkl", string.Join(",", v2.Select(pair => pair.Key + "=" + Encoding.UTF8.GetString(pair.Value))));
//                Assert.AreEqual("abc=def,ghi=jkl,mno=pqr", string.Join(",", v3.Select(pair => pair.Key + "=" + pair.Value)));
//                Assert.AreEqual("ghi=jkl", string.Join(",", v4.Select(pair => pair.Key + "=" + pair.Value)));
//            }
//        }
//        [Test]
//        public void TestIncrementOnHashThatDoesntExist()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(0, "keynotexist");
//                var result1 = conn.Wait(conn.Hashes.Increment(0, "keynotexist", "fieldnotexist", 1));
//                var result2 = conn.Wait(conn.Hashes.Increment(0, "keynotexist", "anotherfieldnotexist", 1));
//                Assert.AreEqual(1, result1);
//                Assert.AreEqual(1, result2);
//            }
//        }
//        [Test]
//        public void TestIncrByFloat()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen:true))
//            {
//                if (conn.Features.IncrementFloat)
//                {
//                    conn.Keys.Remove(5, "hash-test");
//                    for (int i = 1; i < 1000; i++)
//                    {
//                        Assert.AreEqual((double)i, conn.Hashes.Increment(5, "hash-test", "a", 1.0).Result);
//                        Assert.AreEqual((double)(-i), conn.Hashes.Increment(5, "hash-test", "b", -1.0).Result);
//                    }
//                }
//            }
//        }


//        [Test]
//        public void TestGetAll()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
                
//                const string key = "hash test";
//                conn.Keys.Remove(6, key);
//                var shouldMatch = new Dictionary<Guid, int>();
//                var random = new Random();

//                for (int i = 1; i < 1000; i++)
//                {
//                    var guid = Guid.NewGuid();
//                    var value = random.Next(Int32.MaxValue);

//                    shouldMatch[guid] = value;

//                    var x = conn.Hashes.Increment(6, key, guid.ToString(), value).Result; // Kill Async
//                }
//#pragma warning disable 618
//                var inRedisRaw = conn.GetHash(6, key).Result;
//#pragma warning restore 618
//                var inRedis = new Dictionary<Guid, int>();

//                for (var i = 0; i < inRedisRaw.Length; i += 2)
//                {
//                    var guid = inRedisRaw[i];
//                    var num = inRedisRaw[i + 1];

//                    inRedis[Guid.Parse(Encoding.ASCII.GetString(guid))] = int.Parse(Encoding.ASCII.GetString(num));
//                }

//                Assert.AreEqual(shouldMatch.Count, inRedis.Count);

//                foreach (var k in shouldMatch.Keys)
//                {
//                    Assert.AreEqual(shouldMatch[k], inRedis[k]);
//                }
//            }
//        }

//        [Test]
//        public void TestGet()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                var key = "hash test";

//                var shouldMatch = new Dictionary<Guid, int>();
//                var random = new Random();

//                for (int i = 1; i < 1000; i++)
//                {
//                    var guid = Guid.NewGuid();
//                    var value = random.Next(Int32.MaxValue);

//                    shouldMatch[guid] = value;

//                    var x = conn.Hashes.Increment(6, key, guid.ToString(), value).Result; // Kill Async
//                }

//                foreach (var k in shouldMatch.Keys)
//                {
//                    var inRedis = conn.Hashes.Get(6, key, k.ToString()).Result;
//                    var num = int.Parse(Encoding.ASCII.GetString(inRedis));

//                    Assert.AreEqual(shouldMatch[k], num);
//                }
//            }
//        }

//        [Test]
//        public void TestSet() // http://redis.io/commands/hset
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");

//                var val0 = conn.Hashes.GetString(9, "hashkey", "field");
//                var set0 = conn.Hashes.Set(9, "hashkey", "field", "value1");
//                var val1 = conn.Hashes.GetString(9, "hashkey", "field");
//                var set1 = conn.Hashes.Set(9, "hashkey", "field", "value2");
//                var val2 = conn.Hashes.GetString(9, "hashkey", "field");

//                var set2 = conn.Hashes.Set(9, "hashkey", "field-blob", Encoding.UTF8.GetBytes("value3"));
//                var val3 = conn.Hashes.Get(9, "hashkey", "field-blob");

//                Assert.AreEqual(null, val0.Result);
//                Assert.AreEqual(true, set0.Result);
//                Assert.AreEqual("value1", val1.Result);
//                Assert.AreEqual(false, set1.Result);
//                Assert.AreEqual("value2", val2.Result);

//                Assert.AreEqual(true, set2.Result);
//                Assert.AreEqual("value3", Encoding.UTF8.GetString(val3.Result));
                
//            }
//        }
//        [Test]
//        public void TestSetNotExists() // http://redis.io/commands/hsetnx
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");

//                var val0 = conn.Hashes.GetString(9, "hashkey", "field");
//                var set0 = conn.Hashes.SetIfNotExists(9, "hashkey", "field", "value1");
//                var val1 = conn.Hashes.GetString(9, "hashkey", "field");
//                var set1 = conn.Hashes.SetIfNotExists(9, "hashkey", "field", "value2");
//                var val2 = conn.Hashes.GetString(9, "hashkey", "field");

//                var set2 = conn.Hashes.SetIfNotExists(9, "hashkey", "field-blob", Encoding.UTF8.GetBytes("value3"));
//                var val3 = conn.Hashes.Get(9, "hashkey", "field-blob");
//                var set3 = conn.Hashes.SetIfNotExists(9, "hashkey", "field-blob", Encoding.UTF8.GetBytes("value3"));

//                Assert.AreEqual(null, val0.Result);
//                Assert.AreEqual(true, set0.Result);
//                Assert.AreEqual("value1", val1.Result);
//                Assert.AreEqual(false, set1.Result);
//                Assert.AreEqual("value1", val2.Result);

//                Assert.AreEqual(true, set2.Result);
//                Assert.AreEqual("value3", Encoding.UTF8.GetString(val3.Result));
//                Assert.AreEqual(false, set3.Result);

//            }
//        }
//        [Test]
//        public void TestDelSingle() // http://redis.io/commands/hdel
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {

//                conn.Keys.Remove(9, "hashkey");
//                var del0 = conn.Hashes.Remove(9, "hashkey", "field");

//                conn.Hashes.Set(9, "hashkey", "field", "value");

//                var del1 = conn.Hashes.Remove(9, "hashkey", "field");
//                var del2 = conn.Hashes.Remove(9, "hashkey", "field");

//                Assert.AreEqual(false, del0.Result);
//                Assert.AreEqual(true, del1.Result);
//                Assert.AreEqual(false, del2.Result);
                
//            }
//        }
//        [Test]
//        public void TestDelMulti() // http://redis.io/commands/hdel
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Hashes.Set(3, "TestDelMulti", "key1", "val1");
//                conn.Hashes.Set(3, "TestDelMulti", "key2", "val2");
//                conn.Hashes.Set(3, "TestDelMulti", "key3", "val3");

//                var s1 = conn.Hashes.Exists(3, "TestDelMulti", "key1");
//                var s2 = conn.Hashes.Exists(3, "TestDelMulti", "key2");
//                var s3 = conn.Hashes.Exists(3, "TestDelMulti", "key3");

//                var removed = conn.Hashes.Remove(3, "TestDelMulti", new[] { "key1", "key3" });

//                var d1 = conn.Hashes.Exists(3, "TestDelMulti", "key1");
//                var d2 = conn.Hashes.Exists(3, "TestDelMulti", "key2");
//                var d3 = conn.Hashes.Exists(3, "TestDelMulti", "key3");

//                Assert.IsTrue(conn.Wait(s1));
//                Assert.IsTrue(conn.Wait(s2));
//                Assert.IsTrue(conn.Wait(s3));

//                Assert.AreEqual(2, conn.Wait(removed));

//                Assert.IsFalse(conn.Wait(d1));
//                Assert.IsTrue(conn.Wait(d2));
//                Assert.IsFalse(conn.Wait(d3));

//                var removeFinal = conn.Hashes.Remove(3, "TestDelMulti", new[] {"key2"});
                
//                Assert.AreEqual(0, conn.Wait(conn.Hashes.GetLength(3, "TestDelMulti")));
//                Assert.AreEqual(1, conn.Wait(removeFinal));
//            }
//        }
//        [Test]
//        public void TestDelMultiInsideTransaction() // http://redis.io/commands/hdel
//        {
//            using (var outer = Config.GetUnsecuredConnection())
//            {
//                using (var conn = outer.CreateTransaction())
//                {
//                    conn.Hashes.Set(3, "TestDelMulti", "key1", "val1");
//                    conn.Hashes.Set(3, "TestDelMulti", "key2", "val2");
//                    conn.Hashes.Set(3, "TestDelMulti", "key3", "val3");

//                    var s1 = conn.Hashes.Exists(3, "TestDelMulti", "key1");
//                    var s2 = conn.Hashes.Exists(3, "TestDelMulti", "key2");
//                    var s3 = conn.Hashes.Exists(3, "TestDelMulti", "key3");

//                    var removed = conn.Hashes.Remove(3, "TestDelMulti", new[] { "key1", "key3" });

//                    var d1 = conn.Hashes.Exists(3, "TestDelMulti", "key1");
//                    var d2 = conn.Hashes.Exists(3, "TestDelMulti", "key2");
//                    var d3 = conn.Hashes.Exists(3, "TestDelMulti", "key3");

//                    conn.Execute();

//                    Assert.IsTrue(conn.Wait(s1));
//                    Assert.IsTrue(conn.Wait(s2));
//                    Assert.IsTrue(conn.Wait(s3));

//                    Assert.AreEqual(2, conn.Wait(removed));

//                    Assert.IsFalse(conn.Wait(d1));
//                    Assert.IsTrue(conn.Wait(d2));
//                    Assert.IsFalse(conn.Wait(d3));
//                }

//            }
//        }
//        [Test]
//        public void TestExists() // http://redis.io/commands/hexists
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");
//                var ex0 = conn.Hashes.Exists(9, "hashkey", "field");
//                conn.Hashes.Set(9, "hashkey", "field", "value");
//                var ex1 = conn.Hashes.Exists(9, "hashkey", "field");
//                conn.Hashes.Remove(9, "hashkey", "field");
//                var ex2 = conn.Hashes.Exists(9, "hashkey", "field");
                
//                Assert.AreEqual(false, ex0.Result);
//                Assert.AreEqual(true, ex1.Result);
//                Assert.AreEqual(false, ex0.Result);

//            }
//        }

//        [Test]
//        public void TestHashKeys() // http://redis.io/commands/hkeys
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");

//                var keys0 = conn.Hashes.GetKeys(9, "hashkey");

//                conn.Hashes.Set(9, "hashkey", "foo", "abc");
//                conn.Hashes.Set(9, "hashkey", "bar", "def");

//                var keys1 = conn.Hashes.GetKeys(9, "hashkey");

//                Assert.AreEqual(0, keys0.Result.Length);

//                var arr = keys1.Result;
//                Assert.AreEqual(2, arr.Length);
//                Assert.AreEqual("foo", arr[0]);
//                Assert.AreEqual("bar", arr[1]);

//            }
//        }

//        [Test]
//        public void TestHashValues() // http://redis.io/commands/hvals
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");

//                var keys0 = conn.Hashes.GetValues(9, "hashkey");

//                conn.Hashes.Set(9, "hashkey", "foo", "abc");
//                conn.Hashes.Set(9, "hashkey", "bar", "def");

//                var keys1 = conn.Hashes.GetValues(9, "hashkey");

//                Assert.AreEqual(0, keys0.Result.Length);

//                var arr = keys1.Result;
//                Assert.AreEqual(2, arr.Length);
//                Assert.AreEqual("abc", Encoding.UTF8.GetString(arr[0]));
//                Assert.AreEqual("def", Encoding.UTF8.GetString(arr[1]));

//            }
//        }

//        [Test]
//        public void TestHashLength() // http://redis.io/commands/hlen
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");

//                var len0 = conn.Hashes.GetLength(9, "hashkey");

//                conn.Hashes.Set(9, "hashkey", "foo", "abc");
//                conn.Hashes.Set(9, "hashkey", "bar", "def");

//                var len1 = conn.Hashes.GetLength(9, "hashkey");

//                Assert.AreEqual(0, len0.Result);
//                Assert.AreEqual(2, len1.Result);

//            }
//        }

//        [Test]
//        public void TestGetMulti() // http://redis.io/commands/hmget
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");

//                string[] fields = { "foo", "bar", "blop" };
//                var result0 = conn.Hashes.GetString(9, "hashkey", fields);

//                conn.Hashes.Set(9, "hashkey", "foo", "abc");
//                conn.Hashes.Set(9, "hashkey", "bar", "def");

//                var result1 = conn.Hashes.GetString(9, "hashkey", fields);

//                var result2 = conn.Hashes.Get(9, "hashkey", fields);

//                var arr0 = result0.Result;
//                var arr1 = result1.Result;
//                var arr2 = result2.Result;

//                Assert.AreEqual(3, arr0.Length);
//                Assert.IsNull(arr0[0]);
//                Assert.IsNull(arr0[1]);
//                Assert.IsNull(arr0[2]);

//                Assert.AreEqual(3, arr1.Length);
//                Assert.AreEqual("abc", arr1[0]);
//                Assert.AreEqual("def", arr1[1]);
//                Assert.IsNull(arr1[2]);

//                Assert.AreEqual(3, arr2.Length);
//                Assert.AreEqual("abc", Encoding.UTF8.GetString(arr2[0]));
//                Assert.AreEqual("def", Encoding.UTF8.GetString(arr2[1]));
//                Assert.IsNull(arr2[2]);
//            }
//        }

//        [Test]
//        public void TestGetPairs() // http://redis.io/commands/hgetall
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");

//                var result0 = conn.Hashes.GetAll(9, "hashkey");

//                conn.Hashes.Set(9, "hashkey", "foo", "abc");
//                conn.Hashes.Set(9, "hashkey", "bar", "def");

//                var result1 = conn.Hashes.GetAll(9, "hashkey");

//                Assert.AreEqual(0, result0.Result.Count);
//                var result = result1.Result;
//                Assert.AreEqual(2, result.Count);
//                Assert.AreEqual("abc", Encoding.UTF8.GetString(result["foo"]));
//                Assert.AreEqual("def", Encoding.UTF8.GetString(result["bar"]));
//            }
//        }

//        [Test]
//        public void TestSetPairs() // http://redis.io/commands/hmset
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(9, "hashkey");

//                var result0 = conn.Hashes.GetAll(9, "hashkey");

//                var data = new Dictionary<string, byte[]> {
//                    {"foo", Encoding.UTF8.GetBytes("abc")},
//                    {"bar", Encoding.UTF8.GetBytes("def")}
//                };
//                conn.Hashes.Set(9, "hashkey", data);

//                var result1 = conn.Hashes.GetAll(9, "hashkey");

//                Assert.AreEqual(0, result0.Result.Count);
//                var result = result1.Result;
//                Assert.AreEqual(2, result.Count);
//                Assert.AreEqual("abc", Encoding.UTF8.GetString(result["foo"]));
//                Assert.AreEqual("def", Encoding.UTF8.GetString(result["bar"]));
//            }
//        }

//    }
//}
