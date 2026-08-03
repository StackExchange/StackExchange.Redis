using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Interfaces;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

// Configuration validation (which lives on RetryPolicy.Builder, since a RetryPolicy is immutable and
// validated on construction) and the wait/failover timing state machine of RetryController; neither needs a
// server, or even an inner database - CanRetry and the delays never touch one.
public class RetryControllerTests
{
    // A failover threshold below 1 could never be reached by the attempt counter (which starts at 1), so
    // it would *silently* disable failover; that is rejected up front.
    [Fact]
    public void Policy_RejectsUnreachableFailoverThreshold()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy.Builder { MaxAttemptsBeforeFailover = 0 }.Create());

    // DatabaseFeatureFlags is internal, so theories take a bool and map here
    private static DatabaseFeatureFlags Features(bool withFailover)
        => withFailover ? DatabaseFeatureFlags.Failover : DatabaseFeatureFlags.None;

    // Negative durations are nonsense for a delay; each is validated separately.
    [Fact]
    public void Policy_RejectsNegativeDurations()
    {
        var negative = TimeSpan.FromMilliseconds(-1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy.Builder { RetryDelay = negative }.Create());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy.Builder { JitterMax = negative }.Create());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy.Builder { FailoverDelay = negative }.Create());
    }

    // The watch-contention budget counts *attempts*, so 1 means "try once, do not re-attempt"; zero or
    // negative is meaningless rather than a way to say "never execute".
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Policy_RejectsNonPositiveWatchAttempts(int attempts)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy.Builder { MaxAttemptsOnWatchConflict = attempts }.Create());

    // ...and 1 is accepted, since that is how re-attempting is switched off
    [Fact]
    public void Policy_AcceptsSingleWatchAttempt()
        => Assert.Equal(1, new RetryPolicy.Builder { MaxAttemptsOnWatchConflict = 1 }.Create().MaxAttemptsOnWatchConflict);

    // The category cap must name exactly one of the CommandRetry* values: an empty value, or one that
    // strays outside the category bits, is a usage error rather than something to interpret.
    [Fact]
    public void Policy_RejectsNonCategoryMaxCommandRetryCategory()
    {
        Assert.Throws<ArgumentException>(() => new RetryPolicy.Builder { MaxCommandRetryCategory = CommandFlags.None }.Create());
        Assert.Throws<ArgumentException>(() => new RetryPolicy.Builder { MaxCommandRetryCategory = CommandFlags.FireAndForget }.Create());
        Assert.Throws<ArgumentException>(
            () => new RetryPolicy.Builder { MaxCommandRetryCategory = CommandFlags.CommandRetryReadOnly | CommandFlags.FireAndForget }.Create());

        RetryPolicy valid = new RetryPolicy.Builder { MaxCommandRetryCategory = CommandFlags.CommandRetryAlways };
        Assert.Equal(CommandFlags.CommandRetryAlways, valid.MaxCommandRetryCategory);
    }

    // RetryPolicy.None means *nothing* is re-attempted - including watch contention, which is bounded by
    // an attempt count rather than by CanRetry (nothing was applied, so there is no fault to judge).
    [Fact]
    public void NonePolicy_DisablesWatchReattempts()
    {
        Assert.Equal(1, RetryPolicy.None.MaxAttemptsOnWatchConflict);
        Assert.Equal(DefaultMaxAttemptsOnWatchConflict, RetryPolicy.Default.MaxAttemptsOnWatchConflict);
    }

    private const int DefaultMaxAttemptsOnWatchConflict = 3;

    // Round-tripping a policy through a builder must preserve the watch budget along with everything else.
    [Fact]
    public void Policy_RoundTripsThroughBuilder()
    {
        RetryPolicy original = new RetryPolicy.Builder { MaxAttemptsOnWatchConflict = 7, MaxAttempts = 4 };
        var copy = new RetryPolicy.Builder(original).Create();

        Assert.Equal(7, copy.MaxAttemptsOnWatchConflict);
        Assert.Equal(4, copy.MaxAttempts);
    }

    // Contention is not a fault, so there is no backoff - only jitter, to stop two callers colliding again
    // in lock-step. With jitter disabled the re-attempt is immediate.
    [Fact]
    public async Task WatchConflictDelay_HasNoBackoff()
    {
        var controller = new RetryController(
            new RetryPolicy.Builder
            {
                RetryDelay = TimeSpan.FromMilliseconds(LongMillis),
                FailoverDelay = TimeSpan.FromMilliseconds(LongMillis),
                JitterMax = TimeSpan.Zero,
            },
            DatabaseFeatureFlags.Failover);

        var watch = Stopwatch.StartNew();
        await controller.WatchConflictDelayAsync();
        Assert.True(watch.ElapsedMilliseconds < ShortMillis, $"returned after {watch.ElapsedMilliseconds}ms");
    }

    // Capturing the "next failover" token costs something, so we only do it when a failover could
    // actually be waited on: the database must offer failover, there must be more than one attempt, and
    // the threshold must sit strictly below the attempt cap (at the cap it can never be reached).
    [Theory]
    [InlineData(3, 1, true, true)]
    [InlineData(3, 1, false, false)] // no failover available
    [InlineData(1, 1, true, false)] // single attempt: nothing to retry
    [InlineData(3, 3, true, false)] // threshold == cap: unreachable
    [InlineData(3, 4, true, false)] // threshold beyond cap: unreachable
    public void TracksFailover_OnlyWhenReachable(int maxAttempts, int beforeFailover, bool withFailover, bool expected)
    {
        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = maxAttempts, MaxAttemptsBeforeFailover = beforeFailover };
        Assert.Equal(expected, new RetryController(policy, Features(withFailover)).TracksFailover);
    }

    // MaxAttempts = 1 means "try once": the very first failure is already exhausted.
    [Fact]
    public void SingleAttempt_NeverRetries()
    {
        var controller = new RetryController(new RetryPolicy.Builder { MaxAttempts = 1 }, DatabaseFeatureFlags.Failover);
        using var cts = new CancellationTokenSource();
        var failover = cts.Token;
        var fault = new RedisServerException(RedisErrorKind.Loading, CommandFlags.CommandRetryReadOnly, "LOADING");

        Assert.False(controller.CanRetry(1, fault, ref failover, out var delay));
        Assert.False(delay.CanBeCanceled);
    }

    // --- FailoverOrDelayAsync -----------------------------------------------------------------------
    // Deliberately coarse thresholds: we are distinguishing "waited for the configured period" from
    // "returned as soon as it could", not measuring the clock.
    private const int LongMillis = 2000, ShortMillis = 1000;

    // No failover token: this is a routine pause between same-server attempts, so it waits RetryDelay.
    [Fact]
    public async Task Delay_WithoutFailoverToken_WaitsRetryDelay()
    {
        var controller = new RetryController(
            new RetryPolicy.Builder { RetryDelay = TimeSpan.FromMilliseconds(LongMillis), JitterMax = TimeSpan.Zero },
            DatabaseFeatureFlags.None);

        var watch = Stopwatch.StartNew();
        await controller.FailoverOrDelayAsync(CancellationToken.None);
        Assert.True(watch.ElapsedMilliseconds >= ShortMillis, $"returned after {watch.ElapsedMilliseconds}ms");
    }

    // A failover token that has *already* fired: there is nothing to wait for, so only jitter applies -
    // and in particular RetryDelay is deliberately ignored on the failover path.
    [Fact]
    public async Task Delay_WithFiredFailoverToken_ReturnsImmediately()
    {
        var controller = new RetryController(
            new RetryPolicy.Builder
            {
                RetryDelay = TimeSpan.FromMilliseconds(LongMillis),
                FailoverDelay = TimeSpan.FromMilliseconds(LongMillis),
                JitterMax = TimeSpan.Zero,
            },
            DatabaseFeatureFlags.Failover);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel, not CancelAsync: this project also targets net481

        var watch = Stopwatch.StartNew();
        await controller.FailoverOrDelayAsync(cts.Token);
        Assert.True(watch.ElapsedMilliseconds < ShortMillis, $"returned after {watch.ElapsedMilliseconds}ms");
    }

    // A failover that arrives while we are waiting: we stop waiting as soon as it lands, rather than
    // sitting out the whole FailoverDelay.
    [Fact]
    public async Task Delay_WhenFailoverArrives_StopsWaiting()
    {
        var controller = new RetryController(
            new RetryPolicy.Builder { FailoverDelay = TimeSpan.FromMilliseconds(LongMillis * 4), JitterMax = TimeSpan.Zero },
            DatabaseFeatureFlags.Failover);

        using var cts = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();
        var pending = controller.FailoverOrDelayAsync(cts.Token);
        cts.Cancel(); // Cancel, not CancelAsync: this project also targets net481
        await pending;

        Assert.True(watch.ElapsedMilliseconds < ShortMillis, $"returned after {watch.ElapsedMilliseconds}ms");
    }

    // A failover that never arrives: we give it FailoverDelay and then proceed anyway (retrying on the
    // original server is better than giving up).
    [Fact]
    public async Task Delay_WhenFailoverNeverArrives_ProceedsAfterFailoverDelay()
    {
        var controller = new RetryController(
            new RetryPolicy.Builder { FailoverDelay = TimeSpan.FromMilliseconds(LongMillis), JitterMax = TimeSpan.Zero },
            DatabaseFeatureFlags.Failover);

        using var cts = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();
        await controller.FailoverOrDelayAsync(cts.Token);

        Assert.True(watch.ElapsedMilliseconds >= ShortMillis, $"returned after {watch.ElapsedMilliseconds}ms");
        Assert.False(cts.IsCancellationRequested); // no failover ever happened
    }
}
