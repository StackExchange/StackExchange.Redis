using System;
using System.Buffers;
using System.Linq;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Validates the inline "short blob" storage kind (<see cref="RedisValue.StorageType.ShortBlob"/>), which
/// packs 1..8 payload bytes into the overlapped int64 field instead of allocating a byte[]. Every projection
/// must be indistinguishable from the equivalent <c>byte[]</c>-backed value.
/// </summary>
public class RedisValueShortBlobTests
{
    private static RedisValue Short(byte[] bytes) => RedisValue.FromRaw(bytes); // <= 8 bytes => ShortBlob

    private static RedisValue Sequence(byte[] bytes) // multi-segment, one byte per chunk
        => FragmentedSegment<byte>.Create(bytes.Select((_, i) => new ReadOnlyMemory<byte>(bytes, i, 1)).ToArray());

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("OK")]
    [InlineData("hello")]
    [InlineData("12345678")] // 8 bytes, the max inline size
    [InlineData("1234")] // numeric-looking
    [InlineData("-42")]
    [InlineData("0.5")]
    [InlineData("inf")] // special token (not simplified to a double)
    [InlineData("a1b2c3")] // mixed alphanumeric
    public void ShortBlob_IsIndistinguishableFromByteArray(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.True(bytes.Length is >= 1 and <= 8);

        var shortBlob = Short(bytes);
        RedisValue byteArray = (byte[])bytes.Clone();

        Assert.Equal(RedisValue.StorageType.ShortBlob, shortBlob.Type);
        Assert.Equal(RedisValue.StorageType.ByteArray, byteArray.Type);

        // length / projections
        Assert.Equal(byteArray.Length(), shortBlob.Length());
        Assert.Equal((string?)byteArray, (string?)shortBlob);
        Assert.Equal((byte[]?)byteArray, (byte[]?)shortBlob);

        // equality + hash + compare, both directions, against the byte[] form
        Assert.True(shortBlob == byteArray);
        Assert.True(byteArray == shortBlob);
        Assert.True(shortBlob.Equals(byteArray));
        Assert.Equal(byteArray.GetHashCode(), shortBlob.GetHashCode());
        Assert.Equal(0, shortBlob.CompareTo(byteArray));

        // also equal to the equivalent multi-segment sequence (cross-kind)
        var sequence = Sequence(bytes);
        Assert.True(shortBlob == sequence);
        Assert.Equal(shortBlob.GetHashCode(), sequence.GetHashCode());
        Assert.Equal(0, shortBlob.CompareTo(sequence));

        // CopyTo round-trips
        Span<byte> copy = stackalloc byte[shortBlob.GetByteCount()];
        Assert.Equal(bytes.Length, shortBlob.CopyTo(copy));
        Assert.True(copy.SequenceEqual(bytes));
    }

    [Fact]
    public void ShortBlob_NumericContent_EqualsAndParsesLikeInteger()
    {
        var shortBlob = Short(Encoding.UTF8.GetBytes("1234"));
        Assert.Equal(RedisValue.StorageType.ShortBlob, shortBlob.Type);

        Assert.True(shortBlob == 1234);
        Assert.True(1234 == shortBlob);
        Assert.Equal(((RedisValue)1234).GetHashCode(), shortBlob.GetHashCode()); // numeric-consistent hash

        Assert.True(shortBlob.TryParse(out long l));
        Assert.Equal(1234, l);
        Assert.Equal(1234, (int)shortBlob);
    }

    [Theory]
    [InlineData("inf", double.PositiveInfinity)]
    [InlineData("+inf", double.PositiveInfinity)]
    [InlineData("-inf", double.NegativeInfinity)]
    [InlineData("Inf", double.PositiveInfinity)] // case-insensitive
    [InlineData("nan", double.NaN)]
    public void ShortBlob_SpecialDouble_ParsesLikeByteArray(string text, double expected)
    {
        // regression: inf/nan are deliberately NOT folded by Simplify() (they'd break equality semantics),
        // so they stay blob-backed; a <= 8 byte payload is a ShortBlob. The (double) cast must parse it
        // exactly like the byte[] form. Previously the cast had no ShortBlob arm and threw for these.
        var bytes = Encoding.UTF8.GetBytes(text);
        var shortBlob = Short(bytes);
        RedisValue byteArray = (byte[])bytes.Clone();
        Assert.Equal(RedisValue.StorageType.ShortBlob, shortBlob.Type);
        Assert.Equal(RedisValue.StorageType.ByteArray, byteArray.Type);

        Assert.Equal(expected, (double)shortBlob);
        Assert.Equal((double)byteArray, (double)shortBlob); // indistinguishable from the byte[] form
    }

    [Fact]
    public void ShortBlob_StartsWith_MatchesByteArray()
    {
        var shortBlob = Short(Encoding.UTF8.GetBytes("abcde"));
        Assert.Equal(RedisValue.StorageType.ShortBlob, shortBlob.Type);

        Assert.True(shortBlob.StartsWith("abc"u8.ToArray()));
        Assert.True(shortBlob.StartsWith(Short("ab"u8.ToArray()))); // ShortBlob vs ShortBlob
        Assert.True(shortBlob.StartsWith(Sequence("abc"u8.ToArray()))); // ShortBlob vs Sequence
        Assert.False(shortBlob.StartsWith("abd"u8.ToArray()));
        Assert.False(shortBlob.StartsWith("abcdef"u8.ToArray()));
    }

    [Fact]
    public void FromRaw_RoutesByLength()
    {
        Assert.Equal(RedisValue.StorageType.String, Short(Array.Empty<byte>()).Type); // empty => EmptyString
        Assert.Equal(RedisValue.StorageType.ShortBlob, Short(new byte[8]).Type); // 8 => inline
        Assert.Equal(RedisValue.StorageType.ByteArray, Short(new byte[9]).Type); // 9 => allocate
    }

    [Fact]
    public void ShortBlob_NonTextBytes_RoundTripAndEqualByteArray()
    {
        // arbitrary non-UTF8 bytes including zero and high bytes, exactly 8 long (max inline)
        var bytes = new byte[] { 0x00, 0x01, 0xFF, 0x80, 0x7F, 0xAB, 0x00, 0xCD };
        var shortBlob = Short(bytes);
        RedisValue byteArray = (byte[])bytes.Clone();

        Assert.Equal(RedisValue.StorageType.ShortBlob, shortBlob.Type);
        Assert.True(shortBlob == byteArray);
        Assert.Equal(byteArray.GetHashCode(), shortBlob.GetHashCode());
        Assert.Equal(bytes, (byte[]?)shortBlob);
        Assert.Equal(0, shortBlob.CompareTo(byteArray));
    }

    [Fact]
    public void ShortBlob_WriteBulkString_MatchesByteArray()
    {
        var bytes = Encoding.UTF8.GetBytes("hello"); // 5 bytes => ShortBlob
        var shortBlob = Short(bytes);
        RedisValue byteArray = (byte[])bytes.Clone();
        Assert.Equal(RedisValue.StorageType.ShortBlob, shortBlob.Type);

        var fromShortBlob = new ArrayBufferWriter<byte>();
        var fromByteArray = new ArrayBufferWriter<byte>();
        MessageWriter.WriteBulkString(shortBlob, fromShortBlob);
        MessageWriter.WriteBulkString(byteArray, fromByteArray);

        // the wire bytes must be identical, and a well-formed RESP bulk string
        Assert.Equal(fromByteArray.WrittenSpan.ToArray(), fromShortBlob.WrittenSpan.ToArray());
        Assert.Equal(Encoding.ASCII.GetBytes("$5\r\nhello\r\n"), fromShortBlob.WrittenSpan.ToArray());
    }

    [Fact]
    public void ShortBlob_NotNullOrEmpty()
    {
        var shortBlob = Short(new byte[] { 0 }); // a single zero byte
        Assert.Equal(RedisValue.StorageType.ShortBlob, shortBlob.Type);
        Assert.False(shortBlob.IsNull);
        Assert.False(shortBlob.IsNullOrEmpty);
        Assert.True(shortBlob.HasValue);
    }
}
