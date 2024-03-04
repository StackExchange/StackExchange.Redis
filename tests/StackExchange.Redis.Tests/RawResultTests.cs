using System.Buffers;
using Xunit;

namespace StackExchange.Redis.Tests;

public class RawResultTests
{
    [Fact]
    public void TypeLoads()
    {
        var type = typeof(RawResult);
        Assert.Equal(nameof(RawResult), type.Name);
    }

    [Theory]
    [InlineData(ResultType.BulkString)]
    [InlineData(ResultType.Null)]
    public void NullWorks(ResultType type)
    {
        var result = new RawResult(type, ReadOnlySequence<byte>.Empty, RawResult.ResultFlags.None);
        Assert.Equal(type, result.Resp3Type);
        Assert.True(result.HasValue);
        Assert.True(result.IsNull);

        var value = result.AsRedisValue();

        Assert.True(value.IsNull);
        string? s = value;
        Assert.Null(s);

        byte[]? arr = (byte[]?)value;
        Assert.Null(arr);
    }

    [Fact]
    public void DefaultWorks()
    {
        var result = RawResult.Nil;
        Assert.Equal(ResultType.None, result.Resp3Type);
        Assert.False(result.HasValue);
        Assert.True(result.IsNull);

        var value = result.AsRedisValue();

        Assert.True(value.IsNull);
        var s = (string?)value;
        Assert.Null(s);

        var arr = (byte[]?)value;
        Assert.Null(arr);
    }

    [Fact]
    public void NilWorks()
    {
        var result = RawResult.Nil;
        Assert.Equal(ResultType.None, result.Resp3Type);
        Assert.True(result.IsNull);

        var value = result.AsRedisValue();

        Assert.True(value.IsNull);
        var s = (string?)value;
        Assert.Null(s);

        var arr = (byte[]?)value;
        Assert.Null(arr);
    }
}
