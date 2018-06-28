using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class RedisValueEquivalency
    {
        // internal storage types: null, integer, double, string, raw
        // public perceived types: int, long, double, bool, memory / byte[]

        [Fact]
        public void Int32_Matrix()
        {
            void Check(RedisValue known, RedisValue test)
            {
                KeysAndValues.CheckSame(known, test);
                if (known.IsNull)
                {
                    Assert.True(test.IsNull);
                    Assert.False(((int?)test).HasValue);
                }
                else
                {
                    Assert.False(test.IsNull);
                    Assert.Equal((int)known, ((int?)test).Value);
                    Assert.Equal((int)known, (int)test);
                }
                Assert.Equal((int)known, (int)test);
            }
            Check(42, 42);
            Check(42, 42.0);
            Check(42, "42");
            Check(42, "42.0");
            Check(42, Bytes("42"));
            Check(42, Bytes("42.0"));
            CheckString(42, "42");

            Check(-42, -42);
            Check(-42, -42.0);
            Check(-42, "-42");
            Check(-42, "-42.0");
            Check(-42, Bytes("-42"));
            Check(-42, Bytes("-42.0"));
            CheckString(-42, "-42");

            Check(1, true);
            Check(0, false);
        }

        [Fact]
        public void Int64_Matrix()
        {
            void Check(RedisValue known, RedisValue test)
            {
                KeysAndValues.CheckSame(known, test);
                if (known.IsNull)
                {
                    Assert.True(test.IsNull);
                    Assert.False(((long?)test).HasValue);
                }
                else
                {
                    Assert.False(test.IsNull);
                    Assert.Equal((long)known, ((long?)test).Value);
                    Assert.Equal((long)known, (long)test);
                }
                Assert.Equal((long)known, (long)test);
            }
            Check(1099511627848, 1099511627848);
            Check(1099511627848, 1099511627848.0);
            Check(1099511627848, "1099511627848");
            Check(1099511627848, "1099511627848.0");
            Check(1099511627848, Bytes("1099511627848"));
            Check(1099511627848, Bytes("1099511627848.0"));
            CheckString(1099511627848, "1099511627848");

            Check(-1099511627848, -1099511627848);
            Check(-1099511627848, -1099511627848);
            Check(-1099511627848, "-1099511627848");
            Check(-1099511627848, "-1099511627848.0");
            Check(-1099511627848, Bytes("-1099511627848"));
            Check(-1099511627848, Bytes("-1099511627848.0"));
            CheckString(-1099511627848, "-1099511627848");

            Check(1L, true);
            Check(0L, false);
        }

        [Fact]
        public void Double_Matrix()
        {
            void Check(RedisValue known, RedisValue test)
            {
                KeysAndValues.CheckSame(known, test);
                if (known.IsNull)
                {
                    Assert.True(test.IsNull);
                    Assert.False(((double?)test).HasValue);
                }
                else
                {
                    Assert.False(test.IsNull);
                    Assert.Equal((double)known, ((double?)test).Value);
                    Assert.Equal((double)known, (double)test);
                }
                Assert.Equal((double)known, (double)test);
            }
            Check(1099511627848.0, 1099511627848);
            Check(1099511627848.0, 1099511627848.0);
            Check(1099511627848.0, "1099511627848");
            Check(1099511627848.0, "1099511627848.0");
            Check(1099511627848.0, Bytes("1099511627848"));
            Check(1099511627848.0, Bytes("1099511627848.0"));
            CheckString(1099511627848.0, "1099511627848");

            Check(-1099511627848.0, -1099511627848);
            Check(-1099511627848.0, -1099511627848);
            Check(-1099511627848.0, "-1099511627848");
            Check(-1099511627848.0, "-1099511627848.0");
            Check(-1099511627848.0, Bytes("-1099511627848"));
            Check(-1099511627848.0, Bytes("-1099511627848.0"));
            CheckString(-1099511627848.0, "-1099511627848");

            Check(1.0, true);
            Check(0.0, false);

            Check(1099511627848.6001, 1099511627848.6001);
            Check(1099511627848.6001, "1099511627848.6001");
            Check(1099511627848.6001, Bytes("1099511627848.6001"));
            CheckString(1099511627848.6001, "1099511627848.6001");

            Check(-1099511627848.6001, -1099511627848.6001);
            Check(-1099511627848.6001, "-1099511627848.6001");
            Check(-1099511627848.6001, Bytes("-1099511627848.6001"));
            CheckString(-1099511627848.6001, "-1099511627848.6001");

            Check(double.NegativeInfinity, double.NegativeInfinity);
            Check(double.NegativeInfinity, "-inf");
            CheckString(double.NegativeInfinity, "-inf");

            Check(double.PositiveInfinity, double.PositiveInfinity);
            Check(double.PositiveInfinity, "+inf");
            CheckString(double.PositiveInfinity, "+inf");
        }

        static void CheckString(RedisValue value, string expected)
        {
            var s = value.ToString();
            Assert.True(s == expected, $"'{s}' vs '{expected}'");
        }

        static byte[] Bytes(string s) => s == null ? null : Encoding.UTF8.GetBytes(s);
    }
}
