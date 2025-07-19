using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

#if !NET6_0_OR_GREATER
internal static class TaskExtensions
{
    // suboptimal polyfill version of the .NET 6+ API; I'm not recommending this for production use,
    // but it's good enough for tests
    public static Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
    {
        if (task.IsCompleted) return task;
        return Wrap(task, timeout);

        static async Task<T> Wrap(Task<T> task, TimeSpan timeout)
        {
            Task other = Task.Delay(timeout);
            var first = await Task.WhenAny(task, other);
            if (ReferenceEquals(first, other))
            {
                throw new TimeoutException();
            }
            return await task;
        }
    }
}
#endif

[Collection(SharedConnectionFixture.Key)]
public class CancellationTests : TestBase
{
    public CancellationTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task WithCancellation_CancelledToken_ThrowsOperationCanceledException()
    {
        using var conn = Create();
        var db = conn.GetDatabase();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await db.StringSetAsync(Me(), "value").WaitAsync(cts.Token);
        });
    }

    private IInternalConnectionMultiplexer Create() => Create(syncTimeout: 10_000);

    [Fact]
    public async Task WithCancellation_ValidToken_OperationSucceeds()
    {
        using var conn = Create();
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
        db.Execute("client", new object[] { "pause", ConnectionPauseMilliseconds }, CommandFlags.FireAndForget);
    }

    private void Pause(IServer server)
    {
        server.Execute("client", new object[] { "pause", ConnectionPauseMilliseconds }, CommandFlags.FireAndForget);
    }

    [Fact]
    public async Task WithTimeout_ShortTimeout_Async_ThrowsOperationCanceledException()
    {
        using var conn = Create();
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
        using var conn = Create();
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
        using var conn = Create();
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

    [Fact]
    public async Task ScanCancellable()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var server = conn.GetServer(conn.GetEndPoints()[0]);

        using var cts = new CancellationTokenSource();

        var watch = Stopwatch.StartNew();
        Pause(server);
        try
        {
            db.StringSet(Me(), "value", TimeSpan.FromMinutes(5), flags: CommandFlags.FireAndForget);
            await using var iter = server.KeysAsync(pageSize: 1000).WithCancellation(cts.Token).GetAsyncEnumerator();
            var pending = iter.MoveNextAsync();
            Assert.False(cts.Token.IsCancellationRequested);
            cts.CancelAfter(ShortDelayMilliseconds); // start this *after* we've got past the initial check
            while (await pending)
            {
                pending = iter.MoveNextAsync();
            }
            Assert.Fail($"{ExpectedCancel}: {watch.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException oce)
        {
            var taken = watch.ElapsedMilliseconds;
            // Expected if cancellation happens during operation
            Log($"Cancelled after {taken}ms");
            Assert.True(taken < ConnectionPauseMilliseconds / 2, "Should have cancelled much sooner");
            Assert.Equal(cts.Token, oce.CancellationToken);
        }
    }
}
