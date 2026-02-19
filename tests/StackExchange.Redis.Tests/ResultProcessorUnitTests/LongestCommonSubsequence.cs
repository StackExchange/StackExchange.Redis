using System.Linq;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class LongestCommonSubsequence(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void SingleMatch_Success()
    {
        // LCS key1 key2 IDX MINMATCHLEN 4 WITHMATCHLEN
        // 1) "matches"
        // 2) 1) 1) 1) (integer) 4
        //          2) (integer) 7
        //       2) 1) (integer) 5
        //          2) (integer) 8
        //       3) (integer) 4
        // 3) "len"
        // 4) (integer) 6
        var resp = "*4\r\n$7\r\nmatches\r\n*1\r\n*3\r\n*2\r\n:4\r\n:7\r\n*2\r\n:5\r\n:8\r\n:4\r\n$3\r\nlen\r\n:6\r\n";
        var result = Execute(resp, ResultProcessor.LCSMatchResult);

        Assert.Equal(6, result.LongestMatchLength);
        Assert.Single(result.Matches);
        Assert.Equal(4, result.Matches[0].FirstStringIndex);
        Assert.Equal(5, result.Matches[0].SecondStringIndex);
        Assert.Equal(4, result.Matches[0].Length);
    }

    [Fact]
    public void TwoMatches_Success()
    {
        // LCS key1 key2 IDX MINMATCHLEN 0 WITHMATCHLEN
        // 1) "matches"
        // 2) 1) 1) 1) (integer) 4
        //          2) (integer) 7
        //       2) 1) (integer) 5
        //          2) (integer) 8
        //       3) (integer) 4
        //    2) 1) 1) (integer) 2
        //          2) (integer) 3
        //       2) 1) (integer) 0
        //          2) (integer) 1
        //       3) (integer) 2
        // 3) "len"
        // 4) (integer) 6
        var resp = "*4\r\n$7\r\nmatches\r\n*2\r\n*3\r\n*2\r\n:4\r\n:7\r\n*2\r\n:5\r\n:8\r\n:4\r\n*3\r\n*2\r\n:2\r\n:3\r\n*2\r\n:0\r\n:1\r\n:2\r\n$3\r\nlen\r\n:6\r\n";
        var result = Execute(resp, ResultProcessor.LCSMatchResult);

        Assert.Equal(6, result.LongestMatchLength);
        Assert.Equal(2, result.Matches.Length);
        Assert.Equal(4, result.Matches[0].FirstStringIndex);
        Assert.Equal(5, result.Matches[0].SecondStringIndex);
        Assert.Equal(4, result.Matches[0].Length);
        Assert.Equal(2, result.Matches[1].FirstStringIndex);
        Assert.Equal(0, result.Matches[1].SecondStringIndex);
        Assert.Equal(2, result.Matches[1].Length);
    }

    [Fact]
    public void NoMatches_Success()
    {
        // LCS key1 key2 IDX
        // 1) "matches"
        // 2) (empty array)
        // 3) "len"
        // 4) (integer) 0
        var resp = "*4\r\n$7\r\nmatches\r\n*0\r\n$3\r\nlen\r\n:0\r\n";
        var result = Execute(resp, ResultProcessor.LCSMatchResult);

        Assert.Equal(0, result.LongestMatchLength);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void NotArray_Failure()
    {
        var resp = "+OK\r\n";
        ExecuteUnexpected(resp, ResultProcessor.LCSMatchResult);
    }
}
