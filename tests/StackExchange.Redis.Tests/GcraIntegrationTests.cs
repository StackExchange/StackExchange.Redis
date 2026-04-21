using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class GcraIntegrationTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact(Timeout = 5000)]
    public async Task GcraRateLimit_NonDefaultCount()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var result = await db.StringGcraRateLimitAsync(key, maxBurst: 10, tokensPerPeriod: 10, periodSeconds: 1.0, count: 8);
        Assert.False(result.Limited);
        Log($"Run 1: Limited: {result.Limited}, Available: {result.AvailableTokens}, RetryAfter: {result.RetryAfterSeconds}, FullBurstAfter: {result.FullBurstAfterSeconds}, MaxTokens: {result.MaxTokens}");

        result = await db.StringGcraRateLimitAsync(key, maxBurst: 10, tokensPerPeriod: 10, periodSeconds: 1.0, count: 8);
        Assert.True(result.Limited);
        Log($"Run 2: Limited: {result.Limited}, Available: {result.AvailableTokens}, RetryAfter: {result.RetryAfterSeconds}, FullBurstAfter: {result.FullBurstAfterSeconds}, MaxTokens: {result.MaxTokens}");
    }

    [Fact(Timeout = 5000)]
    public async Task GcraRateLimit_SmokeTest()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var key = Me() + Guid.NewGuid();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        for (int i = 0; i < 15; i++)
        {
            var result = await db.StringGcraRateLimitAsync(key, maxBurst: 10, tokensPerPeriod: 10, periodSeconds: 1.0);
            Log($"Run {i}: Limited: {result.Limited}, Available: {result.AvailableTokens}, RetryAfter: {result.RetryAfterSeconds}, FullBurstAfter: {result.FullBurstAfterSeconds}, MaxTokens: {result.MaxTokens}");
            if (i <= 10)
            {
                Assert.False(result.Limited, $"run {i}");
            }
            else
            {
                Assert.True(result.Limited, $"run {i}");
            }
            Assert.Equal(11, result.MaxTokens);
            Assert.Equal(Math.Max(0, 10 - i), result.AvailableTokens);
            if (result.Limited)
            {
                Assert.True(result.RetryAfterSeconds > 0);
            }
            else
            {
                Assert.Equal(-1, result.RetryAfterSeconds);
            }
            Assert.True(result.FullBurstAfterSeconds > 0);
        }
        await db.TryAcquireGcraAsync(
            key,
            maxBurst: 10,
            tokensPerPeriod: 10,
            allow: TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
