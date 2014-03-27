//using System.Threading;
//using BookSleeve;
//using NUnit.Framework;
//using System.Text.RegularExpressions;
//using System.Linq;
//using System;
//namespace Tests
//{
//    [TestFixture]
//    public class Keys // http://redis.io/commands#generic
//    {
//        // note we don't expose EXPIREAT as it raises all sorts of problems with
//        // time synchronisation, UTC vs local, DST, etc; easier for the caller
//        // to use EXPIRE

//        [Test]
//        public void TestDeleteValidKey()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(0, "del", "abcdef");
//                var x = conn.Strings.GetString(0, "del");
//                var del = conn.Keys.Remove(0, "del");
//                var y = conn.Strings.GetString(0, "del");
//                conn.WaitAll(x, del, y);
//                Assert.AreEqual("abcdef", x.Result);
//                Assert.IsTrue(del.Result);
//                Assert.AreEqual(null, y.Result);
//            }
//        }

//        [Test]
//        public void TestLargeIntegers()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                const long expected = 20L * int.MaxValue;
//                conn.Strings.Set(0, "large-int", expected);
//                var result = conn.Strings.GetInt64(0, "large-int");
//                Assert.AreEqual(expected, conn.Wait(result));
//            }
//        }

//        [Test]
//        public void TestDeleteInvalidKey()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(0, "exists", "abcdef");
//                var x = conn.Keys.Remove(0, "exists");
//                var y = conn.Keys.Remove(0, "exists");
//                conn.WaitAll(x, y);
//                Assert.IsTrue(x.Result);
//                Assert.IsFalse(y.Result);
//            }
//        }

//        [Test]
//        public void TestDeleteMultiple()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(0, "del", "abcdef");
//                var x = conn.Keys.Remove(0, "del");
//                var y = conn.Keys.Remove(0, "del");
//                conn.WaitAll(x, y);
//                Assert.IsTrue(x.Result);
//                Assert.IsFalse(y.Result);
//            }
//        }

//        [Test]
//        public void Scan()
//        {
//            using (var conn = Config.GetUnsecuredConnection(allowAdmin: true, waitForOpen: true))
//            {
//                if (!conn.Features.Scan) Assert.Inconclusive();

//                const int DB = 3;
//                conn.Wait(conn.Server.FlushDb(DB));
//                conn.Strings.Set(DB, "foo", "foo");
//                conn.Strings.Set(DB, "bar", "bar");
//                conn.Strings.Set(DB, "blap", "blap");

//                var keys = conn.Keys.Scan(DB).ToArray();
//                Array.Sort(keys);
//                Assert.AreEqual(3, keys.Length);
//                Assert.AreEqual("bar", keys[0]);
//                Assert.AreEqual("blap", keys[1]);
//                Assert.AreEqual("foo", keys[2]);

//                keys = conn.Keys.Scan(DB, "b*").ToArray();
//                Array.Sort(keys);
//                Assert.AreEqual(2, keys.Length);
//                Assert.AreEqual("bar", keys[0]);
//                Assert.AreEqual("blap", keys[1]);

                
//            }
//        }

//        [Test]
//        public void TestExpireAgainstInvalidKey()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(0, "delA", "abcdef");
//                conn.Keys.Remove(0, "delB");
//                conn.Strings.Set(0, "delC", "abcdef");

//                var del = conn.Keys.Remove(0, new[] {"delA", "delB", "delC"});
//                Assert.AreEqual(2, conn.Wait(del));
//            }
//        }

//        [Test]
//        public void TestExists()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(0, "exists", "abcdef");
//                var x = conn.Keys.Exists(0, "exists");
//                conn.Keys.Remove(0, "exists");
//                var y = conn.Keys.Exists(0, "exists");
//                conn.WaitAll(x, y);
//                Assert.IsTrue(x.Result);
//                Assert.IsFalse (y.Result);
//            }
//        }

//        [Test]
//        public void TestExpireAgainstValidKey()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(0, "expire", "abcdef");
//                var x = conn.Keys.TimeToLive(0, "expire");
//                var exp1 = conn.Keys.Expire(0, "expire", 100);
//                var y = conn.Keys.TimeToLive(0, "expire");
//                var exp2 = conn.Keys.Expire(0, "expire", 150);
//                var z = conn.Keys.TimeToLive(0, "expire");

//                conn.WaitAll(x, exp1, y, exp2, z);
                
//                Assert.AreEqual(-1, x.Result);
//                Assert.IsTrue(exp1.Result);
//                Assert.GreaterOrEqual(y.Result, 90);
//                Assert.LessOrEqual(y.Result, 100);

//                if (conn.Features.ExpireOverwrite)
//                {
//                    Assert.IsTrue(exp2.Result);
//                    Assert.GreaterOrEqual(z.Result, 140);
//                    Assert.LessOrEqual(z.Result, 150);
//                }
//                else
//                {
//                    Assert.IsFalse(exp2.Result);
//                    Assert.GreaterOrEqual(z.Result, 90);
//                    Assert.LessOrEqual(z.Result, 100);
//                }
//            }
//        }

//        [Test]
//        public void TestSuccessfulMove()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(1, "move", "move-value");
//                conn.Keys.Remove(2, "move");

//                var succ = conn.Keys.Move(1, "move", 2);
//                var in1 = conn.Strings.GetString(1, "move");
//                var in2 = conn.Strings.GetString(2, "move");

//                Assert.IsTrue(conn.Wait(succ));
//                Assert.IsNull(conn.Wait(in1));
//                Assert.AreEqual("move-value", conn.Wait(in2));
//            }
//        }

//        [Test]
//        public void TestFailedMoveWhenNotExistsInSource()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(1, "move");
//                conn.Strings.Set(2, "move", "move-value");
                
//                var succ = conn.Keys.Move(1, "move", 2);
//                var in1 = conn.Strings.GetString(1, "move");
//                var in2 = conn.Strings.GetString(2, "move");

//                Assert.IsFalse(conn.Wait(succ));
//                Assert.IsNull(conn.Wait(in1));
//                Assert.AreEqual("move-value", conn.Wait(in2));
//            }
//        }

//        [Test]
//        public void TestFailedMoveWhenNotExistsInTarget()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Strings.Set(1, "move", "move-valueA");
//                conn.Strings.Set(2, "move", "move-valueB");

//                var succ = conn.Keys.Move(1, "move", 2);
//                var in1 = conn.Strings.GetString(1, "move");
//                var in2 = conn.Strings.GetString(2, "move");

//                Assert.IsFalse(conn.Wait(succ));
//                Assert.AreEqual("move-valueA", conn.Wait(in1));
//                Assert.AreEqual("move-valueB", conn.Wait(in2));
//            }
//        }

//        [Test]
//        public void RemoveExpiry()
//        {
//            int errors = 0, expectedErrors;
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                conn.Error += delegate
//                {
//                    Interlocked.Increment(ref errors);
//                };
//                conn.Keys.Remove(1, "persist");
//                conn.Strings.Set(1, "persist", "persist");
//                var persist1 = conn.Keys.Persist(1, "persist");
//                conn.Keys.Expire(1, "persist", 100);
//                var before = conn.Keys.TimeToLive(1, "persist");
//                var persist2 = conn.Keys.Persist(1, "persist");
//                var after = conn.Keys.TimeToLive(1, "persist");
                
//                Assert.GreaterOrEqual(conn.Wait(before), 90);
//                if (conn.Features.Persist)
//                {
//                    Assert.IsFalse(conn.Wait(persist1));   
//                    Assert.IsTrue(conn.Wait(persist2));
//                    Assert.AreEqual(-1, conn.Wait(after));
//                    expectedErrors = 0;
//                }
//                else
//                {
//                    try{
//                        conn.Wait(persist1);
//                        Assert.Fail();
//                    }
//                    catch (RedisException){}
//                    try
//                    {
//                        conn.Wait(persist2);
//                        Assert.Fail();
//                    }
//                    catch (RedisException) { }
//                    Assert.GreaterOrEqual(conn.Wait(after), 90);
//                    expectedErrors = 2;
//                }
//            }

//            Assert.AreEqual(expectedErrors, Interlocked.CompareExchange(ref errors,0,0));
//        }


//        [Test]
//        public void RandomKeys()
//        {
//            using (var conn = Config.GetUnsecuredConnection(allowAdmin: true, waitForOpen: true))
//            {
//                conn.Server.FlushDb(6);
//                var key1 = conn.Keys.Random(6);
//                conn.Strings.Set(6, "random1", "random1");
//                var key2 = conn.Keys.Random(6);
//                for (int i = 2; i < 100; i++)
//                {
//                    string key = "random" + i;
//                    conn.Strings.Set(6, key, key);
//                }
//                var key3 = conn.Keys.Random(6);

//                Assert.IsNull(conn.Wait(key1));
//                Assert.AreEqual("random1", conn.Wait(key2));
//                string s = conn.Wait(key3);

//                Assert.IsTrue(s.StartsWith("random"));
//                s = s.Substring(6);
//                int result = int.Parse(s);
//                Assert.GreaterOrEqual(result, 1);
//                Assert.Less(result, 100);
//            }

//        }

//        [Test]
//        public void Sort()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(1, "sort");
//                conn.Lists.AddLast(1, "sort", "10");
//                conn.Lists.AddLast(1, "sort", "3");
//                conn.Lists.AddLast(1, "sort", "1.1");
//                conn.Lists.AddLast(1, "sort", "2");
//                var a = conn.Keys.SortString(1, "sort");
//                var b = conn.Keys.SortString(1, "sort", ascending: false, offset: 1, count: 2);
//                var c = conn.Keys.SortString(1, "sort", alpha: true);
//                var d = conn.Keys.SortAndStore(1, "sort-store", "sort");
//                var e = conn.Lists.RangeString(1, "sort-store", 0, -1);
//                var f = conn.Lists.RangeString(1, "sort", 0, -1);

//                Assert.AreEqual("1.1;2;3;10",string.Join(";", conn.Wait(a)));
//                Assert.AreEqual("3;2",string.Join(";", conn.Wait(b)));
//                Assert.AreEqual("10;1.1;2;3", string.Join(";", conn.Wait(c)));
//                Assert.AreEqual(4, conn.Wait(d));
//                Assert.AreEqual("1.1;2;3;10", string.Join(";", conn.Wait(e)));
//                Assert.AreEqual("10;3;1.1;2", string.Join(";", conn.Wait(f)));

//            }

//        }

//        [Test]
//        public void ItemType()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(4, new[]{"type-none", "type-list", "type-string",
//                    "type-set", "type-zset", "type-hash"});
//                conn.Strings.Set(4, "type-string", "blah");
//                conn.Lists.AddLast(4, "type-list", "blah");
//                conn.Sets.Add(4, "type-set", "blah");
//                conn.SortedSets.Add(4, "type-zset", "blah", 123);
//                conn.Hashes.Set(4, "type-hash", "foo", "blah");

//                var x0 = conn.Keys.Type(4, "type-none");
//                var x1 = conn.Keys.Type(4, "type-list");
//                var x2 = conn.Keys.Type(4, "type-string");
//                var x3 = conn.Keys.Type(4, "type-set");
//                var x4 = conn.Keys.Type(4, "type-zset");
//                var x5 = conn.Keys.Type(4, "type-hash");

//                Assert.AreEqual(RedisConnection.ItemTypes.None, conn.Wait(x0));
//                Assert.AreEqual(RedisConnection.ItemTypes.List, conn.Wait(x1));
//                Assert.AreEqual(RedisConnection.ItemTypes.String, conn.Wait(x2));
//                Assert.AreEqual(RedisConnection.ItemTypes.Set, conn.Wait(x3));
//                Assert.AreEqual(RedisConnection.ItemTypes.SortedSet, conn.Wait(x4));
//                Assert.AreEqual(RedisConnection.ItemTypes.Hash, conn.Wait(x5));
//            }
//        }
//        [Test]
//        public void RenameKeyWithOverwrite()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(1, "foo");
//                conn.Keys.Remove(1, "bar");

//                var check1 = conn.Keys.Rename(1, "foo", "bar"); // neither
//                var after1_foo = conn.Strings.GetString(1, "foo");
//                var after1_bar = conn.Strings.GetString(1, "bar");

//                conn.Strings.Set(1, "foo", "foo-value");

//                var check2 = conn.Keys.Rename(1, "foo", "bar"); // source only
//                var after2_foo = conn.Strings.GetString(1, "foo");
//                var after2_bar = conn.Strings.GetString(1, "bar");

//                var check3 = conn.Keys.Rename(1, "foo", "bar"); // dest only
//                var after3_foo = conn.Strings.GetString(1, "foo");
//                var after3_bar = conn.Strings.GetString(1, "bar");

//                conn.Strings.Set(1, "foo", "new-value");
//                var check4 = conn.Keys.Rename(1, "foo", "bar"); // both
//                var after4_foo = conn.Strings.GetString(1, "foo");
//                var after4_bar = conn.Strings.GetString(1, "bar");

//                try
//                {
//                    conn.Wait(check1);
//                    Assert.Fail();
//                }
//                catch (RedisException) { }
//                Assert.IsNull(conn.Wait(after1_foo));
//                Assert.IsNull(conn.Wait(after1_bar));

//                conn.Wait(check2);
//                Assert.IsNull(conn.Wait(after2_foo));
//                Assert.AreEqual("foo-value", conn.Wait(after2_bar));

//                try
//                {
//                    conn.Wait(check3);
//                    Assert.Fail();
//                }
//                catch (RedisException) { }
//                Assert.IsNull(conn.Wait(after3_foo));
//                Assert.AreEqual("foo-value", conn.Wait(after3_bar));

//                conn.Wait(check4);
//                Assert.IsNull(conn.Wait(after4_foo));
//                Assert.AreEqual("new-value", conn.Wait(after4_bar));

//            }
//        }

//        [Test]
//        public void RenameKeyWithoutOverwrite()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(1, "foo");
//                conn.Keys.Remove(1, "bar");

//                var check1 = conn.Keys.RenameIfNotExists(1, "foo", "bar"); // neither
//                var after1_foo = conn.Strings.GetString(1, "foo");
//                var after1_bar = conn.Strings.GetString(1, "bar");

//                conn.Strings.Set(1, "foo", "foo-value");

//                var check2 = conn.Keys.RenameIfNotExists(1, "foo", "bar"); // source only
//                var after2_foo = conn.Strings.GetString(1, "foo");
//                var after2_bar = conn.Strings.GetString(1, "bar");

//                var check3 = conn.Keys.RenameIfNotExists(1, "foo", "bar"); // dest only
//                var after3_foo = conn.Strings.GetString(1, "foo");
//                var after3_bar = conn.Strings.GetString(1, "bar");

//                conn.Strings.Set(1, "foo", "new-value");
//                var check4 = conn.Keys.RenameIfNotExists(1, "foo", "bar"); // both
//                var after4_foo = conn.Strings.GetString(1, "foo");
//                var after4_bar = conn.Strings.GetString(1, "bar");

//                try
//                {
//                    conn.Wait(check1);
//                    Assert.Fail();
//                }
//                catch (RedisException) { }
//                Assert.IsNull(conn.Wait(after1_foo));
//                Assert.IsNull(conn.Wait(after1_bar));

//                Assert.IsTrue(conn.Wait(check2));
//                Assert.IsNull(conn.Wait(after2_foo));
//                Assert.AreEqual("foo-value", conn.Wait(after2_bar));

//                try
//                {
//                    conn.Wait(check3);
//                    Assert.Fail();
//                }
//                catch (RedisException) { }
//                Assert.IsNull(conn.Wait(after3_foo));
//                Assert.AreEqual("foo-value", conn.Wait(after3_bar));

//                Assert.IsFalse(conn.Wait(check4));
//                Assert.AreEqual("new-value", conn.Wait(after4_foo));
//                Assert.AreEqual("foo-value", conn.Wait(after4_bar));

//            }
//        }

//        [Test]
//        public void TestFind()
//        {
//            using(var conn = Config.GetUnsecuredConnection(allowAdmin:true))
//            {
//                conn.Server.FlushDb(5);
//                conn.Strings.Set(5, "abc", "def");
//                conn.Strings.Set(5, "abd", "ghi");
//                conn.Strings.Set(5, "aef", "jkl");
//                var arr = conn.Wait(conn.Keys.Find(5, "ab*"));
//                Assert.AreEqual(2, arr.Length);
//                Assert.Contains("abc", arr);
//                Assert.Contains("abd", arr);
//            }
//        }

//        [Test]
//        public void TestDBSize()
//        {
//            using(var conn = Config.GetUnsecuredConnection(allowAdmin:true))
//            {
//                conn.Server.FlushDb(5);
//                var empty = conn.Keys.GetLength(5);
//                for (int i = 0; i < 10; i++ )
//                    conn.Strings.Set(5, "abc" + i, "def" + i);
//                var withData = conn.Keys.GetLength(5);

//                Assert.AreEqual(0, conn.Wait(empty));
//                Assert.AreEqual(10, conn.Wait(withData));
//            }
//        }

//        [Test]
//        public void TestDebugObject()
//        {
//            using (var conn = Config.GetUnsecuredConnection(allowAdmin: true))
//            {
//                conn.Strings.Set(3, "test-debug", "some value");
//                var s = conn.Wait(conn.Keys.DebugObject(3, "test-debug"));
//                Assert.IsTrue(Regex.IsMatch(s, @"\bserializedlength:([0-9]+)\b"));
//            }
//        }
//    }
//}




