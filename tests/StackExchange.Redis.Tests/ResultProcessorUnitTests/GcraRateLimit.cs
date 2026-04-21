using Xunit;

/* unavailable until v3
namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for GCRA rate limit result processor.
/// </summary>
public class GcraRateLimit(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void GcraRateLimit_NotLimited_Success()
    {
        // GCRA response when token acquisition is allowed:
        // 1) 0 (not limited)
        // 2) 11 (max tokens = max_burst + 1)
        // 3) 10 (available tokens)
        // 4) -1 (retry after - always -1 when not limited)
        // 5) 5 (full burst after seconds)
        var resp = "*5\r\n:0\r\n:11\r\n:10\r\n:-1\r\n:5\r\n";
        var processor = ResultProcessor.GcraRateLimit;
        var result = Execute(resp, processor);

        Assert.False(result.Limited);
        Assert.Equal(11, result.MaxTokens);
        Assert.Equal(10, result.AvailableTokens);
        Assert.Equal(-1, result.RetryAfterSeconds);
        Assert.Equal(5, result.FullBurstAfterSeconds);
    }

    [Fact]
    public void GcraRateLimit_Limited_Success()
    {
        // GCRA response when request is rate limited:
        // 1) 1 (limited)
        // 2) 11 (max tokens = max_burst + 1)
        // 3) 0 (no available tokens)
        // 4) 2 (retry after 2 seconds)
        // 5) 10 (full burst after 10 seconds)
        var resp = "*5\r\n:1\r\n:11\r\n:0\r\n:2\r\n:10\r\n";
        var processor = ResultProcessor.GcraRateLimit;
        var result = Execute(resp, processor);

        Assert.True(result.Limited);
        Assert.Equal(11, result.MaxTokens);
        Assert.Equal(0, result.AvailableTokens);
        Assert.Equal(2, result.RetryAfterSeconds);
        Assert.Equal(10, result.FullBurstAfterSeconds);
    }

    [Fact]
    public void GcraRateLimit_PartiallyAvailable_Success()
    {
        // GCRA response when some tokens are available:
        // 1) 0 (not limited)
        // 2) 101 (max tokens)
        // 3) 50 (50 tokens available)
        // 4) -1 (retry after - not limited)
        // 5) 100 (full burst after 100 seconds)
        var resp = "*5\r\n:0\r\n:101\r\n:50\r\n:-1\r\n:100\r\n";
        var processor = ResultProcessor.GcraRateLimit;
        var result = Execute(resp, processor);

        Assert.False(result.Limited);
        Assert.Equal(101, result.MaxTokens);
        Assert.Equal(50, result.AvailableTokens);
        Assert.Equal(-1, result.RetryAfterSeconds);
        Assert.Equal(100, result.FullBurstAfterSeconds);
    }

    [Theory]
    [InlineData("*4\r\n:0\r\n:11\r\n:10\r\n:-1\r\n")] // only 4 elements
    [InlineData(":0\r\n")] // scalar instead of array
    [InlineData("*5\r\n$1\r\n0\r\n:11\r\n:10\r\n:-1\r\n:5\r\n")] // first element is string
    public void GcraRateLimit_InvalidResponse_Failure(string resp)
    {
        ExecuteUnexpected(resp, ResultProcessor.GcraRateLimit);
    }
}
*/
