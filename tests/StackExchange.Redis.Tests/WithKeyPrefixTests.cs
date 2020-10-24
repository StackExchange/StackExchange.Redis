using System;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class WithKeyPrefixTests : TestBase
    {
        public WithKeyPrefixTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public void BlankPrefixYieldsSame_Bytes()
        {
            using (var conn = Create())
            {
                var raw = conn.GetDatabase();
                var prefixed = raw.WithKeyPrefix(new byte[0]);
                Assert.Same(raw, prefixed);
            }
        }

        [Fact]
        public void BlankPrefixYieldsSame_String()
        {
            using (var conn = Create())
            {
                var raw = conn.GetDatabase();
                var prefixed = raw.WithKeyPrefix("");
                Assert.Same(raw, prefixed);
            }
        }

        [Fact]
        public void NullPrefixIsError_Bytes()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                using var conn = Create();
                var raw = conn.GetDatabase();
                raw.WithKeyPrefix((byte[])null);
            });
        }

        [Fact]
        public void NullPrefixIsError_String()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                using var conn = Create();
                var raw = conn.GetDatabase();
                raw.WithKeyPrefix((string)null);
            });
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData(null)]
        public void NullDatabaseIsError(string prefix)
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                IDatabase raw = null;
                raw.WithKeyPrefix(prefix);
            });
        }

        [Fact]
        public void BasicSmokeTest()
        {
            using (var conn = Create())
            {
                var raw = conn.GetDatabase();

                var prefix = Me();
                var foo = raw.WithKeyPrefix(prefix);
                var foobar = foo.WithKeyPrefix("bar");

                string key = Me();

                string s = Guid.NewGuid().ToString(), t = Guid.NewGuid().ToString();

                foo.StringSet(key, s, flags: CommandFlags.FireAndForget);
                var val = (string)foo.StringGet(key);
                Assert.Equal(s, val); // fooBasicSmokeTest

                foobar.StringSet(key, t, flags: CommandFlags.FireAndForget);
                val = foobar.StringGet(key);
                Assert.Equal(t, val); // foobarBasicSmokeTest

                val = foo.StringGet("bar" + key);
                Assert.Equal(t, val); // foobarBasicSmokeTest

                val = raw.StringGet(prefix + key);
                Assert.Equal(s, val); // fooBasicSmokeTest

                val = raw.StringGet(prefix + "bar" + key);
                Assert.Equal(t, val); // foobarBasicSmokeTest
            }
        }

        [Fact]
        public void ConditionTest()
        {
            using (var conn = Create())
            {
                var raw = conn.GetDatabase();

                var prefix = Me() + ":";
                var foo = raw.WithKeyPrefix(prefix);

                raw.KeyDelete(prefix + "abc", CommandFlags.FireAndForget);
                raw.KeyDelete(prefix + "i", CommandFlags.FireAndForget);

                // execute while key exists
                raw.StringSet(prefix + "abc", "def", flags: CommandFlags.FireAndForget);
                var tran = foo.CreateTransaction();
                tran.AddCondition(Condition.KeyExists("abc"));
                tran.StringIncrementAsync("i");
                tran.Execute();

                int i = (int)raw.StringGet(prefix + "i");
                Assert.Equal(1, i);

                // repeat without key
                raw.KeyDelete(prefix + "abc", CommandFlags.FireAndForget);
                tran = foo.CreateTransaction();
                tran.AddCondition(Condition.KeyExists("abc"));
                tran.StringIncrementAsync("i");
                tran.Execute();

                i = (int)raw.StringGet(prefix + "i");
                Assert.Equal(1, i);
            }
        }
    }
}
