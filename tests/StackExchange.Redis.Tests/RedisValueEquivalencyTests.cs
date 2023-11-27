using System;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class RedisValueEquivalency
{
    // internal storage types: null, integer, double, string, raw
    // public perceived types: int, long, double, bool, memory / byte[]

    [Fact]
    public void Int32_Matrix()
    {
        static void Check(RedisValue known, RedisValue test)
        {
            KeyAndValueTests.CheckSame(known, test);
            if (known.IsNull)
            {
                Assert.True(test.IsNull);
                Assert.False(((int?)test).HasValue);
            }
            else
            {
                Assert.False(test.IsNull);
                Assert.Equal((int)known, ((int?)test)!.Value);
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
        static void Check(RedisValue known, RedisValue test)
        {
            KeyAndValueTests.CheckSame(known, test);
            if (known.IsNull)
            {
                Assert.True(test.IsNull);
                Assert.False(((long?)test).HasValue);
            }
            else
            {
                Assert.False(test.IsNull);
                Assert.Equal((long)known, ((long?)test!).Value);
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
        static void Check(RedisValue known, RedisValue test)
        {
            KeyAndValueTests.CheckSame(known, test);
            if (known.IsNull)
            {
                Assert.True(test.IsNull);
                Assert.False(((double?)test).HasValue);
            }
            else
            {
                Assert.False(test.IsNull);
                Assert.Equal((double)known, ((double?)test)!.Value);
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

    private static void CheckString(RedisValue value, string expected)
    {
        var s = value.ToString();
        Assert.True(s == expected, $"'{s}' vs '{expected}'");
    }

    private static byte[]? Bytes(string? s) => s == null ? null : Encoding.UTF8.GetBytes(s);

    private static string LineNumber([CallerLineNumber] int lineNumber = 0) => lineNumber.ToString();

    [Fact]
    public void RedisValueStartsWith()
    {
        // test strings
        RedisValue x = "abc";
        Assert.True(x.StartsWith("a"), LineNumber());
        Assert.True(x.StartsWith("ab"), LineNumber());
        Assert.True(x.StartsWith("abc"), LineNumber());
        Assert.False(x.StartsWith("abd"), LineNumber());
        Assert.False(x.StartsWith("abcd"), LineNumber());
        Assert.False(x.StartsWith(123), LineNumber());
        Assert.False(x.StartsWith(false), LineNumber());

        // test binary
        x = Encoding.ASCII.GetBytes("abc");
        Assert.True(x.StartsWith("a"), LineNumber());
        Assert.True(x.StartsWith("ab"), LineNumber());
        Assert.True(x.StartsWith("abc"), LineNumber());
        Assert.False(x.StartsWith("abd"), LineNumber());
        Assert.False(x.StartsWith("abcd"), LineNumber());
        Assert.False(x.StartsWith(123), LineNumber());
        Assert.False(x.StartsWith(false), LineNumber());

        Assert.True(x.StartsWith(Encoding.ASCII.GetBytes("a")), LineNumber());
        Assert.True(x.StartsWith(Encoding.ASCII.GetBytes("ab")), LineNumber());
        Assert.True(x.StartsWith(Encoding.ASCII.GetBytes("abc")), LineNumber());
        Assert.False(x.StartsWith(Encoding.ASCII.GetBytes("abd")), LineNumber());
        Assert.False(x.StartsWith(Encoding.ASCII.GetBytes("abcd")), LineNumber());

        x = 10; // integers are effectively strings in this context
        Assert.True(x.StartsWith(1), LineNumber());
        Assert.True(x.StartsWith(10), LineNumber());
        Assert.False(x.StartsWith(100), LineNumber());
    }

    [Fact]
    public void TryParseInt64()
    {
        Assert.True(((RedisValue)123).TryParse(out long l));
        Assert.Equal(123, l);

        Assert.True(((RedisValue)123.0).TryParse(out l));
        Assert.Equal(123, l);

        Assert.True(((RedisValue)(int.MaxValue + 123L)).TryParse(out l));
        Assert.Equal(int.MaxValue + 123L, l);

        Assert.True(((RedisValue)"123").TryParse(out l));
        Assert.Equal(123, l);

        Assert.True(((RedisValue)(-123)).TryParse(out l));
        Assert.Equal(-123, l);

        Assert.True(default(RedisValue).TryParse(out l));
        Assert.Equal(0, l);

        Assert.True(((RedisValue)123.0).TryParse(out l));
        Assert.Equal(123, l);

        Assert.False(((RedisValue)"abc").TryParse(out long _));
        Assert.False(((RedisValue)"123.1").TryParse(out long _));
        Assert.False(((RedisValue)123.1).TryParse(out long _));
    }

    [Fact]
    public void TryParseInt32()
    {
        Assert.True(((RedisValue)123).TryParse(out int i));
        Assert.Equal(123, i);

        Assert.True(((RedisValue)123.0).TryParse(out i));
        Assert.Equal(123, i);

        Assert.False(((RedisValue)(int.MaxValue + 123L)).TryParse(out int _));

        Assert.True(((RedisValue)"123").TryParse(out i));
        Assert.Equal(123, i);

        Assert.True(((RedisValue)(-123)).TryParse(out i));
        Assert.Equal(-123, i);

        Assert.True(default(RedisValue).TryParse(out i));
        Assert.Equal(0, i);

        Assert.True(((RedisValue)123.0).TryParse(out i));
        Assert.Equal(123, i);

        Assert.False(((RedisValue)"abc").TryParse(out int _));
        Assert.False(((RedisValue)"123.1").TryParse(out int _));
        Assert.False(((RedisValue)123.1).TryParse(out int _));
    }

    [Fact]
    public void TryParseDouble()
    {
        Assert.True(((RedisValue)123).TryParse(out double d));
        Assert.Equal(123, d);

        Assert.True(((RedisValue)123.0).TryParse(out d));
        Assert.Equal(123.0, d);

        Assert.True(((RedisValue)123.1).TryParse(out d));
        Assert.Equal(123.1, d);

        Assert.True(((RedisValue)(int.MaxValue + 123L)).TryParse(out d));
        Assert.Equal(int.MaxValue + 123L, d);

        Assert.True(((RedisValue)"123").TryParse(out d));
        Assert.Equal(123.0, d);

        Assert.True(((RedisValue)(-123)).TryParse(out d));
        Assert.Equal(-123.0, d);

        Assert.True(default(RedisValue).TryParse(out d));
        Assert.Equal(0.0, d);

        Assert.True(((RedisValue)123.0).TryParse(out d));
        Assert.Equal(123.0, d);

        Assert.True(((RedisValue)"123.1").TryParse(out d));
        Assert.Equal(123.1, d);

        Assert.False(((RedisValue)"abc").TryParse(out double _));
    }

    [Fact]
    public void RedisValueLengthString()
    {
        RedisValue value = "abc";
        Assert.Equal(RedisValue.StorageType.String, value.Type);
        Assert.Equal(3, value.Length());
    }

    [Fact]
    public void RedisValueLengthDouble()
    {
        RedisValue value = Math.PI;
        Assert.Equal(RedisValue.StorageType.Double, value.Type);
        Assert.Equal(18, value.Length());
    }

    [Fact]
    public void RedisValueLengthInt64()
    {
        RedisValue value = 123;
        Assert.Equal(RedisValue.StorageType.Int64, value.Type);
        Assert.Equal(3, value.Length());
    }

    [Fact]
    public void RedisValueLengthUInt64()
    {
        RedisValue value = ulong.MaxValue - 5;
        Assert.Equal(RedisValue.StorageType.UInt64, value.Type);
        Assert.Equal(20, value.Length());
    }

    [Fact]
    public void RedisValueLengthRaw()
    {
        RedisValue value = new byte[] { 0, 1, 2 };
        Assert.Equal(RedisValue.StorageType.Raw, value.Type);
        Assert.Equal(3, value.Length());
    }

    [Fact]
    public void RedisValueLengthNull()
    {
        RedisValue value = RedisValue.Null;
        Assert.Equal(RedisValue.StorageType.Null, value.Type);
        Assert.Equal(0, value.Length());
    }
}
