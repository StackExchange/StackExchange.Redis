using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class CancellationTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task WithCancellation_CancelledToken_ThrowsOperationCanceledException()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await db.StringSetAsync(Me(), "value").WaitAsync(cts.Token));
    }

    private IInternalConnectionMultiplexer Create() => Create(syncTimeout: 10_000);

    [Fact]
    public async Task WithCancellation_ValidToken_OperationSucceeds()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var cts = new CancellationTokenSource();

        RedisKey key = Me();
        // This should succeed
        await db.StringSetAsync(key, "value");
        var result = await db.StringGetAsync(key).WaitAsync(cts.Token);
        Assert.Equal("value", result);
    }

    private void Pause(IDatabase db)
    {
        db.Execute("client", ["pause", ConnectionPauseMilliseconds], CommandFlags.FireAndForget);
    }

    [Fact]
    public async Task WithTimeout_ShortTimeout_Async_ThrowsOperationCanceledException()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var watch = Stopwatch.StartNew();
        Pause(db);

        var timeout = TimeSpan.FromMilliseconds(ShortDelayMilliseconds);
        // This might throw due to timeout, but let's test the mechanism
        var pending = db.StringSetAsync(Me(), "value").WaitAsync(timeout); // check we get past this
        try
        {
            await pending;
            // If it succeeds, that's fine too - Redis is fast
            Assert.Fail(ExpectedCancel + ": " + watch.ElapsedMilliseconds + "ms");
        }
        catch (TimeoutException)
        {
            // Expected for very short timeouts
            Log($"Timeout after {watch.ElapsedMilliseconds}ms");
        }
    }

    private const string ExpectedCancel = "This operation should have been cancelled";

    [Fact]
    public async Task WithoutCancellation_OperationsWorkNormally()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        // No cancellation - should work normally
        RedisKey key = Me();
        await db.StringSetAsync(key, "value");
        var result = await db.StringGetAsync(key);
        Assert.Equal("value", result);
    }

    public enum CancelStrategy
    {
        Constructor,
        Method,
        Manual,
    }

    private const int ConnectionPauseMilliseconds = 50, ShortDelayMilliseconds = 5;

    private static CancellationTokenSource CreateCts(CancelStrategy strategy)
    {
        switch (strategy)
        {
            case CancelStrategy.Constructor:
                return new CancellationTokenSource(TimeSpan.FromMilliseconds(ShortDelayMilliseconds));
            case CancelStrategy.Method:
                var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromMilliseconds(ShortDelayMilliseconds));
                return cts;
            case CancelStrategy.Manual:
                cts = new();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(ShortDelayMilliseconds);
                    // ReSharper disable once MethodHasAsyncOverload - TFM-dependent
                    cts.Cancel();
                });
                return cts;
            default:
                throw new ArgumentOutOfRangeException(nameof(strategy));
        }
    }

    [Theory]
    [InlineData(CancelStrategy.Constructor)]
    [InlineData(CancelStrategy.Method)]
    [InlineData(CancelStrategy.Manual)]
    public async Task CancellationDuringOperation_Async_CancelsGracefully(CancelStrategy strategy)
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var watch = Stopwatch.StartNew();
        Pause(db);

        // Cancel after a short delay
        using var cts = CreateCts(strategy);

        // Start an operation and cancel it mid-flight
        var pending = db.StringSetAsync($"{Me()}:{strategy}", "value").WaitAsync(cts.Token);

        try
        {
            await pending;
            Assert.Fail(ExpectedCancel + ": " + watch.ElapsedMilliseconds + "ms");
        }
        catch (OperationCanceledException oce)
        {
            // Expected if cancellation happens during operation
            Log($"Cancelled after {watch.ElapsedMilliseconds}ms");
            Assert.Equal(cts.Token, oce.CancellationToken);
        }
    }
}
