using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Unit tests for GCRA rate limiting functionality.
/// </summary>
public class GcraUnitTests(ITestOutputHelper log)
{
    private RedisKey Me([CallerMemberName] string callerName = "") => callerName;

    [Fact]
    public async Task GcraRateLimit_NotLimited_ReturnsExpectedResult()
    {
        // Arrange
        var expectedResult = new GcraRateLimitResult(
            limited: false,
            maxTokens: 10,
            availableTokens: 9,
            retryAfterSeconds: 0,
            fullBurstAfterSeconds: 1);

        using var server = new GcraTestServer(expectedResult, log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();

        // Act
        var result = await db.StringGcraRateLimitAsync(key, maxBurst: 10, tokensPerPeriod: 10, periodSeconds: 1.0, count: 1);

        // Assert
        Assert.False(result.Limited);
        Assert.Equal(10, result.MaxTokens);
        Assert.Equal(9, result.AvailableTokens);
        Assert.Equal(0, result.RetryAfterSeconds);
        Assert.Equal(1, result.FullBurstAfterSeconds);

        // Verify the request received by the server
        var lastRequest = server.LastRequest;
        Assert.NotNull(lastRequest);
        Assert.Equal(key, lastRequest.Key);
        Assert.Equal(10, lastRequest.MaxBurst);
        Assert.Equal(10, lastRequest.TokensPerPeriod);
        Assert.Equal(1.0, lastRequest.PeriodSeconds);
        Assert.Equal(1, lastRequest.Count);
    }

    [Fact]
    public async Task GcraRateLimit_Limited_ReturnsExpectedResult()
    {
        // Arrange
        var expectedResult = new GcraRateLimitResult(
            limited: true,
            maxTokens: 5,
            availableTokens: 0,
            retryAfterSeconds: 2,
            fullBurstAfterSeconds: 10);

        using var server = new GcraTestServer(expectedResult, log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();

        // Act
        var result = await db.StringGcraRateLimitAsync(key, maxBurst: 5, tokensPerPeriod: 5, periodSeconds: 1.0);

        // Assert
        Assert.True(result.Limited);
        Assert.Equal(5, result.MaxTokens);
        Assert.Equal(0, result.AvailableTokens);
        Assert.Equal(2, result.RetryAfterSeconds);
        Assert.Equal(10, result.FullBurstAfterSeconds);

        // Verify the request received by the server
        var lastRequest = server.LastRequest;
        Assert.NotNull(lastRequest);
        Assert.Equal(key, lastRequest.Key);
        Assert.Equal(5, lastRequest.MaxBurst);
        Assert.Equal(5, lastRequest.TokensPerPeriod);
        Assert.Equal(1.0, lastRequest.PeriodSeconds);
        Assert.Equal(1, lastRequest.Count);
    }

    [Fact]
    public async Task GcraRateLimit_WithCustomCount_SendsCorrectParameters()
    {
        // Arrange
        var expectedResult = new GcraRateLimitResult(
            limited: false,
            maxTokens: 100,
            availableTokens: 95,
            retryAfterSeconds: 0,
            fullBurstAfterSeconds: 5);

        using var server = new GcraTestServer(expectedResult, log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();

        // Act
        var result = await db.StringGcraRateLimitAsync(key, maxBurst: 100, tokensPerPeriod: 100, periodSeconds: 60.0, count: 5);

        // Assert
        Assert.False(result.Limited);
        Assert.Equal(100, result.MaxTokens);
        Assert.Equal(95, result.AvailableTokens);

        // Verify the request received by the server includes the count parameter
        var lastRequest = server.LastRequest;
        Assert.NotNull(lastRequest);
        Assert.Equal(key, lastRequest.Key);
        Assert.Equal(100, lastRequest.MaxBurst);
        Assert.Equal(100, lastRequest.TokensPerPeriod);
        Assert.Equal(60.0, lastRequest.PeriodSeconds);
        Assert.Equal(5, lastRequest.Count);
    }

    [Fact]
    public async Task GcraRateLimit_SyncVersion_ReturnsExpectedResult()
    {
        // Arrange
        var expectedResult = new GcraRateLimitResult(
            limited: false,
            maxTokens: 20,
            availableTokens: 19,
            retryAfterSeconds: 0,
            fullBurstAfterSeconds: 1);

        using var server = new GcraTestServer(expectedResult, log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();

        // Act
        var result = db.StringGcraRateLimit(key, maxBurst: 20, tokensPerPeriod: 20, periodSeconds: 1.0);

        // Assert
        Assert.False(result.Limited);
        Assert.Equal(20, result.MaxTokens);
        Assert.Equal(19, result.AvailableTokens);
        Assert.Equal(0, result.RetryAfterSeconds);
        Assert.Equal(1, result.FullBurstAfterSeconds);

        // Verify the request received by the server
        var lastRequest = server.LastRequest;
        Assert.NotNull(lastRequest);
        Assert.Equal(key, lastRequest.Key);
        Assert.Equal(20, lastRequest.MaxBurst);
        Assert.Equal(20, lastRequest.TokensPerPeriod);
        Assert.Equal(1.0, lastRequest.PeriodSeconds);
        Assert.Equal(1, lastRequest.Count);
    }
}
