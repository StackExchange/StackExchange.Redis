using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class TrackSubscriptions(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData("*3\r\n$9\r\nsubscribe\r\n$7\r\nchannel\r\n:1\r\n", 1)] // SUBSCRIBE response with count 1
    [InlineData("*3\r\n$9\r\nsubscribe\r\n$7\r\nchannel\r\n:5\r\n", 5)] // SUBSCRIBE response with count 5
    [InlineData("*3\r\n$11\r\nunsubscribe\r\n$7\r\nchannel\r\n:0\r\n", 0)] // UNSUBSCRIBE response with count 0
    [InlineData("*3\r\n$10\r\npsubscribe\r\n$8\r\npattern*\r\n:2\r\n", 2)] // PSUBSCRIBE response with count 2
    public void TrackSubscriptions_Success(string resp, int expectedCount)
    {
        var processor = ResultProcessor.TrackSubscriptions;
        var result = Execute(resp, processor);
        Assert.True(result);
        Log($"Successfully parsed subscription response with count {expectedCount}");
    }
}
