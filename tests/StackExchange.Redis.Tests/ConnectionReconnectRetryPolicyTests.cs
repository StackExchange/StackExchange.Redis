using System;
using Xunit;

namespace StackExchange.Redis.Tests;

public class TransientErrorTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public void TestExponentialRetry()
    {
        IReconnectRetryPolicy exponentialRetry = new ExponentialRetry(5000);
        Assert.False(exponentialRetry.ShouldRetry(0, 0));
        Assert.True(exponentialRetry.ShouldRetry(1, 5600));
        Assert.True(exponentialRetry.ShouldRetry(2, 6050));
        Assert.False(exponentialRetry.ShouldRetry(2, 4050));
    }

    [Fact]
    public void TestExponentialMaxRetry()
    {
        IReconnectRetryPolicy exponentialRetry = new ExponentialRetry(5000);
        Assert.True(exponentialRetry.ShouldRetry(long.MaxValue, (int)TimeSpan.FromSeconds(30).TotalMilliseconds));
    }

    [Fact]
    public void TestExponentialRetryArgs()
    {
        _ = new ExponentialRetry(5000);
        _ = new ExponentialRetry(5000, 10000);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialRetry(-1));
        Assert.Equal("deltaBackOffMilliseconds", ex.ParamName);

        ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialRetry(5000, -1));
        Assert.Equal("maxDeltaBackOffMilliseconds", ex.ParamName);

        ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialRetry(10000, 5000));
        Assert.Equal("maxDeltaBackOffMilliseconds", ex.ParamName);
    }

    [Fact]
    public void TestLinearRetry()
    {
        IReconnectRetryPolicy linearRetry = new LinearRetry(5000);
        Assert.False(linearRetry.ShouldRetry(0, 0));
        Assert.False(linearRetry.ShouldRetry(2, 4999));
        Assert.True(linearRetry.ShouldRetry(1, 5000));
    }
}
