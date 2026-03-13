using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class GcraRateLimitRoundTrip
{
    [Theory(Timeout = 1000)]
    [InlineData("mykey", 10, 100, 1.0, "*5\r\n$4\r\nGCRA\r\n$5\r\nmykey\r\n$2\r\n10\r\n$3\r\n100\r\n$1\r\n1\r\n", "*5\r\n:0\r\n:11\r\n:10\r\n:-1\r\n:5\r\n")]
    public async Task GcraRateLimit_DefaultCount_RoundTrip(
        string key,
        int maxBurst,
        int requestsPerPeriod,
        double periodSeconds,
        string requestResp,
        string responseResp)
    {
        var msg = new RedisDatabase.GcraMessage(0, CommandFlags.None, key, maxBurst, requestsPerPeriod, periodSeconds, 1);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.GcraRateLimit, requestResp, responseResp);

        Assert.False(result.Limited);
        Assert.Equal(11, result.MaxRequests);
        Assert.Equal(10, result.AvailableRequests);
        Assert.Equal(-1, result.RetryAfterSeconds);
        Assert.Equal(5, result.FullBurstAfterSeconds);
    }

    [Theory(Timeout = 1000)]
    [InlineData("mykey", 10, 100, 1.0, 5, "*7\r\n$4\r\nGCRA\r\n$5\r\nmykey\r\n$2\r\n10\r\n$3\r\n100\r\n$1\r\n1\r\n$12\r\nNUM_REQUESTS\r\n$1\r\n5\r\n", "*5\r\n:1\r\n:11\r\n:0\r\n:2\r\n:10\r\n")]
    public async Task GcraRateLimit_WithCount_RoundTrip(
        string key,
        int maxBurst,
        int requestsPerPeriod,
        double periodSeconds,
        int count,
        string requestResp,
        string responseResp)
    {
        var msg = new RedisDatabase.GcraMessage(0, CommandFlags.None, key, maxBurst, requestsPerPeriod, periodSeconds, count);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.GcraRateLimit, requestResp, responseResp);

        Assert.True(result.Limited);
        Assert.Equal(11, result.MaxRequests);
        Assert.Equal(0, result.AvailableRequests);
        Assert.Equal(2, result.RetryAfterSeconds);
        Assert.Equal(10, result.FullBurstAfterSeconds);
    }

    [Theory(Timeout = 1000)]
    [InlineData("rate:api", 50, 1000, 60.0, "*5\r\n$4\r\nGCRA\r\n$8\r\nrate:api\r\n$2\r\n50\r\n$4\r\n1000\r\n$2\r\n60\r\n", "*5\r\n:0\r\n:51\r\n:25\r\n:-1\r\n:30\r\n")]
    public async Task GcraRateLimit_CustomPeriod_RoundTrip(
        string key,
        int maxBurst,
        int requestsPerPeriod,
        double periodSeconds,
        string requestResp,
        string responseResp)
    {
        var msg = new RedisDatabase.GcraMessage(0, CommandFlags.None, key, maxBurst, requestsPerPeriod, periodSeconds, 1);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.GcraRateLimit, requestResp, responseResp);

        Assert.False(result.Limited);
        Assert.Equal(51, result.MaxRequests);
        Assert.Equal(25, result.AvailableRequests);
        Assert.Equal(-1, result.RetryAfterSeconds);
        Assert.Equal(30, result.FullBurstAfterSeconds);
    }
}
