using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class DigestUnitTests(ITestOutputHelper output) : TestBase(output)
{
    [Theory]
    [MemberData(nameof(SimpleDigestTestValues))]
    public void RedisValue_Digest(string equivalentValue, RedisValue value)
    {
        // first, use pure XxHash3 to see what we expect
        var hashHex = GetXxh3Hex(equivalentValue);

        var digest = value.Digest();
        Assert.True(digest.HasValue);
        Assert.True(digest.IsDigest);
        Assert.True(digest.IsEqual);

        Assert.Equal($"IFDEQ {hashHex}", digest.ToString());
    }

    public static IEnumerable<object[]> SimpleDigestTestValues()
    {
        yield return ["Hello World", (RedisValue)"Hello World"];
        yield return ["42", (RedisValue)"42"];
        yield return ["42", (RedisValue)42];
    }

    [Theory]
    [InlineData("Hello World", "e34615aade2e6333")]
    [InlineData("42", "1217cb28c0ef2191")]
    public void ValueCondition_CalculateDigest(string source, string expected)
    {
        var digest = ValueCondition.CalculateDigest(Encoding.UTF8.GetBytes(source));
        Assert.Equal($"IFDEQ {expected}", digest.ToString());
    }

    [Theory]
    [InlineData("e34615aade2e6333")]
    [InlineData("1217cb28c0ef2191")]
    public void ValueCondition_ParseDigest(string value)
    {
        // parse from hex chars
        var digest = ValueCondition.ParseDigest(value.AsSpan());
        Assert.Equal($"IFDEQ {value}", digest.ToString());

        // and the same, from hex bytes
        digest = ValueCondition.ParseDigest(Encoding.UTF8.GetBytes(value).AsSpan());
        Assert.Equal($"IFDEQ {value}", digest.ToString());
    }

    [Theory]
    [InlineData("Hello World", "e34615aade2e6333")]
    [InlineData("42", "1217cb28c0ef2191")]
    public void KnownXxh3Values(string source, string expected)
        => Assert.Equal(expected, GetXxh3Hex(source));

    private static string GetXxh3Hex(string source)
    {
        var len = Encoding.UTF8.GetMaxByteCount(source.Length);
        var oversized = ArrayPool<byte>.Shared.Rent(len);
        #if NET
        var bytes = Encoding.UTF8.GetBytes(source, oversized);
        #else
        int bytes;
        unsafe
        {
            fixed (byte* bPtr = oversized)
            {
                fixed (char* cPtr = source)
                {
                    bytes = Encoding.UTF8.GetBytes(cPtr, source.Length, bPtr, len);
                }
            }
        }
        #endif
        var result = GetXxh3Hex(oversized.AsSpan(0, bytes));
        ArrayPool<byte>.Shared.Return(oversized);
        return result;
    }

    private static string GetXxh3Hex(ReadOnlySpan<byte> source)
    {
        byte[] targetBytes = new byte[8];
        XxHash3.Hash(source, targetBytes);
        return BitConverter.ToString(targetBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    [Fact]
    public void ValueCondition_Mutations()
    {
        const string InputValue =
            "Meantime we shall express our darker purpose.\nGive me the map there. Know we have divided\nIn three our kingdom; and 'tis our fast intent\nTo shake all cares and business from our age,\nConferring them on younger strengths while we\nUnburthen'd crawl toward death. Our son of Cornwall,\nAnd you, our no less loving son of Albany,\nWe have this hour a constant will to publish\nOur daughters' several dowers, that future strife\nMay be prevented now. The princes, France and Burgundy,\nGreat rivals in our youngest daughter's love,\nLong in our court have made their amorous sojourn,\nAnd here are to be answer'd.";

        var condition = ValueCondition.Equal(InputValue);
        Assert.Equal($"IFEQ {InputValue}", condition.ToString());
        Assert.True(condition.HasValue);
        Assert.False(condition.IsDigest);
        Assert.True(condition.IsEqual);

        var negCondition = !condition;
        Assert.NotEqual(condition, negCondition);
        Assert.Equal($"IFNE {InputValue}", negCondition.ToString());
        Assert.True(negCondition.HasValue);
        Assert.False(negCondition.IsDigest);
        Assert.False(negCondition.IsEqual);

        var negNegCondition = !negCondition;
        Assert.Equal(condition, negNegCondition);

        var digest = condition.Digest();
        Assert.NotEqual(condition, digest);
        Assert.Equal($"IFDEQ {GetXxh3Hex(InputValue)}", digest.ToString());
        Assert.True(digest.HasValue);
        Assert.True(digest.IsDigest);
        Assert.True(digest.IsEqual);

        var negDigest = !digest;
        Assert.NotEqual(digest, negDigest);
        Assert.Equal($"IFDNE {GetXxh3Hex(InputValue)}", negDigest.ToString());
        Assert.True(negDigest.HasValue);
        Assert.True(negDigest.IsDigest);
        Assert.False(negDigest.IsEqual);

        var negNegDigest = !negDigest;
        Assert.Equal(digest, negNegDigest);

        var @default = default(ValueCondition);
        Assert.False(@default.HasValue);
        Assert.False(@default.IsDigest);
        Assert.False(@default.IsEqual);
        Assert.Equal("", @default.ToString());
    }

    [Fact]
    public void RandomBytes()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8000);
        var rand = new Random();

        for (int i = 0; i < 100; i++)
        {
            var len = rand.Next(1, buffer.Length);
            var span = buffer.AsSpan(0, len);
#if NET
            rand.NextBytes(span);
#else
            rand.NextBytes(buffer);
#endif
            var digest = ValueCondition.CalculateDigest(span);
            Assert.Equal($"IFDEQ {GetXxh3Hex(span)}", digest.ToString());
        }
    }
}
