using System;
using NUnit.Framework;
using StackExchange.Redis.KeyspaceIsolation;

namespace StackExchange.Redis.Tests
{

    [TestFixture]
    public class WithKeyPrefixTests : TestBase
    {
        [Test]
        public void BlankPrefixYieldsSame_Bytes()
        {
            using (var conn = Create())
            {
                var raw = conn.GetDatabase(1);
                var prefixed = raw.WithKeyPrefix(new byte[0]);
                Assert.AreSame(raw, prefixed);
            }
        }
        [Test]
        public void BlankPrefixYieldsSame_String()
        {
            using (var conn = Create())
            {
                var raw = conn.GetDatabase(1);
                var prefixed = raw.WithKeyPrefix("");
                Assert.AreSame(raw, prefixed);
            }
        }
        [Test]
        public void NullPrefixIsError_Bytes()
        {
            Assert.Throws<ArgumentNullException>(() => {
                using (var conn = Create())
                {
                    var raw = conn.GetDatabase(1);
                    var prefixed = raw.WithKeyPrefix((byte[])null);
                }
            });
        }
        [Test]
        public void NullPrefixIsError_String()
        {
            Assert.Throws<ArgumentNullException>(() => {
                using (var conn = Create())
                {
                    var raw = conn.GetDatabase(1);
                    var prefixed = raw.WithKeyPrefix((string)null);
                }
            });
        }

        [Test]
        [TestCase("abc")]
        [TestCase("")]
        [TestCase(null)]
        public void NullDatabaseIsError(string prefix)
        {
            Assert.Throws<ArgumentNullException>(() => {
                IDatabase raw = null;
                var prefixed = raw.WithKeyPrefix(prefix);
            });
        }
        [Test]
        public void BasicSmokeTest()
        {
            using(var conn = Create())
            {
                var raw = conn.GetDatabase(1);

                var foo = raw.WithKeyPrefix("foo");
                var foobar = foo.WithKeyPrefix("bar");

                string key = Me();

                string s = Guid.NewGuid().ToString(), t = Guid.NewGuid().ToString();

                foo.StringSet(key, s);
                var val = (string)foo.StringGet(key);
                Assert.AreEqual(s, val); // fooBasicSmokeTest

                foobar.StringSet(key, t);
                val = (string)foobar.StringGet(key);
                Assert.AreEqual(t, val); // foobarBasicSmokeTest

                val = (string)foo.StringGet("bar" + key);
                Assert.AreEqual(t, val); // foobarBasicSmokeTest

                val = (string)raw.StringGet("foo" + key);
                Assert.AreEqual(s, val); // fooBasicSmokeTest

                val = (string)raw.StringGet("foobar" + key);
                Assert.AreEqual(t, val); // foobarBasicSmokeTest
            }
        }
        [Test]
        public void ConditionTest()
        {
            using(var conn = Create())
            {
                var raw = conn.GetDatabase(2);

                var foo = raw.WithKeyPrefix("tran:");

                raw.KeyDelete("tran:abc");
                raw.KeyDelete("tran:i");

                // execute while key exists
                raw.StringSet("tran:abc", "def");
                var tran = foo.CreateTransaction();
                tran.AddCondition(Condition.KeyExists("abc"));
                tran.StringIncrementAsync("i");
                tran.Execute();

                int i = (int)raw.StringGet("tran:i");
                Assert.AreEqual(1, i);

                // repeat without key
                raw.KeyDelete("tran:abc");
                tran = foo.CreateTransaction();
                tran.AddCondition(Condition.KeyExists("abc"));
                tran.StringIncrementAsync("i");
                tran.Execute();

                i = (int)raw.StringGet("tran:i");
                Assert.AreEqual(1, i);
            }
        }
    }
}
