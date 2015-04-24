using System;
using System.Globalization;
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

            RedisValue bool1 = true;
            CheckNotNull(bool1);
            RedisValue bool2 = false;
            CheckNotNull(bool2);
            RedisValue bool3 = true;
            CheckNotNull(bool3);

            CheckSame(a0, a0);
            CheckSame(a1, a1);
            CheckSame(a0, a1);

            CheckSame(b0, b0);
            CheckSame(b1, b1);
            CheckSame(b0, b1);

            CheckSame(i4, i4);
            CheckSame(i8, i8);
            CheckSame(i4, i8);

            CheckSame(bool1, bool3);
            CheckNotSame(bool1, bool2);
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


        [Test]
        public void ValuesAreConvertible()
        {
            RedisValue val = 123;
            object o = val;
            byte[] blob = (byte[])Convert.ChangeType(o, typeof(byte[]));

            Assert.AreEqual(3, blob.Length);
            Assert.AreEqual((byte)'1', blob[0]);
            Assert.AreEqual((byte)'2', blob[1]);
            Assert.AreEqual((byte)'3', blob[2]);

            Assert.AreEqual((double)123, Convert.ToDouble(o));

            IConvertible c = (IConvertible)o;
            Assert.AreEqual((short)123, c.ToInt16(CultureInfo.InvariantCulture));
            Assert.AreEqual((int)123, c.ToInt32(CultureInfo.InvariantCulture));
            Assert.AreEqual((long)123, c.ToInt64(CultureInfo.InvariantCulture));
            Assert.AreEqual((float)123, c.ToSingle(CultureInfo.InvariantCulture));
            Assert.AreEqual("123", c.ToString(CultureInfo.InvariantCulture));
            Assert.AreEqual((double)123, c.ToDouble(CultureInfo.InvariantCulture));
            Assert.AreEqual((decimal)123, c.ToDecimal(CultureInfo.InvariantCulture));
            Assert.AreEqual((ushort)123, c.ToUInt16(CultureInfo.InvariantCulture));
            Assert.AreEqual((uint)123, c.ToUInt32(CultureInfo.InvariantCulture));
            Assert.AreEqual((ulong)123, c.ToUInt64(CultureInfo.InvariantCulture));

            blob = (byte[])c.ToType(typeof(byte[]), CultureInfo.InvariantCulture);
            Assert.AreEqual(3, blob.Length);
            Assert.AreEqual((byte)'1', blob[0]);
            Assert.AreEqual((byte)'2', blob[1]);
            Assert.AreEqual((byte)'3', blob[2]);
        }

        [Test]
        public void CanBeDynamic()
        {
            RedisValue val = "abc";
            object o = val;
            dynamic d = o;
            byte[] blob = (byte[])d; // could be in a try/catch
            Assert.AreEqual(3, blob.Length);
            Assert.AreEqual((byte)'a', blob[0]);
            Assert.AreEqual((byte)'b', blob[1]);
            Assert.AreEqual((byte)'c', blob[2]);
        }

        [Test]
        public void TryParse()
        {
            {
                RedisValue val = "1";
                int i;
                Assert.IsTrue(val.TryParse(out i));
                Assert.AreEqual(1, i);
                long l;
                Assert.IsTrue(val.TryParse(out l));
                Assert.AreEqual(1L, l);
                double d;
                Assert.IsTrue(val.TryParse(out d));
                Assert.AreEqual(1.0, l);
            }

            {
                RedisValue val = "8675309";
                int i;
                Assert.IsTrue(val.TryParse(out i));
                Assert.AreEqual(8675309, i);
                long l;
                Assert.IsTrue(val.TryParse(out l));
                Assert.AreEqual(8675309L, l);
                double d;
                Assert.IsTrue(val.TryParse(out d));
                Assert.AreEqual(8675309.0, l);
            }

            {
                RedisValue val = "3.14159";
                double d;
                Assert.IsTrue(val.TryParse(out d));
                Assert.AreEqual(3.14159, d);
            }

            {
                RedisValue val = "not a real number";
                int i;
                Assert.IsFalse(val.TryParse(out i));
                long l;
                Assert.IsFalse(val.TryParse(out l));
                double d;
                Assert.IsFalse(val.TryParse(out d));
            }
        }
    }
}
