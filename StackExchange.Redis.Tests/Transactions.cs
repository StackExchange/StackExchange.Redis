using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Transactions : TestBase
    {
        [Test]
        public void BasicEmptyTran()
        {
            using(var muxer = Create())
            {
                RedisKey key = Me();
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));

                var tran = db.CreateTransaction();

                var result = tran.Execute();
                Assert.IsTrue(result);
            }
        }

        [Test]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void BasicTranWithExistsCondition(bool demandKeyExists, bool keyExists, bool expectTran)
        {
            using (var muxer = Create(disabledCommands: new[] { "info", "config" }))
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);
                if (keyExists) db.StringSet(key2, "any value", flags: CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(keyExists, db.KeyExists(key2));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(demandKeyExists ? Condition.KeyExists(key2) : Condition.KeyNotExists(key2));
                var incr = tran.StringIncrementAsync(key);
                var exec = tran.ExecuteAsync();
                var get = db.StringGet(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");
                if (demandKeyExists == keyExists)
                {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(1, db.Wait(incr), "eq: incr");                    
                    Assert.AreEqual(1, (long)get, "eq: get");
                }
                else
                {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, incr.Status, "neq: incr");                    
                    Assert.AreEqual(0, (long)get, "neq: get");
                }
            }
        }

        [Test]
        [TestCase("same", "same", true, true)]
        [TestCase("x", "y", true, false)]
        [TestCase("x", null, true, false)]
        [TestCase(null, "y", true, false)]
        [TestCase(null, null, true, true)]

        [TestCase("same", "same", false, false)]
        [TestCase("x", "y", false, true)]
        [TestCase("x", null, false, true)]
        [TestCase(null, "y", false, true)]
        [TestCase(null, null, false, false)]
        public void BasicTranWithEqualsCondition(string expected, string value, bool expectEqual, bool expectTran)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                if (value != null) db.StringSet(key2, value, flags: CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(value, (string)db.StringGet(key2));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(expectEqual ? Condition.StringEqual(key2, expected) : Condition.StringNotEqual(key2, expected));
                var incr = tran.StringIncrementAsync(key);
                var exec = tran.ExecuteAsync();
                var get = db.StringGet(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");
                if (expectEqual == (value == expected))
                {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(1, db.Wait(incr), "eq: incr");
                    Assert.AreEqual(1, (long)get, "eq: get");
                }
                else
                {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, incr.Status, "neq: incr");
                    Assert.AreEqual(0, (long)get, "neq: get");
                }
            }
        }


        [Test]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void BasicTranWithHashExistsCondition(bool demandKeyExists, bool keyExists, bool expectTran)
        {
            using (var muxer = Create(disabledCommands: new[] { "info", "config" }))
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);
                RedisValue hashField = "field";
                if (keyExists) db.HashSet(key2, hashField, "any value", flags:  CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(keyExists, db.HashExists(key2, hashField));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(demandKeyExists ? Condition.HashExists(key2, hashField) : Condition.HashNotExists(key2, hashField));
                var incr = tran.StringIncrementAsync(key);
                var exec = tran.ExecuteAsync();
                var get = db.StringGet(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");
                if (demandKeyExists == keyExists)
                {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(1, db.Wait(incr), "eq: incr");
                    Assert.AreEqual(1, (long)get, "eq: get");
                }
                else
                {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, incr.Status, "neq: incr");
                    Assert.AreEqual(0, (long)get, "neq: get");
                }
            }
        }

        [Test]
        [TestCase("same", "same", true, true)]
        [TestCase("x", "y", true, false)]
        [TestCase("x", null, true, false)]
        [TestCase(null, "y", true, false)]
        [TestCase(null, null, true, true)]

        [TestCase("same", "same", false, false)]
        [TestCase("x", "y", false, true)]
        [TestCase("x", null, false, true)]
        [TestCase(null, "y", false, true)]
        [TestCase(null, null, false, false)]
        public void BasicTranWithHashEqualsCondition(string expected, string value, bool expectEqual, bool expectTran)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                RedisValue hashField = "field";
                if (value != null) db.HashSet(key2, hashField, value, flags:  CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(value, (string)db.HashGet(key2, hashField));


                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(expectEqual ? Condition.HashEqual(key2, hashField, expected) : Condition.HashNotEqual(key2, hashField, expected));
                var incr = tran.StringIncrementAsync(key);
                var exec = tran.ExecuteAsync();
                var get = db.StringGet(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");
                if (expectEqual == (value == expected))
                {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(1, db.Wait(incr), "eq: incr");
                    Assert.AreEqual(1, (long)get, "eq: get");
                }
                else
                {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, incr.Status, "neq: incr");
                    Assert.AreEqual(0, (long)get, "neq: get");
                }
            }
        }

        [Test]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void BasicTranWithListExistsCondition(bool demandKeyExists, bool keyExists, bool expectTran)
        {
            using (var muxer = Create(disabledCommands: new[] { "info", "config" }))
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);
                if (keyExists) db.ListRightPush(key2, "any value", flags:  CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(keyExists, db.KeyExists(key2));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(demandKeyExists ? Condition.ListIndexExists(key2, 0) : Condition.ListIndexNotExists(key2, 0));
                var push = tran.ListRightPushAsync(key, "any value");
                var exec = tran.ExecuteAsync();
                var get = db.ListGetByIndex(key, 0);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");
                if (demandKeyExists == keyExists)
                {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(1, db.Wait(push), "eq: push");
                    Assert.AreEqual("any value", (string)get, "eq: get");
                }
                else
                {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, push.Status, "neq: push");
                    Assert.AreEqual(null, (string)get, "neq: get");
                }
            }
        }

        [Test]
        [TestCase("same", "same", true, true)]
        [TestCase("x", "y", true, false)]
        [TestCase("x", null, true, false)]
        [TestCase(null, "y", true, false)]
        [TestCase(null, null, true, true)]

        [TestCase("same", "same", false, false)]
        [TestCase("x", "y", false, true)]
        [TestCase("x", null, false, true)]
        [TestCase(null, "y", false, true)]
        [TestCase(null, null, false, false)]
        public void BasicTranWithListEqualsCondition(string expected, string value, bool expectEqual, bool expectTran)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                if (value != null) db.ListRightPush(key2, value, flags: CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(value, (string)db.ListGetByIndex(key2, 0));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(expectEqual ? Condition.ListIndexEqual(key2, 0, expected) : Condition.ListIndexNotEqual(key2, 0, expected));
                var push = tran.ListRightPushAsync(key, "any value");
                var exec = tran.ExecuteAsync();
                var get = db.ListGetByIndex(key, 0);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");
                if (expectEqual == (value == expected))
                {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(1, db.Wait(push), "eq: push");
                    Assert.AreEqual("any value", (string)get, "eq: get");
                }
                else
                {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, push.Status, "neq: push");
                    Assert.AreEqual(null, (string)get, "neq: get");
                }
            }
        }

        public enum ComparisonType
        {
            Equal,
            LessThan,
            GreaterThan
        }

        [Test]
        [TestCase("five", ComparisonType.Equal, 5L, false)]
        [TestCase("four", ComparisonType.Equal, 4L, true)]
        [TestCase("three", ComparisonType.Equal, 3L, false)]
        [TestCase("", ComparisonType.Equal, 2L, false)]
        [TestCase("", ComparisonType.Equal, 0L, true)]
        [TestCase(null, ComparisonType.Equal, 1L, false)]
        [TestCase(null, ComparisonType.Equal, 0L, true)]

        [TestCase("five", ComparisonType.LessThan, 5L, true)]
        [TestCase("four", ComparisonType.LessThan, 4L, false)]
        [TestCase("three", ComparisonType.LessThan, 3L, false)]
        [TestCase("", ComparisonType.LessThan, 2L, true)]
        [TestCase("", ComparisonType.LessThan, 0L, false)]
        [TestCase(null, ComparisonType.LessThan, 1L, true)]
        [TestCase(null, ComparisonType.LessThan, 0L, false)]

        [TestCase("five", ComparisonType.GreaterThan, 5L, false)]
        [TestCase("four", ComparisonType.GreaterThan, 4L, false)]
        [TestCase("three", ComparisonType.GreaterThan, 3L, true)]
        [TestCase("", ComparisonType.GreaterThan, 2L, false)]
        [TestCase("", ComparisonType.GreaterThan, 0L, false)]
        [TestCase(null, ComparisonType.GreaterThan, 1L, false)]
        [TestCase(null, ComparisonType.GreaterThan, 0L, false)]
        public void BasicTranWithStringLengthCondition(string value, ComparisonType type, long length, bool expectTran)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                var expectSuccess = false;
                Condition condition = null;
                var valueLength = value?.Length ?? 0;
                switch (type) {
                    case ComparisonType.Equal:
                        expectSuccess = valueLength == length;
                        condition = Condition.StringLengthEqual(key2, length);
                        Assert.That(condition.ToString(), Contains.Substring("String length == " + length));
                        break;
                    case ComparisonType.GreaterThan:
                        expectSuccess = valueLength > length;
                        condition = Condition.StringLengthGreaterThan(key2, length);
                        Assert.That(condition.ToString(), Contains.Substring("String length > " + length));
                        break;
                    case ComparisonType.LessThan:
                        expectSuccess = valueLength < length;
                        condition = Condition.StringLengthLessThan(key2, length);
                        Assert.That(condition.ToString(), Contains.Substring("String length < " + length));
                        break;
                }

                if (value != null) db.StringSet(key2, value, flags: CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(value, (string)db.StringGet(key2));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(condition);
                var push = tran.StringSetAsync(key, "any value");
                var exec = tran.ExecuteAsync();
                var get = db.StringLength(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");

                if (expectSuccess) {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(true, db.Wait(push), "eq: push");
                    Assert.AreEqual("any value".Length, get, "eq: get");
                } else {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, push.Status, "neq: push");
                    Assert.AreEqual(0, get, "neq: get");
                }
            }
        }

        [Test]
        [TestCase("five", ComparisonType.Equal, 5L, false)]
        [TestCase("four", ComparisonType.Equal, 4L, true)]
        [TestCase("three", ComparisonType.Equal, 3L, false)]
        [TestCase("", ComparisonType.Equal, 2L, false)]
        [TestCase("", ComparisonType.Equal, 0L, true)]
        
        [TestCase("five", ComparisonType.LessThan, 5L, true)]
        [TestCase("four", ComparisonType.LessThan, 4L, false)]
        [TestCase("three", ComparisonType.LessThan, 3L, false)]
        [TestCase("", ComparisonType.LessThan, 2L, true)]
        [TestCase("", ComparisonType.LessThan, 0L, false)]

        [TestCase("five", ComparisonType.GreaterThan, 5L, false)]
        [TestCase("four", ComparisonType.GreaterThan, 4L, false)]
        [TestCase("three", ComparisonType.GreaterThan, 3L, true)]
        [TestCase("", ComparisonType.GreaterThan, 2L, false)]
        [TestCase("", ComparisonType.GreaterThan, 0L, false)]
        public void BasicTranWithHashLengthCondition(string value, ComparisonType type, long length, bool expectTran)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                var expectSuccess = false;
                Condition condition = null;
                var valueLength = value?.Length ?? 0;
                switch (type) {
                    case ComparisonType.Equal:
                        expectSuccess = valueLength == length;
                        condition = Condition.HashLengthEqual(key2, length);
                        break;
                    case ComparisonType.GreaterThan:
                        expectSuccess = valueLength > length;
                        condition = Condition.HashLengthGreaterThan(key2, length);
                        break;
                    case ComparisonType.LessThan:
                        expectSuccess = valueLength < length;
                        condition = Condition.HashLengthLessThan(key2, length);
                        break;
                }

                for (var i = 0; i < valueLength; i++) {
                    db.HashSet(key2, i, value[i].ToString(), flags: CommandFlags.FireAndForget);
                }
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(valueLength, db.HashLength(key2));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(condition);
                var push = tran.StringSetAsync(key, "any value");
                var exec = tran.ExecuteAsync();
                var get = db.StringLength(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");

                if (expectSuccess) {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(true, db.Wait(push), "eq: push");
                    Assert.AreEqual("any value".Length, get, "eq: get");
                } else {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, push.Status, "neq: push");
                    Assert.AreEqual(0, get, "neq: get");
                }
            }
        }
        
        [Test]
        [TestCase("five", ComparisonType.Equal, 5L, false)]
        [TestCase("four", ComparisonType.Equal, 4L, true)]
        [TestCase("three", ComparisonType.Equal, 3L, false)]
        [TestCase("", ComparisonType.Equal, 2L, false)]
        [TestCase("", ComparisonType.Equal, 0L, true)]
        
        [TestCase("five", ComparisonType.LessThan, 5L, true)]
        [TestCase("four", ComparisonType.LessThan, 4L, false)]
        [TestCase("three", ComparisonType.LessThan, 3L, false)]
        [TestCase("", ComparisonType.LessThan, 2L, true)]
        [TestCase("", ComparisonType.LessThan, 0L, false)]

        [TestCase("five", ComparisonType.GreaterThan, 5L, false)]
        [TestCase("four", ComparisonType.GreaterThan, 4L, false)]
        [TestCase("three", ComparisonType.GreaterThan, 3L, true)]
        [TestCase("", ComparisonType.GreaterThan, 2L, false)]
        [TestCase("", ComparisonType.GreaterThan, 0L, false)]
        public void BasicTranWithSetCardinalityCondition(string value, ComparisonType type, long length, bool expectTran)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                var expectSuccess = false;
                Condition condition = null;
                var valueLength = value?.Length ?? 0;
                switch (type) {
                    case ComparisonType.Equal:
                        expectSuccess = valueLength == length;
                        condition = Condition.SetLengthEqual(key2, length);
                        break;
                    case ComparisonType.GreaterThan:
                        expectSuccess = valueLength > length;
                        condition = Condition.SetLengthGreaterThan(key2, length);
                        break;
                    case ComparisonType.LessThan:
                        expectSuccess = valueLength < length;
                        condition = Condition.SetLengthLessThan(key2, length);
                        break;
                }

                for (var i = 0; i < valueLength; i++) {
                    db.SetAdd(key2, i, flags: CommandFlags.FireAndForget);
                }
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(valueLength, db.SetLength(key2));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(condition);
                var push = tran.StringSetAsync(key, "any value");
                var exec = tran.ExecuteAsync();
                var get = db.StringLength(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");

                if (expectSuccess) {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(true, db.Wait(push), "eq: push");
                    Assert.AreEqual("any value".Length, get, "eq: get");
                } else {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, push.Status, "neq: push");
                    Assert.AreEqual(0, get, "neq: get");
                }
            }
        }

        [Test]
        [TestCase("five", ComparisonType.Equal, 5L, false)]
        [TestCase("four", ComparisonType.Equal, 4L, true)]
        [TestCase("three", ComparisonType.Equal, 3L, false)]
        [TestCase("", ComparisonType.Equal, 2L, false)]
        [TestCase("", ComparisonType.Equal, 0L, true)]
        
        [TestCase("five", ComparisonType.LessThan, 5L, true)]
        [TestCase("four", ComparisonType.LessThan, 4L, false)]
        [TestCase("three", ComparisonType.LessThan, 3L, false)]
        [TestCase("", ComparisonType.LessThan, 2L, true)]
        [TestCase("", ComparisonType.LessThan, 0L, false)]

        [TestCase("five", ComparisonType.GreaterThan, 5L, false)]
        [TestCase("four", ComparisonType.GreaterThan, 4L, false)]
        [TestCase("three", ComparisonType.GreaterThan, 3L, true)]
        [TestCase("", ComparisonType.GreaterThan, 2L, false)]
        [TestCase("", ComparisonType.GreaterThan, 0L, false)]
        public void BasicTranWithSortedSetCardinalityCondition(string value, ComparisonType type, long length, bool expectTran)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                var expectSuccess = false;
                Condition condition = null;
                var valueLength = value?.Length ?? 0;
                switch (type) {
                    case ComparisonType.Equal:
                        expectSuccess = valueLength == length;
                        condition = Condition.SortedSetLengthEqual(key2, length);
                        break;
                    case ComparisonType.GreaterThan:
                        expectSuccess = valueLength > length;
                        condition = Condition.SortedSetLengthGreaterThan(key2, length);
                        break;
                    case ComparisonType.LessThan:
                        expectSuccess = valueLength < length;
                        condition = Condition.SortedSetLengthLessThan(key2, length);
                        break;
                }

                for (var i = 0; i < valueLength; i++) {
                    db.SortedSetAdd(key2, i, i, flags: CommandFlags.FireAndForget);
                }
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(valueLength, db.SortedSetLength(key2));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(condition);
                var push = tran.StringSetAsync(key, "any value");
                var exec = tran.ExecuteAsync();
                var get = db.StringLength(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");

                if (expectSuccess) {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(true, db.Wait(push), "eq: push");
                    Assert.AreEqual("any value".Length, get, "eq: get");
                } else {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, push.Status, "neq: push");
                    Assert.AreEqual(0, get, "neq: get");
                }
            }
        }

        [Test]
        [TestCase("five", ComparisonType.Equal, 5L, false)]
        [TestCase("four", ComparisonType.Equal, 4L, true)]
        [TestCase("three", ComparisonType.Equal, 3L, false)]
        [TestCase("", ComparisonType.Equal, 2L, false)]
        [TestCase("", ComparisonType.Equal, 0L, true)]
        
        [TestCase("five", ComparisonType.LessThan, 5L, true)]
        [TestCase("four", ComparisonType.LessThan, 4L, false)]
        [TestCase("three", ComparisonType.LessThan, 3L, false)]
        [TestCase("", ComparisonType.LessThan, 2L, true)]
        [TestCase("", ComparisonType.LessThan, 0L, false)]

        [TestCase("five", ComparisonType.GreaterThan, 5L, false)]
        [TestCase("four", ComparisonType.GreaterThan, 4L, false)]
        [TestCase("three", ComparisonType.GreaterThan, 3L, true)]
        [TestCase("", ComparisonType.GreaterThan, 2L, false)]
        [TestCase("", ComparisonType.GreaterThan, 0L, false)]
        public void BasicTranWithListLengthCondition(string value, ComparisonType type, long length, bool expectTran)
        {
            using (var muxer = Create())
            {
                RedisKey key = Me(), key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                var expectSuccess = false;
                Condition condition = null;
                var valueLength = value?.Length ?? 0;
                switch (type) {
                    case ComparisonType.Equal:
                        expectSuccess = valueLength == length;
                        condition = Condition.ListLengthEqual(key2, length);
                        break;
                    case ComparisonType.GreaterThan:
                        expectSuccess = valueLength > length;
                        condition = Condition.ListLengthGreaterThan(key2, length);
                        break;
                    case ComparisonType.LessThan:
                        expectSuccess = valueLength < length;
                        condition = Condition.ListLengthLessThan(key2, length);
                        break;
                }

                for (var i = 0; i < valueLength; i++) {
                    db.ListRightPush(key2, i, flags: CommandFlags.FireAndForget);
                }
                Assert.IsFalse(db.KeyExists(key));
                Assert.AreEqual(valueLength, db.ListLength(key2));

                var tran = db.CreateTransaction();
                var cond = tran.AddCondition(condition);
                var push = tran.StringSetAsync(key, "any value");
                var exec = tran.ExecuteAsync();
                var get = db.StringLength(key);

                Assert.AreEqual(expectTran, db.Wait(exec), "expected tran result");

                if (expectSuccess) {
                    Assert.IsTrue(db.Wait(exec), "eq: exec");
                    Assert.IsTrue(cond.WasSatisfied, "eq: was satisfied");
                    Assert.AreEqual(true, db.Wait(push), "eq: push");
                    Assert.AreEqual("any value".Length, get, "eq: get");
                } else {
                    Assert.IsFalse(db.Wait(exec), "neq: exec");
                    Assert.False(cond.WasSatisfied, "neq: was satisfied");
                    Assert.AreEqual(TaskStatus.Canceled, push.Status, "neq: push");
                    Assert.AreEqual(0, get, "neq: get");
                }
            }
        }
        
        [Test]
        public async void BasicTran()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));

                var tran = db.CreateTransaction();
                var a = tran.StringIncrementAsync(key, 10);
                var b = tran.StringIncrementAsync(key, 5);
                var c = tran.StringGetAsync(key);
                var d = tran.KeyExistsAsync(key);
                var e = tran.KeyDeleteAsync(key);
                var f = tran.KeyExistsAsync(key);
                Assert.IsFalse(a.IsCompleted);
                Assert.IsFalse(b.IsCompleted);
                Assert.IsFalse(c.IsCompleted);
                Assert.IsFalse(d.IsCompleted);
                Assert.IsFalse(e.IsCompleted);
                Assert.IsFalse(f.IsCompleted);
                var result = db.Wait(tran.ExecuteAsync());
                Assert.IsTrue(result, "result");
                db.WaitAll(a, b, c, d, e, f);
                Assert.IsTrue(a.IsCompleted, "a");
                Assert.IsTrue(b.IsCompleted, "b");
                Assert.IsTrue(c.IsCompleted, "c");
                Assert.IsTrue(d.IsCompleted, "d");
                Assert.IsTrue(e.IsCompleted, "e");
                Assert.IsTrue(f.IsCompleted, "f");

                var g = db.KeyExists(key);
                

                Assert.AreEqual(10, await a.ConfigureAwait(false));
                Assert.AreEqual(15, await b.ConfigureAwait(false));
                Assert.AreEqual(15, (long)await c.ConfigureAwait(false));
                Assert.IsTrue(await d.ConfigureAwait(false));
                Assert.IsTrue(await e.ConfigureAwait(false));
                Assert.IsFalse(await f.ConfigureAwait(false));
                Assert.IsFalse(g);
            }
        }

        [Test]
        public void CombineFireAndForgetAndRegularAsyncInTransaction()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                Assert.IsFalse(db.KeyExists(key));

                var tran = db.CreateTransaction("state");
                var a = tran.StringIncrementAsync(key, 5);
                var b = tran.StringIncrementAsync(key, 10, CommandFlags.FireAndForget);
                var c = tran.StringIncrementAsync(key, 15);
                Assert.IsTrue(tran.Execute());
                var count = (long)db.StringGet(key);

                Assert.AreEqual(5, db.Wait(a), "a");
                Assert.AreEqual("state", a.AsyncState);
                Assert.AreEqual(0, db.Wait(b), "b");
                Assert.IsNull(b.AsyncState);
                Assert.AreEqual(30, db.Wait(c), "c");
                Assert.AreEqual("state", a.AsyncState);
                Assert.AreEqual(30, count, "count");
            }
        }
    }
}
