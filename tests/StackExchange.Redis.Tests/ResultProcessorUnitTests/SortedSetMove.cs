using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// <c>ZMOVE</c> / <c>ZMOVEMEMBER</c> reply with an array of <c>[member, score]</c> pairs - nested in RESP2 as well as
/// RESP3, unlike <c>ZPOPMIN</c> - or the null reply when nothing moved.
/// </summary>
public class SortedSetMove(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void Pairs_Resp2_ScoresAreBulkStrings()
    {
        var resp = "*2\r\n*2\r\n$1\r\na\r\n$1\r\n1\r\n*2\r\n$1\r\nb\r\n$3\r\n2.5\r\n";

        var result = Execute(resp, ResultProcessor.NullableSortedSetEntryArray);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("a", result[0].Element);
        Assert.Equal(1, result[0].Score);
        Assert.Equal("b", result[1].Element);
        Assert.Equal(2.5, result[1].Score);
    }

    [Fact]
    public void Pairs_Resp3_ScoresAreDoubles()
    {
        var resp = "*2\r\n*2\r\n$1\r\na\r\n,1\r\n*2\r\n$1\r\nb\r\n,2.5\r\n";

        var result = Execute(resp, ResultProcessor.NullableSortedSetEntryArray, protocol: RedisProtocol.Resp3);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("a", result[0].Element);
        Assert.Equal(1, result[0].Score);
        Assert.Equal("b", result[1].Element);
        Assert.Equal(2.5, result[1].Score);
    }

    [Fact]
    public void SinglePair_StaysNested()
    {
        // one moved member is still an array of one pair; in particular the RESP2 form must not be read as the
        // flat [member, score] list that ZPOPMIN sends
        var resp = "*1\r\n*2\r\n$1\r\na\r\n$1\r\n1\r\n";

        var result = Execute(resp, ResultProcessor.NullableSortedSetEntryArray);

        var entry = Assert.Single(result!);
        Assert.Equal("a", entry.Element);
        Assert.Equal(1, entry.Score);
    }

    [Theory]
    [InlineData("*2\r\n*2\r\n$1\r\na\r\n$4\r\n-inf\r\n*2\r\n$1\r\nb\r\n$3\r\ninf\r\n", RedisProtocol.Resp2)]
    [InlineData("*2\r\n*2\r\n$1\r\na\r\n,-inf\r\n*2\r\n$1\r\nb\r\n,inf\r\n", RedisProtocol.Resp3)]
    public void InfiniteScores(string resp, RedisProtocol protocol)
    {
        var result = Execute(resp, ResultProcessor.NullableSortedSetEntryArray, protocol: protocol);

        Assert.NotNull(result);
        Assert.Equal(double.NegativeInfinity, result[0].Score);
        Assert.Equal(double.PositiveInfinity, result[1].Score);
    }

    [Fact]
    public void OrderIsPreserved()
    {
        // MAX replies highest-first; the order is part of the reply
        var resp = "*3\r\n*2\r\n$1\r\nc\r\n$1\r\n3\r\n*2\r\n$1\r\nb\r\n$1\r\n2\r\n*2\r\n$1\r\na\r\n$1\r\n1\r\n";

        var result = Execute(resp, ResultProcessor.NullableSortedSetEntryArray);

        Assert.Equal("c,b,a", Join(System.Array.ConvertAll(result!, static x => x.Element)));
    }

    [Fact]
    public void NullArray_Resp2_IsNull()
        => Assert.Null(Execute("*-1\r\n", ResultProcessor.NullableSortedSetEntryArray));

    [Fact]
    public void Null_Resp3_IsNull()
        => Assert.Null(Execute("_\r\n", ResultProcessor.NullableSortedSetEntryArray, protocol: RedisProtocol.Resp3));

    [Fact]
    public void EmptyArray_IsEmptyNotNull()
    {
        // the server never sends this (nothing moved is the null reply), but it must not be folded into null
        var result = Execute("*0\r\n", ResultProcessor.NullableSortedSetEntryArray);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("$5\r\nhello\r\n")] // scalar
    [InlineData(":5\r\n")] // integer
    [InlineData("*2\r\n$1\r\na\r\n$1\r\n1\r\n")] // flat (ZPOPMIN-style) list, not pairs
    [InlineData("*1\r\n*3\r\n$1\r\na\r\n$1\r\n1\r\n$1\r\nx\r\n")] // a "pair" of three
    [InlineData("*1\r\n*1\r\n$1\r\na\r\n")] // a "pair" of one
    [InlineData("*1\r\n*2\r\n$1\r\na\r\n*0\r\n")] // score is not a scalar
    public void Malformed_Failure(string resp) => ExecuteUnexpected(resp, ResultProcessor.NullableSortedSetEntryArray);
}
