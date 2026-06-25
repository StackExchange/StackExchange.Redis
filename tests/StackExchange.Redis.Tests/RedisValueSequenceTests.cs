using System;
using System.Buffers;
using System.Linq;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for <see cref="RedisValue"/> backed by a multi-segment <see cref="ReadOnlySequence{T}"/>
/// (<see cref="RedisValue.StorageType.Sequence"/>), focusing on text handling where a multi-byte UTF-8
/// glyph can straddle a segment boundary.
/// </summary>
public class RedisValueSequenceTests
{
    [Theory]
    [InlineData("")] // empty
    [InlineData("hello")] // ASCII only
    [InlineData("héllo")] // 2-byte glyph (é)
    [InlineData("€100")] // 3-byte glyph (€)
    [InlineData("a\U0001F389b")] // 4-byte glyph / surrogate pair (🎉)
    [InlineData("é€\U0001F389")] // adjacent multi-byte glyphs of differing widths
    [InlineData("héllo € wörld \U0001F389 mixed")] // mixed widths
    public void MultiSegmentUtf8_DecodesAcrossBoundaries(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        // split at *every* byte so multi-byte glyphs are guaranteed to straddle segments
        RedisValue value = SplitEveryByte(bytes);

        // empty collapses to the empty string; anything else stays a genuine multi-segment sequence
        if (bytes.Length == 0)
        {
            Assert.Equal(RedisValue.StorageType.String, value.Type);
        }
        else
        {
            Assert.Equal(RedisValue.StorageType.Sequence, value.Type);
        }

        // byte length is unaffected by where we slice
        Assert.Equal(bytes.Length, value.GetByteCount());

        // the bug under test: a naive per-segment char count over-counts split glyphs; this must match
        // the contiguous count
        Assert.Equal(text.Length, value.GetCharCount());

        // GetMaxCharCount must remain a safe upper bound
        Assert.True(value.GetMaxCharCount() >= text.Length);

        // text round-trips via ToString / the string operator (Format.GetString linearizes first)
        Assert.Equal(text, value.ToString());
        Assert.Equal(text, (string?)value);

        // ...and via the char-span copy (GetChars over the sequence), sized from GetCharCount
        var dest = new char[value.GetCharCount()];
        int written = value.CopyTo(dest.AsSpan());
        Assert.Equal(text.Length, written);
        Assert.Equal(text, new string(dest, 0, written));
    }

    [Fact]
    public void LargeMultiSegmentUtf8_UsesStreamingDecoderPath()
    {
        // exceed the helper's stack-linearize threshold so the streaming Decoder path is exercised, with
        // every byte in its own segment so multi-byte glyphs straddle boundaries throughout
        var text = string.Concat(Enumerable.Repeat("héllo-€-\U0001F389-", 20));
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.True(bytes.Length > 128, $"expected a payload over the linearize threshold, got {bytes.Length}");

        RedisValue value = SplitEveryByte(bytes);
        Assert.Equal(RedisValue.StorageType.Sequence, value.Type);
        Assert.Equal(text.Length, value.GetCharCount());
        Assert.Equal(text, value.ToString());

        var dest = new char[value.GetCharCount()];
        int written = value.CopyTo(dest.AsSpan());
        Assert.Equal(text.Length, written);
        Assert.Equal(text, new string(dest, 0, written));
    }

    [Theory]
    [InlineData("10")] // numeric: simplifies to an integer, so all forms hash as Int64
    [InlineData("10.5")] // numeric: simplifies to a double
    [InlineData("hello")] // plain text: compared/hashed as a string
    [InlineData("inf")] // special-case text that deliberately does NOT simplify to a double
    [InlineData("nan")]
    public void EqualValues_HashIdentically_AcrossAllStorageForms(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        RedisValue asString = text;
        RedisValue asBytes = bytes; // single-buffer (ByteArray)
        RedisValue asSequence = SplitEveryByte(bytes); // multi-buffer (Sequence)

        Assert.Equal(RedisValue.StorageType.String, asString.Type);
        Assert.Equal(RedisValue.StorageType.ByteArray, asBytes.Type);
        Assert.Equal(RedisValue.StorageType.Sequence, asSequence.Type);

        // all forms are equal to one another...
        Assert.True(asString == asBytes, "string == bytes");
        Assert.True(asString == asSequence, "string == sequence");
        Assert.True(asBytes == asSequence, "bytes == sequence");

        // ...so the equality/hash contract demands identical hash codes
        int expected = asString.GetHashCode();
        Assert.Equal(expected, asBytes.GetHashCode());
        Assert.Equal(expected, asSequence.GetHashCode());
    }

    [Fact]
    public void IntegerAndTextForms_HashIdentically()
    {
        // the canonical example: 10, "10", and its bytes (single- and multi-buffer) all hash the same
        RedisValue asInt = 10;
        RedisValue asString = "10";
        RedisValue asBytes = new byte[] { (byte)'1', (byte)'0' };
        RedisValue asSequence = SplitEveryByte(new byte[] { (byte)'1', (byte)'0' });

        Assert.Equal(RedisValue.StorageType.Sequence, asSequence.Type);

        int expected = asInt.GetHashCode();
        Assert.Equal(expected, asString.GetHashCode());
        Assert.Equal(expected, asBytes.GetHashCode());
        Assert.Equal(expected, asSequence.GetHashCode());
    }

    [Fact]
    public void MultiSegmentBytes_RoundTripToArray()
    {
        var bytes = Encoding.UTF8.GetBytes("the quick brown fox");
        RedisValue value = SplitEveryByte(bytes);

        Assert.Equal(RedisValue.StorageType.Sequence, value.Type);
        Assert.Equal(bytes, (byte[]?)value);
    }

    private static RedisValue SplitEveryByte(byte[] bytes)
    {
        var chunks = new ReadOnlyMemory<byte>[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chunks[i] = new ReadOnlyMemory<byte>(bytes, i, 1);
        }
        return FragmentedSegment<byte>.Create(chunks);
    }
}
