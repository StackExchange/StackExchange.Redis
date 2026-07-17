using System;
using StackExchange.Redis.Availability;
using Xunit;
#if NET8_0_OR_GREATER
using System.Threading;
#endif

namespace StackExchange.Redis.Tests;

public class CircuitBreakerUnitTests
{
    [Fact]
    public void Builder_AllDefaults_ReturnsSharedDefaultInstance()
    {
        // a builder that hasn't been touched should collapse onto the shared default instance...
        var a = new CircuitBreaker.Builder().Create();
        var b = new CircuitBreaker.Builder().Create();

        Assert.Same(a, b);
        Assert.Same(CircuitBreaker.Default, a);
    }

    [Fact]
    public void Builder_NonDefaults_ReturnsDistinctValidInstances()
    {
        // ...but as soon as any knob is changed, we get a fresh, distinct instance per Create()
        CircuitBreaker.Builder Configured() => new() { FailureRateThreshold = 42 };

        var a = Configured().Create();
        var b = Configured().Create();

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotSame(a, b);
        Assert.NotSame(CircuitBreaker.Default, a);
    }

    [Fact]
    public void None_IsDistinctFromDefault_ButStable()
    {
        Assert.NotNull(CircuitBreaker.None);
        Assert.Same(CircuitBreaker.None, CircuitBreaker.None);
        Assert.NotSame(CircuitBreaker.Default, CircuitBreaker.None);
    }

    [Fact]
    public void None_IsAlwaysHealthy()
    {
        var acc = CircuitBreaker.None.CreateAccumulator();
        // even a solid wall of tracked failures never trips the no-op breaker
        Assert.True(Record(acc, 10_000, new RedisTimeoutException(CommandFlags.None, "boom", CommandStatus.Unknown)));
    }

#if NET8_0_OR_GREATER
    // the time-windowed logic needs a controllable clock; TimeProvider is only available on net8.0+,
    // and we don't want to pull the BCL shim in just for down-level test coverage.
    [Fact]
    public void BelowMinimumFailures_StaysHealthy()
    {
        var time = new ManualTimeProvider();
        // threshold is trivially low (1%), so the *only* thing keeping us healthy is the minimum-count gate
        var acc = Build(time, failureRateThreshold: 1, minimumNumberOfFailures: 10).CreateAccumulator();

        // nine tracked failures: one short of the minimum, so we withhold judgement
        Assert.True(Record(acc, 9, Timeout()));
    }

    [Fact]
    public void AboveThreshold_Trips()
    {
        var time = new ManualTimeProvider();
        var acc = Build(time, failureRateThreshold: 50, minimumNumberOfFailures: 10).CreateAccumulator();

        // 20 tracked failures, 0 successes -> 100% failure rate, well past both gates
        Assert.False(Record(acc, 20, Timeout()));
    }

    [Fact]
    public void BelowThreshold_StaysHealthy()
    {
        var time = new ManualTimeProvider();
        var acc = Build(time, failureRateThreshold: 50, minimumNumberOfFailures: 10).CreateAccumulator();

        Record(acc, 10, Timeout()); // enough failures to clear the minimum-count gate
        Record(acc, 190); // but drowned out by successes -> 5% failure rate

        // a pure health read confirms we're comfortably under the 50% threshold
        Assert.True(acc.IsHealthy());
    }

    [Fact]
    public void UntrackedExceptions_CountAsSuccess()
    {
        var time = new ManualTimeProvider();
        // default tracking set (null) == RedisConnectionException + RedisTimeoutException only
        var acc = Build(time, failureRateThreshold: 1, minimumNumberOfFailures: 1).CreateAccumulator();

        // a flood of *untracked* failures must not trip the breaker...
        Assert.True(Record(acc, 100, new InvalidOperationException("not tracked")));

        // ...whereas the same volume of tracked failures does
        Assert.False(Record(acc, 100, Timeout()));
    }

    [Fact]
    public void OldFailures_AgeOutOfTheWindow()
    {
        var time = new ManualTimeProvider();
        var acc = Build(time, failureRateThreshold: 50, minimumNumberOfFailures: 1).CreateAccumulator();

        // saturate the window with failures -> tripped
        Assert.False(Record(acc, 100, Timeout()));

        // step past the whole window; the earlier failures should no longer count
        time.Advance(TimeSpan.FromSeconds(11));

        // the window is now empty of in-range failures -> healthy again
        Assert.True(acc.IsHealthy());
    }

    [Fact]
    public void IsHealthy_ReflectsStateWithoutObserving()
    {
        var time = new ManualTimeProvider();
        var acc = Build(time, failureRateThreshold: 50, minimumNumberOfFailures: 1).CreateAccumulator();

        Assert.True(acc.IsHealthy()); // nothing observed yet

        Assert.False(Record(acc, 100, Timeout())); // trip it via observations

        // the context-free overload reports the same verdict, purely by reading the window
        Assert.False(acc.IsHealthy());
    }

    [Fact]
    public void Reset_DiscardsHistory()
    {
        var time = new ManualTimeProvider();
        var acc = Build(time, failureRateThreshold: 50, minimumNumberOfFailures: 1).CreateAccumulator();

        // trip it wide open...
        Assert.False(Record(acc, 100, Timeout()));

        // ...then wipe the slate; the prior failures are forgotten
        acc.Reset();

        // an empty window reads as healthy again
        Assert.True(acc.IsHealthy());
    }

    private static CircuitBreaker Build(
        TimeProvider time,
        double failureRateThreshold,
        int minimumNumberOfFailures)
        => new CircuitBreaker.Builder
        {
            FailureRateThreshold = failureRateThreshold,
            MinimumNumberOfFailures = minimumNumberOfFailures,
            MetricsWindowSize = TimeSpan.FromSeconds(10),
            TimeProvider = time,
        }.Create();

    /// <summary>
    /// A hand-cranked <see cref="TimeProvider"/> whose clock only moves when we tell it to,
    /// so the bucketed metrics window is fully deterministic.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        // one tick == 100ns, matching TimeSpan; keeps Advance(TimeSpan) a straight addition
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _timestamp, by.Ticks);
    }
#endif

    private static RedisTimeoutException Timeout() => new(CommandFlags.None, "timeout", CommandStatus.Unknown);

    private static bool Record(CircuitBreaker.Accumulator accumulator, int count, Exception? fault = null)
    {
        // Trip applies the IsFailure gate (a fault the breaker doesn't consider a failure is counted as a
        // success), then records via ObserveResult; call it rather than ObserveResult directly so the gate runs
        for (int i = 0; i < count; i++)
        {
            accumulator.Trip(fault);
        }

        return accumulator.IsHealthy();
    }
}
