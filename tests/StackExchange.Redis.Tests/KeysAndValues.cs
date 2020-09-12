using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class KeysAndValues
    {
        [Fact]
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
            RedisValue b0 = new[] { (byte)'b' };
            CheckNotNull(b0);
            RedisValue b1 = new[] { (byte)'b' };
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

        internal static void CheckSame(RedisValue x, RedisValue y)
        {
            Assert.True(Equals(x, y), "Equals(x, y)");
            Assert.True(Equals(y, x), "Equals(y, x)");
            Assert.True(EqualityComparer<RedisValue>.Default.Equals(x, y), "EQ(x,y)");
            Assert.True(EqualityComparer<RedisValue>.Default.Equals(y, x), "EQ(y,x)");
            Assert.True(x == y, "x==y");
            Assert.True(y == x, "y==x");
            Assert.False(x != y, "x!=y");
            Assert.False(y != x, "y!=x");
            Assert.True(x.Equals(y),"x.EQ(y)");
            Assert.True(y.Equals(x), "y.EQ(x)");
            Assert.True(x.GetHashCode() == y.GetHashCode(), "GetHashCode");
        }

        private void CheckNotSame(RedisValue x, RedisValue y)
        {
            Assert.False(Equals(x, y));
            Assert.False(Equals(y, x));
            Assert.False(EqualityComparer<RedisValue>.Default.Equals(x, y));
            Assert.False(EqualityComparer<RedisValue>.Default.Equals(y, x));
            Assert.False(x == y);
            Assert.False(y == x);
            Assert.True(x != y);
            Assert.True(y != x);
            Assert.False(x.Equals(y));
            Assert.False(y.Equals(x));
            Assert.False(x.GetHashCode() == y.GetHashCode()); // well, very unlikely
        }

        private void CheckNotNull(RedisValue value)
        {
            Assert.False(value.IsNull);
            Assert.NotNull((byte[])value);
            Assert.NotNull((string)value);
            Assert.NotEqual(-1, value.GetHashCode());

            Assert.NotNull((string)value);
            Assert.NotNull((byte[])value);

            CheckSame(value, value);
            CheckNotSame(value, default(RedisValue));
            CheckNotSame(value, (string)null);
            CheckNotSame(value, (byte[])null);
        }

        internal static void CheckNull(RedisValue value)
        {
            Assert.True(value.IsNull);
            Assert.True(value.IsNullOrEmpty);
            Assert.False(value.IsInteger);
            Assert.Equal(-1, value.GetHashCode());

            Assert.Null((string)value);
            Assert.Null((byte[])value);

            Assert.Equal(0, (int)value);
            Assert.Equal(0L, (long)value);

            CheckSame(value, value);
            //CheckSame(value, default(RedisValue));
            //CheckSame(value, (string)null);
            //CheckSame(value, (byte[])null);
        }

        [Fact]
        public void ValuesAreConvertible()
        {
            RedisValue val = 123;
            object o = val;
            byte[] blob = (byte[])Convert.ChangeType(o, typeof(byte[]));

            Assert.Equal(3, blob.Length);
            Assert.Equal((byte)'1', blob[0]);
            Assert.Equal((byte)'2', blob[1]);
            Assert.Equal((byte)'3', blob[2]);

            Assert.Equal(123, Convert.ToDouble(o));

            IConvertible c = (IConvertible)o;
            // ReSharper disable RedundantCast
            Assert.Equal((short)123, c.ToInt16(CultureInfo.InvariantCulture));
            Assert.Equal((int)123, c.ToInt32(CultureInfo.InvariantCulture));
            Assert.Equal((long)123, c.ToInt64(CultureInfo.InvariantCulture));
            Assert.Equal((float)123, c.ToSingle(CultureInfo.InvariantCulture));
            Assert.Equal("123", c.ToString(CultureInfo.InvariantCulture));
            Assert.Equal((double)123, c.ToDouble(CultureInfo.InvariantCulture));
            Assert.Equal((decimal)123, c.ToDecimal(CultureInfo.InvariantCulture));
            Assert.Equal((ushort)123, c.ToUInt16(CultureInfo.InvariantCulture));
            Assert.Equal((uint)123, c.ToUInt32(CultureInfo.InvariantCulture));
            Assert.Equal((ulong)123, c.ToUInt64(CultureInfo.InvariantCulture));

            blob = (byte[])c.ToType(typeof(byte[]), CultureInfo.InvariantCulture);
            Assert.Equal(3, blob.Length);
            Assert.Equal((byte)'1', blob[0]);
            Assert.Equal((byte)'2', blob[1]);
            Assert.Equal((byte)'3', blob[2]);
        }

        [Fact]
        public void CanBeDynamic()
        {
            RedisValue val = "abc";
            object o = val;
            dynamic d = o;
            byte[] blob = (byte[])d; // could be in a try/catch
            Assert.Equal(3, blob.Length);
            Assert.Equal((byte)'a', blob[0]);
            Assert.Equal((byte)'b', blob[1]);
            Assert.Equal((byte)'c', blob[2]);
        }
    }
}
