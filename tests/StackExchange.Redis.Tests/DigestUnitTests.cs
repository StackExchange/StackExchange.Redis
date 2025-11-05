using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

#pragma warning disable SER002 // 8.4

public class DigestUnitTests(ITestOutputHelper output) : TestBase(output)
{
    [Theory]
    [MemberData(nameof(SimpleDigestTestValues))]
    public void RedisValue_Digest(string equivalentValue, RedisValue value)
    {
        // first, use pure XxHash3 to see what we expect
        var hashHex = GetXxh3Hex(equivalentValue);

        var digest = value.Digest();
        Assert.Equal(ValueCondition.ConditionKind.DigestEquals, digest.Kind);

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
    [InlineData("", "2d06800538d394c2")]
    [InlineData("a", "e6c632b61e964e1f")]
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
        Assert.True(condition.IsValueTest);
        Assert.False(condition.IsDigestTest);
        Assert.False(condition.IsNegated);
        Assert.False(condition.IsExistenceTest);

        var negCondition = !condition;
        Assert.NotEqual(condition, negCondition);
        Assert.Equal($"IFNE {InputValue}", negCondition.ToString());
        Assert.True(negCondition.IsValueTest);
        Assert.False(negCondition.IsDigestTest);
        Assert.True(negCondition.IsNegated);
        Assert.False(negCondition.IsExistenceTest);

        var negNegCondition = !negCondition;
        Assert.Equal(condition, negNegCondition);

        var digest = condition.AsDigest();
        Assert.NotEqual(condition, digest);
        Assert.Equal($"IFDEQ {GetXxh3Hex(InputValue)}", digest.ToString());
        Assert.False(digest.IsValueTest);
        Assert.True(digest.IsDigestTest);
        Assert.False(digest.IsNegated);
        Assert.False(digest.IsExistenceTest);

        var negDigest = !digest;
        Assert.NotEqual(digest, negDigest);
        Assert.Equal($"IFDNE {GetXxh3Hex(InputValue)}", negDigest.ToString());
        Assert.False(negDigest.IsValueTest);
        Assert.True(negDigest.IsDigestTest);
        Assert.True(negDigest.IsNegated);
        Assert.False(negDigest.IsExistenceTest);

        var negNegDigest = !negDigest;
        Assert.Equal(digest, negNegDigest);

        var @default = default(ValueCondition);
        Assert.False(@default.IsValueTest);
        Assert.False(@default.IsDigestTest);
        Assert.False(@default.IsNegated);
        Assert.False(@default.IsExistenceTest);
        Assert.Equal("", @default.ToString());
        Assert.Equal(ValueCondition.Always, @default);

        var ex = Assert.Throws<InvalidOperationException>(() => !@default);
        Assert.Equal("operator ! cannot be used with a Always condition.", ex.Message);

        var exists = ValueCondition.Exists;
        Assert.False(exists.IsValueTest);
        Assert.False(exists.IsDigestTest);
        Assert.False(exists.IsNegated);
        Assert.True(exists.IsExistenceTest);
        Assert.Equal("XX", exists.ToString());

        var notExists = ValueCondition.NotExists;
        Assert.False(notExists.IsValueTest);
        Assert.False(notExists.IsDigestTest);
        Assert.True(notExists.IsNegated);
        Assert.True(notExists.IsExistenceTest);
        Assert.Equal("NX", notExists.ToString());

        Assert.NotEqual(exists, notExists);
        Assert.Equal(exists, !notExists);
        Assert.Equal(notExists, !exists);
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
