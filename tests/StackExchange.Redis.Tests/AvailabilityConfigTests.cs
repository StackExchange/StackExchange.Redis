using System;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Covers the shape shared by every Availability configuration type: an immutable policy with static
/// Default/None, configured through a nested Builder that validates in Create() and collapses onto the
/// shared default when nothing was customized.
/// </summary>
public class AvailabilityConfigTests
{
    // ---- HealthCheck ----
    [Fact]
    public void HealthCheck_UntouchedBuilder_CollapsesOntoDefault()
    {
        Assert.Same(HealthCheck.Default, new HealthCheck.Builder().Create());
        Assert.Same(HealthCheck.Default, new HealthCheck.Builder(HealthCheck.Default).Create());
    }

    [Fact]
    public void HealthCheck_BuilderRoundTripsExistingInstance()
    {
        HealthCheck original = new HealthCheck.Builder
        {
            ProbeCount = 7,
            ProbeTimeout = TimeSpan.FromSeconds(11),
            ProbeInterval = TimeSpan.FromMilliseconds(250),
            Probe = HealthCheckProbe.IsConnected,
            ProbePolicy = HealthCheckProbePolicy.MajoritySuccess,
        };

        // the copy constructor is the replacement for the old Clone()
        var copy = new HealthCheck.Builder(original).Create();

        Assert.NotSame(original, copy);
        Assert.Equal(original.ProbeCount, copy.ProbeCount);
        Assert.Equal(original.ProbeTimeout, copy.ProbeTimeout);
        Assert.Equal(original.ProbeInterval, copy.ProbeInterval);
        Assert.Same(original.Probe, copy.Probe);
        Assert.Same(original.ProbePolicy, copy.ProbePolicy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HealthCheck_RejectsNonPositiveProbeCount(int probeCount)
    {
        var builder = new HealthCheck.Builder { ProbeCount = probeCount };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Create());
        Assert.Equal(nameof(HealthCheck.Builder.ProbeCount), ex.ParamName);
    }

    [Fact]
    public void HealthCheck_RejectsNonPositiveProbeTimeout()
    {
        var builder = new HealthCheck.Builder { ProbeTimeout = TimeSpan.Zero };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Create());
        Assert.Equal(nameof(HealthCheck.Builder.ProbeTimeout), ex.ParamName);
    }

    [Fact]
    public void HealthCheck_RejectsNegativeProbeInterval()
    {
        var builder = new HealthCheck.Builder { ProbeInterval = TimeSpan.FromMilliseconds(-1) };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Create());
        Assert.Equal(nameof(HealthCheck.Builder.ProbeInterval), ex.ParamName);
    }

    [Fact]
    public void HealthCheck_RejectsUnrepresentableTotalBudget()
    {
        // ProbeCount x ProbeTimeout has to fit in int milliseconds; this used to overflow silently
        var builder = new HealthCheck.Builder { ProbeCount = 1000, ProbeTimeout = TimeSpan.FromDays(30) };
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Create());
    }

    [Fact]
    public void HealthCheck_None_IsDisabledAndStable()
    {
        Assert.Same(HealthCheck.None, HealthCheck.None);
        Assert.NotSame(HealthCheck.None, HealthCheck.Default);
        Assert.False(HealthCheck.None.IsEnabled);
        Assert.True(HealthCheck.Default.IsEnabled);
    }

    [Fact]
    public async Task HealthCheck_None_ReportsInconclusiveWithoutProbing()
    {
        // a null server would throw if the probe were actually invoked
        Assert.Equal(HealthCheckResult.Inconclusive, await HealthCheck.None.CheckHealthAsync(server: null!));
    }

    // ---- RetryPolicy ----
    [Fact]
    public void RetryPolicy_UntouchedBuilder_CollapsesOntoDefault()
    {
        Assert.Same(RetryPolicy.Default, new RetryPolicy.Builder().Create());
        Assert.Same(RetryPolicy.Default, new RetryPolicy.Builder(RetryPolicy.Default).Create());
    }

    [Fact]
    public void RetryPolicy_BuilderRoundTripsExistingInstance()
    {
        RetryPolicy original = new RetryPolicy.Builder
        {
            MaxAttempts = 9,
            MaxAttemptsBeforeFailover = 4,
            RetryDelay = TimeSpan.FromMilliseconds(123),
            JitterMax = TimeSpan.FromMilliseconds(45),
            FailoverDelay = TimeSpan.FromSeconds(6),
            MaxCommandRetryCategory = CommandFlags.CommandRetryWriteAccumulating,
        };

        var copy = new RetryPolicy.Builder(original).Create();

        Assert.NotSame(original, copy);
        Assert.Equal(original.MaxAttempts, copy.MaxAttempts);
        Assert.Equal(original.MaxAttemptsBeforeFailover, copy.MaxAttemptsBeforeFailover);
        Assert.Equal(original.RetryDelay, copy.RetryDelay);
        Assert.Equal(original.JitterMax, copy.JitterMax);
        Assert.Equal(original.FailoverDelay, copy.FailoverDelay);
        Assert.Equal(original.MaxCommandRetryCategory, copy.MaxCommandRetryCategory);
    }

    [Fact]
    public void RetryPolicy_RejectsZeroAttempts()
    {
        var builder = new RetryPolicy.Builder { MaxAttempts = 0 };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Create());
        Assert.Equal(nameof(RetryPolicy.Builder.MaxAttempts), ex.ParamName);
    }

    [Fact]
    public void RetryPolicy_RejectsZeroAttemptsBeforeFailover()
    {
        // previously this silently disabled failover, and only threw later, from WithRetry
        var builder = new RetryPolicy.Builder { MaxAttemptsBeforeFailover = 0 };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Create());
        Assert.Equal(nameof(RetryPolicy.Builder.MaxAttemptsBeforeFailover), ex.ParamName);
    }

    [Fact]
    public void RetryPolicy_RejectsNegativeDelays()
    {
        Assert.Equal(
            nameof(RetryPolicy.Builder.RetryDelay),
            Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy.Builder { RetryDelay = TimeSpan.FromTicks(-1) }.Create()).ParamName);
        Assert.Equal(
            nameof(RetryPolicy.Builder.JitterMax),
            Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy.Builder { JitterMax = TimeSpan.FromTicks(-1) }.Create()).ParamName);
        Assert.Equal(
            nameof(RetryPolicy.Builder.FailoverDelay),
            Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy.Builder { FailoverDelay = TimeSpan.FromTicks(-1) }.Create()).ParamName);
    }

    [Theory]
    [InlineData(CommandFlags.None)] // no category at all
    [InlineData(CommandFlags.FireAndForget)] // not a category
    [InlineData(CommandFlags.CommandRetryReadOnly | CommandFlags.PreferReplica)] // category plus noise
    public void RetryPolicy_RejectsInvalidRetryCategory(CommandFlags flags)
    {
        var builder = new RetryPolicy.Builder { MaxCommandRetryCategory = flags };
        var ex = Assert.Throws<ArgumentException>(() => builder.Create());
        Assert.Equal(nameof(RetryPolicy.Builder.MaxCommandRetryCategory), ex.ParamName);
    }

    [Fact]
    public void RetryPolicy_None_NeverRetries()
    {
        Assert.Same(RetryPolicy.None, RetryPolicy.None);
        Assert.NotSame(RetryPolicy.None, RetryPolicy.Default);

        // a transient, retryable fault on a read-only command: the default policy retries, None does not
        var fault = new FaultContext(new RedisConnectionException(ConnectionFailureType.SocketFailure, CommandFlags.None, "boom"));
        Assert.Equal(RetryResult.None, RetryPolicy.None.CanRetry(in fault));
    }

    // ---- CircuitBreaker ----
    [Fact]
    public void CircuitBreaker_RejectsOutOfRangeThreshold()
    {
        Assert.Equal(
            nameof(CircuitBreaker.Builder.FailureRateThreshold),
            Assert.Throws<ArgumentOutOfRangeException>(() => new CircuitBreaker.Builder { FailureRateThreshold = 101 }.Create()).ParamName);
        Assert.Equal(
            nameof(CircuitBreaker.Builder.FailureRateThreshold),
            Assert.Throws<ArgumentOutOfRangeException>(() => new CircuitBreaker.Builder { FailureRateThreshold = -1 }.Create()).ParamName);
    }

    [Fact]
    public void CircuitBreaker_RejectsInvalidWindowAndMinimum()
    {
        Assert.Equal(
            nameof(CircuitBreaker.Builder.MinimumNumberOfFailures),
            Assert.Throws<ArgumentOutOfRangeException>(() => new CircuitBreaker.Builder { MinimumNumberOfFailures = 0 }.Create()).ParamName);
        Assert.Equal(
            nameof(CircuitBreaker.Builder.MetricsWindowSize),
            Assert.Throws<ArgumentOutOfRangeException>(() => new CircuitBreaker.Builder { MetricsWindowSize = TimeSpan.Zero }.Create()).ParamName);
    }

    // ---- MultiGroupOptions ----
    [Fact]
    public void MultiGroupOptions_UntouchedBuilder_CollapsesOntoDefault()
    {
        Assert.Same(MultiGroupOptions.Default, new MultiGroupOptions.Builder().Create());
        Assert.Same(MultiGroupOptions.Default, new MultiGroupOptions.Builder(MultiGroupOptions.Default).Create());
    }

    [Fact]
    public void MultiGroupOptions_DefaultsAreTheSharedPolicyDefaults()
    {
        var options = MultiGroupOptions.Default;
        Assert.Same(HealthCheck.Default, options.HealthCheck);
        Assert.Same(CircuitBreaker.Default, options.CircuitBreaker);
        Assert.Same(RetryPolicy.Default, options.RetryPolicy);
        Assert.Equal(TimeSpan.FromSeconds(5), options.HealthCheckInterval);
        Assert.Equal(TimeSpan.Zero, options.FailbackDelay);
    }

    [Fact]
    public void MultiGroupOptions_RejectsInvalidIntervals()
    {
        Assert.Equal(
            nameof(MultiGroupOptions.Builder.HealthCheckInterval),
            Assert.Throws<ArgumentOutOfRangeException>(() => new MultiGroupOptions.Builder { HealthCheckInterval = TimeSpan.Zero }.Create()).ParamName);
        Assert.Equal(
            nameof(MultiGroupOptions.Builder.FailbackDelay),
            Assert.Throws<ArgumentOutOfRangeException>(() => new MultiGroupOptions.Builder { FailbackDelay = TimeSpan.FromTicks(-1) }.Create()).ParamName);

        // MaxValue is the documented "never" sentinel for both, and must remain legal
        MultiGroupOptions ok = new MultiGroupOptions.Builder
        {
            HealthCheckInterval = TimeSpan.MaxValue,
            FailbackDelay = TimeSpan.MaxValue,
        };
        Assert.Equal(TimeSpan.MaxValue, ok.HealthCheckInterval);
        Assert.Equal(TimeSpan.MaxValue, ok.FailbackDelay);
    }

    [Fact]
    public void MultiGroupOptions_BuilderConvertsImplicitly()
    {
        // every Builder in the namespace supports this, so options can be written inline at the call-site
        MultiGroupOptions options = new MultiGroupOptions.Builder { FailbackDelay = TimeSpan.FromMinutes(2) };
        Assert.Equal(TimeSpan.FromMinutes(2), options.FailbackDelay);
    }

    // ---- per-member override resolution ----
    [Fact]
    public void Member_ResolvesGroupDefaultsWhenNoOverride()
    {
        var member = new ConnectionGroupMember("localhost:6379");
        var options = MultiGroupOptions.Default;

        Assert.Same(options.HealthCheck, member.ResolveHealthCheck(options));
        Assert.Same(options.CircuitBreaker, member.ResolveCircuitBreaker(options));
        Assert.Equal(options.FailbackDelay, member.ResolveFailbackDelay(options));
    }

    [Fact]
    public void Member_OverridesBeatGroupDefaults()
    {
        HealthCheck memberCheck = new HealthCheck.Builder { ProbeCount = 1 };
        CircuitBreaker memberBreaker = new CircuitBreaker.Builder { FailureRateThreshold = 42 };
        var member = new ConnectionGroupMember("localhost:6379")
        {
            HealthCheck = memberCheck,
            CircuitBreaker = memberBreaker,
            FailbackDelay = TimeSpan.FromMinutes(3),
        };

        var options = MultiGroupOptions.Default;
        Assert.Same(memberCheck, member.ResolveHealthCheck(options));
        Assert.Same(memberBreaker, member.ResolveCircuitBreaker(options));
        Assert.Equal(TimeSpan.FromMinutes(3), member.ResolveFailbackDelay(options));
    }

    [Fact]
    public void Member_CircuitBreakerFallsBackToItsOwnConfigurationBeforeTheGroup()
    {
        // precedence is: member override, then the member's own ConfigurationOptions, then the group default
        CircuitBreaker fromConfig = new CircuitBreaker.Builder { FailureRateThreshold = 42 };
        var config = ConfigurationOptions.Parse("localhost:6379");
        config.CircuitBreaker = fromConfig;

        var member = new ConnectionGroupMember(config);
        Assert.Same(fromConfig, member.ResolveCircuitBreaker(MultiGroupOptions.Default));

        CircuitBreaker fromMember = new CircuitBreaker.Builder { FailureRateThreshold = 13 };
        member.CircuitBreaker = fromMember;
        Assert.Same(fromMember, member.ResolveCircuitBreaker(MultiGroupOptions.Default));
    }

    [Fact]
    public void GroupDefaultsAreNotWrittenBackIntoCallerConfiguration()
    {
        // callers may legitimately reuse a ConfigurationOptions across connections, so resolving a group
        // default must not mutate it (this used to be a `config.CircuitBreaker ??= options.CircuitBreaker`)
        var config = ConfigurationOptions.Parse("localhost:6379");
        var member = new ConnectionGroupMember(config);

        Assert.Same(MultiGroupOptions.Default.CircuitBreaker, member.ResolveCircuitBreaker(MultiGroupOptions.Default));
        Assert.Null(config.CircuitBreaker);
    }

    // ---- WithRetry() policy resolution ----
    [Fact]
    public async Task WithRetry_UsesConfiguredPolicyForASingleConnection()
    {
        RetryPolicy configured = new RetryPolicy.Builder { MaxAttempts = 7 };
        var config = ConfigurationOptions.Parse("localhost:6379");
        config.RetryPolicy = configured;
        config.AbortOnConnectFail = false;

        await using var muxer = await ConnectionMultiplexer.ConnectAsync(config);
        var retrying = Assert.IsType<RetryDatabase>(muxer.GetDatabase().WithRetry());
        Assert.Same(configured, retrying.Policy);
    }

    [Fact]
    public async Task WithRetry_FallsBackToDefaultWhenNoneConfigured()
    {
        var config = ConfigurationOptions.Parse("localhost:6379");
        config.AbortOnConnectFail = false;

        await using var muxer = await ConnectionMultiplexer.ConnectAsync(config);
        var retrying = Assert.IsType<RetryDatabase>(muxer.GetDatabase().WithRetry());
        Assert.Same(RetryPolicy.Default, retrying.Policy);
    }
}
