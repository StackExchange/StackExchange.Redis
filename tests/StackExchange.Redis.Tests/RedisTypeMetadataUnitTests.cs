using System;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Pure unit tests over the generated <see cref="RedisType"/> token parser; the tokens are the
/// literal replies from <c>TYPE</c>.
/// </summary>
public class RedisTypeMetadataUnitTests
{
    [Theory]
    [InlineData("none", RedisType.None)] // reply for a key that does not exist; see #3156
    [InlineData("string", RedisType.String)]
    [InlineData("list", RedisType.List)]
    [InlineData("set", RedisType.Set)]
    [InlineData("zset", RedisType.SortedSet)]
    [InlineData("hash", RedisType.Hash)]
    [InlineData("stream", RedisType.Stream)]
    [InlineData("vectorset", RedisType.VectorSet)]
    [InlineData("array", RedisType.Array)]
    // parsing is case-insensitive (as it was in v2, which used Enum.TryParse with ignoreCase)
    [InlineData("NONE", RedisType.None)]
    [InlineData("None", RedisType.None)]
    [InlineData("ZSet", RedisType.SortedSet)]
    [InlineData("VECTORSET", RedisType.VectorSet)]
    public void TryParse_KnownTokens(string value, RedisType expected)
    {
        ReadOnlySpan<byte> bytes = Encoding.ASCII.GetBytes(value);
        Assert.True(RedisTypeMetadata.TryParse(bytes, out var actual), $"parse failed for '{value}'");
        Assert.Equal(expected, actual);
    }

    [Theory]
    // Unknown is a client-side value rather than a server token, so it is excluded from the parser
    [InlineData("unknown")]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("blah")]
    [InlineData("nonex")]
    public void TryParse_RejectsNonTokens(string value)
    {
        ReadOnlySpan<byte> bytes = Encoding.ASCII.GetBytes(value);
        Assert.False(RedisTypeMetadata.TryParse(bytes, out _), $"parse unexpectedly succeeded for '{value}'");
    }
}
