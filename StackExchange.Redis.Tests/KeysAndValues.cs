using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class KeysAndValues
    {
        [Test]
        public void TestValues()
        {
            RedisValue @default = default(RedisValue);
            CheckNull(@default);

            RedisValue nullString = (string)null;
            CheckNull(nullString);

            RedisValue nullBlob = (byte[])null;
            CheckNull(nullBlob);

            RedisValue emptyString = "";
            CheckNotNull(emptyString);

            RedisValue emptyBlob = new byte[0];
            CheckNotNull(emptyBlob);

            RedisValue a0 = new string('a', 1);
            CheckNotNull(a0);
            RedisValue a1 = new string('a', 1);
            CheckNotNull(a1);
            RedisValue b0 = new [] { (byte)'b' };
            CheckNotNull(b0);
            RedisValue b1 = new [] { (byte)'b' };
            CheckNotNull(b1);

            RedisValue i4 = 1;
            CheckNotNull(i4);
            RedisValue i8 = 1L;
            CheckNotNull(i8);

            CheckSame(a0, a0);
            CheckSame(a1, a1);
            CheckSame(a0, a1);

            CheckSame(b0, b0);
            CheckSame(b1, b1);
            CheckSame(b0, b1);

            CheckSame(i4, i4);
            CheckSame(i8, i8);
            CheckSame(i4, i8);
        }

        private void CheckSame(RedisValue x, RedisValue y)
        {
            Assert.IsTrue(Equals(x, y));
            Assert.IsTrue(x.Equals(y));
            Assert.IsTrue(y.Equals(x));
            Assert.IsTrue(x.GetHashCode() == y.GetHashCode());
        }
        private void CheckNotSame(RedisValue x, RedisValue y)
        {
            Assert.IsFalse(Equals(x, y));
            Assert.IsFalse(x.Equals(y));
            Assert.IsFalse(y.Equals(x));
            Assert.IsFalse(x.GetHashCode() == y.GetHashCode()); // well, very unlikely
        }

        private void CheckNotNull(RedisValue value)
        {
            Assert.IsFalse(value.IsNull);
            Assert.IsNotNull((byte[])value);
            Assert.IsNotNull((string)value);
            Assert.AreNotEqual(-1, value.GetHashCode());

            Assert.IsNotNull((string)value);
            Assert.IsNotNull((byte[])value);

            CheckSame(value, value);
            CheckNotSame(value, default(RedisValue));
            CheckNotSame(value, (string)null);
            CheckNotSame(value, (byte[])null);
        }
        private void CheckNull(RedisValue value)
        {
            Assert.IsTrue(value.IsNull);
            Assert.IsTrue(value.IsNullOrEmpty);
            Assert.IsFalse(value.IsInteger);
            Assert.AreEqual(-1, value.GetHashCode());

            Assert.IsNull((string)value);
            Assert.IsNull((byte[])value);

            Assert.AreEqual(0, (int)value);
            Assert.AreEqual(0L, (long)value);

            CheckSame(value, value);
            CheckSame(value, default(RedisValue));
            CheckSame(value, (string)null);
            CheckSame(value, (byte[])null);
        }
    }
}
