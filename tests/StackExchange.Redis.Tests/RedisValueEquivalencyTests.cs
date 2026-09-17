using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class RedisValueEquivalencyUnitTests
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
        Check(42, Bytes("42"u8));
        Check(42, Bytes("42.0"u8));
        Check(42, Bytes("4"u8, "2"u8)); // multi-segment sequence
        Check(42, Bytes("4"u8, "2.0"u8)); // multi-segment sequence
        CheckString(42, "42");

        Check(-42, -42);
        Check(-42, -42.0);
        Check(-42, "-42");
        Check(-42, "-42.0");
        Check(-42, Bytes("-42"u8));
        Check(-42, Bytes("-42.0"u8));
        Check(-42, Bytes("-"u8, "42"u8)); // multi-segment sequence
        Check(-42, Bytes("-4"u8, "2"u8, ".0"u8)); // multi-segment sequence (3 segments)
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
        Check(1099511627848, Bytes("1099511627848"u8));
        Check(1099511627848, Bytes("1099511627848.0"u8));
        Check(1099511627848, Bytes("109951"u8, "1627848"u8)); // multi-segment sequence
        Check(1099511627848, Bytes("109951"u8, "1627848"u8, ".0"u8)); // multi-segment sequence
        CheckString(1099511627848, "1099511627848");

        Check(-1099511627848, -1099511627848);
        Check(-1099511627848, -1099511627848);
        Check(-1099511627848, "-1099511627848");
        Check(-1099511627848, "-1099511627848.0");
        Check(-1099511627848, Bytes("-1099511627848"u8));
        Check(-1099511627848, Bytes("-1099511627848.0"u8));
        Check(-1099511627848, Bytes("-109951"u8, "1627848"u8)); // multi-segment sequence
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
        Check(1099511627848.0, Bytes("1099511627848"u8));
        Check(1099511627848.0, Bytes("1099511627848.0"u8));
        Check(1099511627848.0, Bytes("109951"u8, "1627848"u8)); // multi-segment sequence
        Check(1099511627848.0, Bytes("1099511627848"u8, ".0"u8)); // multi-segment sequence
        CheckString(1099511627848.0, "1099511627848");

        Check(-1099511627848.0, -1099511627848);
        Check(-1099511627848.0, -1099511627848);
        Check(-1099511627848.0, "-1099511627848");
        Check(-1099511627848.0, "-1099511627848.0");
        Check(-1099511627848.0, Bytes("-1099511627848"u8));
        Check(-1099511627848.0, Bytes("-1099511627848.0"u8));
        CheckString(-1099511627848.0, "-1099511627848");

        Check(1.0, true);
        Check(0.0, false);

        Check(1099511627848.6001, 1099511627848.6001);
        Check(1099511627848.6001, "1099511627848.6001");
        Check(1099511627848.6001, Bytes("1099511627848.6001"u8));
        Check(1099511627848.6001, Bytes("1099511627848"u8, ".6001"u8)); // multi-segment sequence
        CheckString(1099511627848.6001, "1099511627848.6001");

        Check(-1099511627848.6001, -1099511627848.6001);
        Check(-1099511627848.6001, "-1099511627848.6001");
        Check(-1099511627848.6001, Bytes("-1099511627848.6001"u8));
        CheckString(-1099511627848.6001, "-1099511627848.6001");

        Check(double.NegativeInfinity, double.NegativeInfinity);
        CheckString(double.NegativeInfinity, "-inf");

        Check(double.PositiveInfinity, double.PositiveInfinity);
        CheckString(double.PositiveInfinity, "+inf");

        Check(double.NaN, double.NaN);
        CheckString(double.NaN, "NaN");
    }

    [Theory]
    [InlineData("na")]
    [InlineData("nan")]
    [InlineData("nans")]
    [InlineData("in")]
    [InlineData("inf")]
    [InlineData("info")]
    public void SpecialCaseEqualityRules_String(string value)
    {
        RedisValue x = value, y = value;
        Assert.Equal(x, y);

        Assert.True(x.Equals(y));
        Assert.True(y.Equals(x));
        Assert.True(x == y);
        Assert.True(y == x);
        Assert.False(x != y);
        Assert.False(y != x);
        Assert.Equal(x.GetHashCode(), y.GetHashCode());
    }

    [Theory]
    [InlineData("na")]
    [InlineData("nan")]
    [InlineData("nans")]
    [InlineData("in")]
    [InlineData("inf")]
    [InlineData("info")]
    public void SpecialCaseEqualityRules_Bytes(string value)
    {
        byte[] bytes0 = Encoding.UTF8.GetBytes(value),
               bytes1 = Encoding.UTF8.GetBytes(value);
        Assert.NotSame(bytes0, bytes1);
        RedisValue x = bytes0, y = bytes1;

        Assert.True(x.Equals(y));
        Assert.True(y.Equals(x));
        Assert.True(x == y);
        Assert.True(y == x);
        Assert.False(x != y);
        Assert.False(y != x);
        Assert.Equal(x.GetHashCode(), y.GetHashCode());
    }

    [Theory]
    [InlineData("na")]
    [InlineData("nan")]
    [InlineData("nans")]
    [InlineData("in")]
    [InlineData("inf")]
    [InlineData("info")]
    public void SpecialCaseEqualityRules_Hybrid(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        RedisValue x = bytes, y = value;

        Assert.True(x.Equals(y));
        Assert.True(y.Equals(x));
        Assert.True(x == y);
        Assert.True(y == x);
        Assert.False(x != y);
        Assert.False(y != x);
        Assert.Equal(x.GetHashCode(), y.GetHashCode());
    }

    [Theory]
    [InlineData("na", "NA")]
    [InlineData("nan", "NAN")]
    [InlineData("nans", "NANS")]
    [InlineData("in", "IN")]
    [InlineData("inf", "INF")]
    [InlineData("info", "INFO")]
    public void SpecialCaseNonEqualityRules_String(string s, string t)
    {
        RedisValue x = s, y = t;
        Assert.False(x.Equals(y));
        Assert.False(y.Equals(x));
        Assert.False(x == y);
        Assert.False(y == x);
        Assert.True(x != y);
        Assert.True(y != x);
    }

    [Theory]
    [InlineData("na", "NA")]
    [InlineData("nan", "NAN")]
    [InlineData("nans", "NANS")]
    [InlineData("in", "IN")]
    [InlineData("inf", "INF")]
    [InlineData("info", "INFO")]
    public void SpecialCaseNonEqualityRules_Bytes(string s, string t)
    {
        RedisValue x = Encoding.UTF8.GetBytes(s), y = Encoding.UTF8.GetBytes(t);
        Assert.False(x.Equals(y));
        Assert.False(y.Equals(x));
        Assert.False(x == y);
        Assert.False(y == x);
        Assert.True(x != y);
        Assert.True(y != x);
    }

    [Theory]
    [InlineData("na", "NA")]
    [InlineData("nan", "NAN")]
    [InlineData("nans", "NANS")]
    [InlineData("in", "IN")]
    [InlineData("inf", "INF")]
    [InlineData("info", "INFO")]
    public void SpecialCaseNonEqualityRules_Hybrid(string s, string t)
    {
        RedisValue x = s, y = Encoding.UTF8.GetBytes(t);
        Assert.False(x.Equals(y));
        Assert.False(y.Equals(x));
        Assert.False(x == y);
        Assert.False(y == x);
        Assert.True(x != y);
        Assert.True(y != x);
    }

    private static void CheckString(RedisValue value, string expected)
    {
        var s = value.ToString();
        Assert.True(s == expected, $"'{s}' vs '{expected}'");
    }

    // single contiguous buffer => stored as a byte[] (StorageType.ByteArray)
    private static RedisValue Bytes(ReadOnlySpan<byte> value) => value.ToArray();

    // multiple chunks => a (deliberately) multi-segment ReadOnlySequence<byte> (StorageType.Sequence).
    // We trust the single-segment collapse logic, so callers pass >= 2 chunks to exercise the sequence path.
    private static RedisValue Bytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        => FragmentedSegment<byte>.Create(a.ToArray(), b.ToArray());

    private static RedisValue Bytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c)
        => FragmentedSegment<byte>.Create(a.ToArray(), b.ToArray(), c.ToArray());

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

        Assert.True(x.StartsWith((RedisValue)Encoding.ASCII.GetBytes("a")), LineNumber());
        Assert.True(x.StartsWith((RedisValue)Encoding.ASCII.GetBytes("ab")), LineNumber());
        Assert.True(x.StartsWith((RedisValue)Encoding.ASCII.GetBytes("abc")), LineNumber());
        Assert.False(x.StartsWith((RedisValue)Encoding.ASCII.GetBytes("abd")), LineNumber());
        Assert.False(x.StartsWith((RedisValue)Encoding.ASCII.GetBytes("abcd")), LineNumber());

        Assert.True(x.StartsWith("a"u8), LineNumber());
        Assert.True(x.StartsWith("ab"u8), LineNumber());
        Assert.True(x.StartsWith("abc"u8), LineNumber());
        Assert.False(x.StartsWith("abd"u8), LineNumber());
        Assert.False(x.StartsWith("abcd"u8), LineNumber());

        x = 10; // integers are effectively strings in this context
        Assert.True(x.StartsWith(1), LineNumber());
        Assert.True(x.StartsWith(10), LineNumber());
        Assert.False(x.StartsWith(100), LineNumber());
    }

    private static ReadOnlySpan<byte> Raw(params byte[] value) => value;

    // The third member of the contiguous-blob storage arm. Unlike a short blob or a byte[], no ordinary
    // conversion produces one, so fabricate it the way the toy server does - otherwise the kind goes untested
    // purely because it is awkward to reach.
    private sealed class ByteMemoryManager(byte[] value) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => value;
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { }
    }

    private static RedisValue Managed(ReadOnlySpan<byte> value)
    {
        var arr = value.ToArray();
        return RedisValue.CreateForeign(new ByteMemoryManager(arr), 0, arr.Length);
    }

    /// <summary>
    /// <see cref="RedisValue.EndsWithAscii"/> takes a different route for every storage kind - that is the
    /// whole point of it - so every kind gets asserted here, and each is checked against the kind it actually
    /// landed in. A test that only exercised strings would prove close to nothing.
    /// </summary>
    [Fact]
    public void RedisValueEndsWithAscii()
    {
        const byte Star = (byte)'*', Zero = (byte)'0', Five = (byte)'5', Eight = (byte)'8';

        // null and empty have no text, so nothing to end with
        Check(RedisValue.Null, RedisValue.StorageType.Null, Star, false);
        Check(RedisValue.EmptyString, RedisValue.StorageType.String, Star, false);

        // string
        Check("*", RedisValue.StorageType.String, Star, true);
        Check("5-*", RedisValue.StorageType.String, Star, true);
        Check("5-5", RedisValue.StorageType.String, Star, false);
        Check("5-5", RedisValue.StorageType.String, Five, true);
        Check("*x", RedisValue.StorageType.String, Star, false);

        // a non-ASCII tail can never match an ASCII byte, and must not be mistaken for one: the last UTF-8
        // byte of "é" is A9, and (char)0xA9 is a perfectly real char - so comparing chars is what keeps this
        // right, where comparing the encoded tail byte-for-byte would need the encode we are avoiding
        Check("é", RedisValue.StorageType.String, 0x29, false); // 0xA9 & 0x7F, had we masked
        Check("é*", RedisValue.StorageType.String, Star, true);

        // short blob (<= 8 bytes, held inline) and byte array (longer), which share a code path but not a kind
        Check(RedisValue.FromRaw("5-*"u8), RedisValue.StorageType.ShortBlob, Star, true);
        Check(RedisValue.FromRaw("5-5"u8), RedisValue.StorageType.ShortBlob, Star, false);
        Check(Bytes("1526919030474-*"u8), RedisValue.StorageType.ByteArray, Star, true);
        Check(Bytes("1526919030474-55"u8), RedisValue.StorageType.ByteArray, Star, false);
        Check(Managed("5-*"u8), RedisValue.StorageType.MemoryManager, Star, true);
        Check(Managed("5-5"u8), RedisValue.StorageType.MemoryManager, Star, false);

        // multi-segment sequence: the last byte is in the final segment, and also the case where that
        // segment holds only it
        Check(Bytes("5-"u8, "*"u8), RedisValue.StorageType.Sequence, Star, true);
        Check(Bytes("5"u8, "-*"u8), RedisValue.StorageType.Sequence, Star, true);
        Check(Bytes("5"u8, "-*"u8, "x"u8), RedisValue.StorageType.Sequence, Star, false);

        // integers: the final digit, without formatting anything
        Check(5, RedisValue.StorageType.Int64, Five, true);
        Check(5, RedisValue.StorageType.Int64, Star, false);
        Check(0, RedisValue.StorageType.Int64, Zero, true);
        Check(15, RedisValue.StorageType.Int64, Five, true);
        Check(15, RedisValue.StorageType.Int64, (byte)'1', false);

        // negatives take the sign off the *remainder*, not the value - so the extreme is the interesting one
        Check(-5, RedisValue.StorageType.Int64, Five, true);
        Check(-15, RedisValue.StorageType.Int64, Five, true);
        Check(long.MinValue, RedisValue.StorageType.Int64, Eight, true); // -9223372036854775808
        Check(long.MaxValue, RedisValue.StorageType.Int64, (byte)'7', true); // 9223372036854775807
        Check(ulong.MaxValue, RedisValue.StorageType.UInt64, Five, true); // 18446744073709551615

        // doubles format, which is fine; "inf" is the case worth pinning, since it is text rather than digits
        Check(1.5, RedisValue.StorageType.Double, Five, true);
        Check(1.5, RedisValue.StorageType.Double, Star, false);
        Check(double.PositiveInfinity, RedisValue.StorageType.Double, (byte)'f', true);
        Check(double.NegativeInfinity, RedisValue.StorageType.Double, (byte)'f', true);

        static void Check(RedisValue value, RedisValue.StorageType expectedKind, byte test, bool expected)
        {
            Assert.Equal(expectedKind, value.Type);
            Assert.Equal(expected, value.EndsWithAscii(test));
        }
    }

    [Fact]
    // The answer cannot depend on how the same value happens to be stored, which is the property the whole
    // per-kind switch has to preserve - so run one value through every kind that can hold it.
    public void RedisValueEndsWithAsciiAgreesAcrossStorageKinds()
    {
        RedisValue[] fifteen =
        {
            15,
            15u,
            15.0,
            "15",
            RedisValue.FromRaw("15"u8),
            Bytes("15"u8),
            Bytes("1"u8, "5"u8),
            Managed("15"u8),
        };

        foreach (var value in fifteen)
        {
            Assert.True(value.EndsWithAscii((byte)'5'), value.Type.ToString());
            Assert.False(value.EndsWithAscii((byte)'1'), value.Type.ToString());
            Assert.False(value.EndsWithAscii((byte)'*'), value.Type.ToString());
        }
    }

    [Fact]
    // A string-backed value holds UTF-16, and the prefix is bytes; the two do not have the same length, so
    // deciding "too short to match" by comparing char count against byte count is wrong the moment anything
    // is not ASCII. "e-acute, euro" is 2 chars but 5 UTF-8 bytes, so every prefix of 3 bytes or more was
    // rejected out of hand.
    public void RedisValueStartsWithMultiByteUtf8String()
    {
        RedisValue x = "é€"; // C3 A9 E2 82 AC
        Assert.Equal(5, x.Length());

        Assert.True(x.StartsWith(Raw(0xC3)), LineNumber());
        Assert.True(x.StartsWith(Raw(0xC3, 0xA9)), LineNumber());
        Assert.True(x.StartsWith(Raw(0xC3, 0xA9, 0xE2)), LineNumber());
        Assert.True(x.StartsWith(Raw(0xC3, 0xA9, 0xE2, 0x82)), LineNumber());
        Assert.True(x.StartsWith(Raw(0xC3, 0xA9, 0xE2, 0x82, 0xAC)), LineNumber());

        Assert.False(x.StartsWith(Raw(0xC3, 0xA9, 0xE2, 0x82, 0xAD)), LineNumber());
        Assert.False(x.StartsWith(Raw(0xC3, 0xA9, 0xE2, 0x82, 0xAC, 0x00)), LineNumber());
        Assert.False(x.StartsWith(Raw(0xE2)), LineNumber());

        // the byte-backed spelling of the same value must of course agree
        RedisValue y = Encoding.UTF8.GetBytes("é€");
        Assert.True(y.StartsWith(Raw(0xC3, 0xA9, 0xE2)), LineNumber());
    }

    [Fact]
    // The other half of the same problem: a prefix of N bytes needs at most N chars, so the string is cut to
    // that many before encoding - but cutting between the halves of a surrogate pair leaves a lone surrogate,
    // which the encoder replaces with U+FFFD (EF BF BD) rather than the bytes the caller is asking about.
    public void RedisValueStartsWithSurrogatePair()
    {
        RedisValue x = "a\U0001F600"; // 'a' + grinning face: 61 F0 9F 98 80
        Assert.Equal(5, x.Length());

        Assert.True(x.StartsWith(Raw(0x61)), LineNumber());
        Assert.True(x.StartsWith(Raw(0x61, 0xF0)), LineNumber());
        Assert.True(x.StartsWith(Raw(0x61, 0xF0, 0x9F)), LineNumber());
        Assert.True(x.StartsWith(Raw(0x61, 0xF0, 0x9F, 0x98, 0x80)), LineNumber());

        Assert.False(x.StartsWith(Raw(0x61, 0xEF)), LineNumber()); // the U+FFFD a naive cut would produce
        Assert.False(x.StartsWith(Raw(0x61, 0xF0, 0x9F, 0x98, 0x81)), LineNumber());
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
        Assert.Equal(RedisValue.StorageType.ByteArray, value.Type);
        Assert.Equal(3, value.Length());
    }

    [Fact]
    public void RedisValueLengthNull()
    {
        RedisValue value = RedisValue.Null;
        Assert.Equal(RedisValue.StorageType.Null, value.Type);
        Assert.Equal(0, value.Length());
    }

    // Comparing a string against a byte-backed value no longer decodes the blob into a transient string, so
    // these pin the relation itself: the answer must still be "the blob, read as UTF-8 text, equals the
    // string" for every input - including text that does not survive a UTF-8 round trip, which is where a
    // byte-domain comparison would silently disagree and put equal values in different hash buckets.
    //
    // Cases are built in code rather than as InlineData: a lone surrogate does not survive either a UTF-8
    // source file or xunit's argument serialization, and those are exactly the interesting inputs.
    private static byte[][] EquivalenceBlobs() =>
    [
        [],
        Encoding.UTF8.GetBytes("abc"),
        Encoding.UTF8.GetBytes("abd"),
        Encoding.UTF8.GetBytes("ab"),
        Encoding.UTF8.GetBytes("abcd"),
        Encoding.UTF8.GetBytes("моя строка"), // cyrillic
        Encoding.UTF8.GetBytes("你好世界"),                                // CJK
        Encoding.UTF8.GetBytes(new string([(char)0xD83D, (char)0xDE00])),                   // emoji: surrogate pair
        Encoding.UTF8.GetBytes(new string('x', 300)),                                       // spans several decode chunks
        [0xFF],                                     // invalid
        [0xC3],                                     // truncated 2-byte sequence
        [0xE4, 0xBD],                               // truncated 3-byte sequence
        [0xF0, 0x9F, 0x98],                         // truncated 4-byte sequence
        [0x61, 0xFF, 0x62],                         // invalid byte mid-payload
        [0xC0, 0xAF],                               // overlong encoding
        [0xED, 0xA0, 0x80],                         // surrogate encoded as UTF-8
        [0x61, 0xF0, 0x9F, 0x98, 0x80, 0x62],       // emoji mid-payload
    ];

    private static string[] EquivalenceStrings() =>
    [
        "",
        "abc",
        "abd",
        "ab",
        "abcd",
        "моя строка",
        "你好世界",
        new string([(char)0xD83D, (char)0xDE00]),
        new string('x', 300),
        "�",
        "a�b",
        new string([(char)0xD800]),   // lone high surrogate: UTF-8 cannot represent it
        "a��b",
        "a😀b",
    ];

    [Fact]
    public void StringVersusBlob_MatchesDecodedComparison()
    {
        foreach (var blob in EquivalenceBlobs())
        {
            RedisValue asBlob = blob;
            var decoded = (string?)asBlob;
            foreach (var s in EquivalenceStrings())
            {
                RedisValue asString = s;
                bool expected = s == decoded;
                var because = $"'{Escape(s)}' vs {BitConverter.ToString(blob)}";

                Assert.True(expected == (asString == asBlob), because);
                Assert.True(expected == (asBlob == asString), because + " (reversed)");
            }
        }
    }

    [Fact]
    public void StringVersusSegmentedBlob_MatchesAtEverySplit()
    {
        foreach (var blob in EquivalenceBlobs())
        {
            if (blob.Length < 2) continue;
            var decoded = (string?)(RedisValue)blob;

            // every split point, so a multi-byte sequence broken across segments is covered
            for (int split = 1; split < blob.Length; split++)
            {
                RedisValue segmented = Segmented(blob, split);
                Assert.Equal(decoded, (string?)segmented);

                foreach (var s in EquivalenceStrings())
                {
                    RedisValue asString = s;
                    Assert.True(
                        (s == decoded) == (asString == segmented),
                        $"'{Escape(s)}' vs {BitConverter.ToString(blob)} split at {split}");
                }
            }
        }
    }

    [Fact]
    public void EqualStringAndBlobShareHashCodes()
    {
        foreach (var blob in EquivalenceBlobs())
        {
            RedisValue asBlob = blob;
            foreach (var s in EquivalenceStrings())
            {
                RedisValue asString = s;
                if (asString != asBlob) continue;

                Assert.True(
                    asString.GetHashCode() == asBlob.GetHashCode(),
                    $"equal values must share a hash code: '{Escape(s)}' vs {BitConverter.ToString(blob)}");
            }
        }
    }

    private static RedisValue Segmented(byte[] payload, int split)
    {
        var first = new EquivalenceSegment(new ReadOnlyMemory<byte>(payload, 0, split), null);
        var second = new EquivalenceSegment(new ReadOnlyMemory<byte>(payload, split, payload.Length - split), first);
        return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (c is >= (char)32 and <= (char)126) sb.Append(c);
            else sb.Append("\\u").Append(((int)c).ToString("X4"));
        }
        return sb.ToString();
    }

    private sealed class EquivalenceSegment : ReadOnlySequenceSegment<byte>
    {
        public EquivalenceSegment(ReadOnlyMemory<byte> value, EquivalenceSegment? head)
        {
            Memory = value;
            if (head is not null)
            {
                RunningIndex = head.RunningIndex + head.Memory.Length;
                head.Next = this;
            }
        }
    }

    [Fact]
    public void DefaultComparer_MatchesTheTypesOwnEquality()
    {
        var comparer = RedisValue.EqualityComparer.Default;
        foreach (var blob in EquivalenceBlobs())
        {
            RedisValue asBlob = blob;
            foreach (var s in EquivalenceStrings())
            {
                RedisValue asString = s;
                var because = $"'{Escape(s)}' vs {BitConverter.ToString(blob)}";

                Assert.True((asString == asBlob) == comparer.Equals(asString, asBlob), because);
                Assert.Equal(asString.GetHashCode(), comparer.GetHashCode(asString));
                Assert.Equal(asBlob.GetHashCode(), comparer.GetHashCode(asBlob));
            }
        }
    }

    [Fact]
    public void BinaryComparer_AgreesWithDefaultOnWellFormedText()
    {
        var binary = RedisValue.EqualityComparer.Binary;
        foreach (var blob in EquivalenceBlobs())
        {
            RedisValue asBlob = blob;
            foreach (var s in EquivalenceStrings())
            {
                // The two readings may differ exactly where UTF8 does not round-trip, and either side can be
                // the culprit: a string holding an unpaired surrogate, or a blob that is not canonical UTF8
                // (0xFF decodes to U+FFFD but re-encodes to EF BF BD). Everywhere else they must agree.
                if (Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(s)) != s) continue;
                if (!Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(blob)).AsSpan().SequenceEqual(blob)) continue;

                RedisValue asString = s;
                Assert.True(
                    (asString == asBlob) == binary.Equals(asString, asBlob),
                    $"'{Escape(s)}' vs {BitConverter.ToString(blob)}");
            }
        }
    }

    [Fact]
    public void BinaryComparer_DivergesOnlyWhereUtf8DoesNotRoundTrip()
    {
        var binary = RedisValue.EqualityComparer.Binary;

        // a lone surrogate encodes to the same bytes as U+FFFD, so Binary calls them equal and the default
        // does not - this is the documented divergence, pinned so it cannot drift silently
        RedisValue loneSurrogate = new string([(char)0xD800]);
        RedisValue replacement = "�";

        Assert.False(loneSurrogate == replacement);
        Assert.True(binary.Equals(loneSurrogate, replacement));

        // and Binary stays self-consistent about it: equal means same hash
        Assert.Equal(binary.GetHashCode(loneSurrogate), binary.GetHashCode(replacement));

        // the other direction, where the *blob* is not canonical UTF8: 0xFF decodes to U+FFFD, so the default
        // calls it equal to that text, while Binary sees FF against EF BF BD and does not
        RedisValue invalidBlob = new byte[] { 0xFF };
        Assert.True(replacement == invalidBlob);
        Assert.False(binary.Equals(replacement, invalidBlob));
    }

    [Fact]
    public void BinaryComparer_EqualValuesShareHashCodes()
    {
        var binary = RedisValue.EqualityComparer.Binary;
        foreach (var blob in EquivalenceBlobs())
        {
            RedisValue asBlob = blob;
            RedisValue asSegmented = blob.Length >= 2 ? Segmented(blob, blob.Length / 2) : asBlob;

            foreach (var s in EquivalenceStrings())
            {
                RedisValue asString = s;
                if (binary.Equals(asString, asBlob))
                {
                    Assert.True(
                        binary.GetHashCode(asString) == binary.GetHashCode(asBlob),
                        $"equal under Binary must share a hash: '{Escape(s)}' vs {BitConverter.ToString(blob)}");
                }
            }

            // representation must not matter: the same bytes, contiguous or segmented
            Assert.True(binary.Equals(asBlob, asSegmented));
            Assert.Equal(binary.GetHashCode(asBlob), binary.GetHashCode(asSegmented));
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("42")]
    public void Comparers_UntypedApiAcceptsWhateverRedisValueAccepts(string value)
    {
        foreach (var comparer in new[] { RedisValue.EqualityComparer.Default, RedisValue.EqualityComparer.Binary })
        {
            var untyped = (IEqualityComparer)comparer;
            object asString = value;
            object asBytes = Encoding.UTF8.GetBytes(value);
            object asRedisValue = (RedisValue)value;

            Assert.True(untyped.Equals(asString, asBytes));
            Assert.True(untyped.Equals(asString, asRedisValue));
            Assert.True(untyped.Equals(asBytes, asRedisValue));

            Assert.Equal(untyped.GetHashCode(asString), untyped.GetHashCode(asBytes));
            Assert.Equal(untyped.GetHashCode(asString), untyped.GetHashCode(asRedisValue));

            // something it cannot read is not equal to anything, but is still reflexive and stable
            object unreadable = new object();
            Assert.False(untyped.Equals(unreadable, asString));
            Assert.True(untyped.Equals(unreadable, unreadable));
            Assert.Equal(unreadable.GetHashCode(), untyped.GetHashCode(unreadable));
        }
    }

    // The chunked encode in Binary works 512 chars at a time, so anything shorter than that never reaches a
    // chunk boundary - and every string in the corpus above is well under it. These put the awkward shapes
    // exactly where the seam falls.
    [Fact]
    public void BinaryComparer_HandlesSurrogatesAtTheChunkBoundary()
    {
        var binary = RedisValue.EqualityComparer.Binary;
        var pair = new string([(char)0xD83D, (char)0xDE00]);   // U+1F600
        var loneHigh = new string([(char)0xD800]);

        foreach (var s in new[]
        {
            // a pair straddling the seam, behind enough three-byte characters that taking one *more* char
            // would also overflow the encode buffer
            new string('你', 511) + pair + "x",

            // a lone high surrogate at the seam, immediately followed by a real pair: extending the chunk
            // would step onto the pair and split that instead
            new string('a', 511) + loneHigh + pair + "x",

            // the seam landing inside a pair from the other side
            new string('a', 510) + pair + pair + "x",

            // and a plain long value, so the multi-chunk path itself is covered
            new string('a', 2000),
        })
        {
            RedisValue asString = s;
            RedisValue asBlob = Encoding.UTF8.GetBytes(s);
            Assert.True(binary.Equals(asString, asBlob), $"length {s.Length}");
            Assert.True(binary.Equals(asBlob, asString), $"length {s.Length} (reversed)");
            Assert.Equal(binary.GetHashCode(asString), binary.GetHashCode(asBlob));
        }
    }

    [Fact]
    public void BinaryComparer_DivergesOnNumericText()
    {
        // RedisValue equality runs Simplify() first, so text that parses to the same number is equal however
        // it was spelled. Binary compares the bytes, so it is not - which is the right answer for a byte
        // reading, and matches how the server identifies members, but it is a divergence worth pinning.
        var binary = RedisValue.EqualityComparer.Binary;
        foreach (var (a, b) in new[] { ("1.0", "1.00"), ("1", "1.0"), ("0", "-0.0"), ("1e2", "100") })
        {
            RedisValue x = a, y = b;
            Assert.True(x == y, $"'{a}' == '{b}' under the default rules");
            Assert.False(binary.Equals(x, y), $"'{a}' vs '{b}' under Binary");
        }

        // identical text still agrees, of course
        Assert.True(binary.Equals((RedisValue)"42", (RedisValue)"42"));
    }

    [Fact]
    public void BinaryComparer_WorksAsADictionaryComparer()
    {
        var dictionary = new Dictionary<RedisValue, string>(RedisValue.EqualityComparer.Binary)
        {
            { "alpha", "one" },
            { Encoding.UTF8.GetBytes("beta"), "two" },
        };

        Assert.Equal("one", dictionary[Encoding.UTF8.GetBytes("alpha")]);
        Assert.Equal("two", dictionary["beta"]);
        Assert.False(dictionary.ContainsKey("gamma"));
    }
}
