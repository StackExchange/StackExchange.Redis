using System;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests;

public class HealthCheckPolicyUnitTests
{
    /// <summary>
    /// Probe traffic must not be counted as work a caller is waiting on, because idleness is what decides
    /// whether an endpoint that has left the deployment can be given up - a probe that looks like caller work
    /// makes a server appear busy precisely because we are watching it.
    /// </summary>
    /// <remarks>
    /// The other half of this test is what it asserts is *false*: the probe must not become an internal call,
    /// which would also change how it is queued - internal calls bypass the backlog. A health check that
    /// bypasses the backlog cannot observe a bridge whose queue is not draining, which is exactly the signal
    /// geo-redundant failover depends on.
    /// </remarks>
    [Fact]
    public void ProbeTrafficIsNotCallerFacingButIsRoutedNormally()
    {
        var flags = new HealthCheckContext(null!, TimeSpan.FromSeconds(1)).ProbeFlags;
        Assert.NotEqual(CommandFlags.None, flags);

        var message = Message.Create(-1, flags, RedisCommand.PING);
        Assert.True(message.IsProbe);
        Assert.False(message.IsCallerFacing);
        Assert.False(message.IsInternalCall);

        // and an ordinary command is unaffected in both directions
        var ordinary = Message.Create(-1, CommandFlags.None, RedisCommand.PING);
        Assert.False(ordinary.IsProbe);
        Assert.True(ordinary.IsCallerFacing);
    }

    [Theory]
    [InlineData(0, 0, 5, HealthCheckResult.Inconclusive)] // No results yet
    [InlineData(1, 0, 0, HealthCheckResult.Healthy)] // One success, no more probes
    [InlineData(0, 1, 0, HealthCheckResult.Unhealthy)] // One failure, no more probes
    [InlineData(2, 1, 0, HealthCheckResult.Healthy)] // Mixed results, success wins
    [InlineData(1, 2, 0, HealthCheckResult.Healthy)] // Mixed results, success wins
    [InlineData(5, 0, 0, HealthCheckResult.Healthy)] // All successes
    [InlineData(0, 5, 0, HealthCheckResult.Unhealthy)] // All failures
    [InlineData(1, 0, 2, HealthCheckResult.Healthy)] // Early success
    [InlineData(0, 1, 2, HealthCheckResult.Inconclusive)] // Early failure but more probes remain
    public void AnySuccess_EvaluatesCorrectly(int success, int failure, int remaining, HealthCheckResult expected)
    {
        var policy = HealthCheckProbePolicy.AnySuccess;
        var context = new HealthCheckProbeContext(HealthCheckResult.Inconclusive, success, failure, remaining, TimeSpan.Zero);

        var result = policy.Evaluate(context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, 0, 5, HealthCheckResult.Inconclusive)] // No results yet
    [InlineData(1, 0, 0, HealthCheckResult.Healthy)] // One success, no more probes
    [InlineData(0, 1, 0, HealthCheckResult.Unhealthy)] // One failure, no more probes
    [InlineData(2, 1, 0, HealthCheckResult.Unhealthy)] // Mixed results, one failure is enough
    [InlineData(1, 2, 0, HealthCheckResult.Unhealthy)] // Mixed results, one failure is enough
    [InlineData(5, 0, 0, HealthCheckResult.Healthy)] // All successes
    [InlineData(0, 5, 0, HealthCheckResult.Unhealthy)] // All failures
    [InlineData(1, 0, 2, HealthCheckResult.Inconclusive)] // Success but more probes remain
    [InlineData(0, 1, 2, HealthCheckResult.Unhealthy)] // Early failure
    [InlineData(4, 0, 1, HealthCheckResult.Inconclusive)] // Multiple successes but still waiting
    public void AllSuccess_EvaluatesCorrectly(int success, int failure, int remaining, HealthCheckResult expected)
    {
        var policy = HealthCheckProbePolicy.AllSuccess;
        var context = new HealthCheckProbeContext(HealthCheckResult.Inconclusive, success, failure, remaining, TimeSpan.Zero);

        var result = policy.Evaluate(context);

        Assert.Equal(expected, result);
    }

    [Theory]
    // Total 5 probes: need 3 for majority
    [InlineData(0, 0, 5, HealthCheckResult.Inconclusive)] // No results yet
    [InlineData(3, 0, 2, HealthCheckResult.Healthy)] // Reached majority (3/5)
    [InlineData(2, 0, 3, HealthCheckResult.Inconclusive)] // Not yet majority
    [InlineData(0, 3, 2, HealthCheckResult.Unhealthy)] // Majority impossible (3 failures)
    [InlineData(2, 2, 1, HealthCheckResult.Inconclusive)] // Tied, one more probe
    [InlineData(3, 2, 0, HealthCheckResult.Healthy)] // Majority achieved (3/5)
    [InlineData(2, 3, 0, HealthCheckResult.Unhealthy)] // Majority failed (3/5)
    [InlineData(5, 0, 0, HealthCheckResult.Healthy)] // All successes
    [InlineData(0, 5, 0, HealthCheckResult.Unhealthy)] // All failures

    // Total 3 probes: need 2 for majority
    [InlineData(0, 0, 3, HealthCheckResult.Inconclusive)] // No results yet (3 total)
    [InlineData(2, 0, 1, HealthCheckResult.Healthy)] // Reached majority (2/3)
    [InlineData(1, 0, 2, HealthCheckResult.Inconclusive)] // Not yet majority (3 total)
    [InlineData(0, 2, 1, HealthCheckResult.Unhealthy)] // Majority impossible (2 failures of 3)
    [InlineData(2, 1, 0, HealthCheckResult.Healthy)] // Majority achieved (2/3)
    [InlineData(1, 2, 0, HealthCheckResult.Unhealthy)] // Majority failed (2/3)

    // Total 1 probe: need 1 for majority
    [InlineData(0, 0, 1, HealthCheckResult.Inconclusive)] // No results yet (1 total)
    [InlineData(1, 0, 0, HealthCheckResult.Healthy)] // Majority achieved (1/1)
    [InlineData(0, 1, 0, HealthCheckResult.Unhealthy)] // Majority failed (1/1)

    // Total 6 probes: need 4 for majority
    [InlineData(4, 0, 2, HealthCheckResult.Healthy)] // Reached majority (4/6)
    [InlineData(3, 0, 3, HealthCheckResult.Inconclusive)] // Not yet majority (6 total)
    [InlineData(0, 4, 2, HealthCheckResult.Unhealthy)] // Majority impossible (4 failures)
    [InlineData(3, 3, 0, HealthCheckResult.Inconclusive)] // Tied, neither side has majority (3/6 is not >=4)
    public void MajoritySuccess_EvaluatesCorrectly(int success, int failure, int remaining, HealthCheckResult expected)
    {
        var policy = HealthCheckProbePolicy.MajoritySuccess;
        var context = new HealthCheckProbeContext(HealthCheckResult.Inconclusive, success, failure, remaining, TimeSpan.Zero);

        var result = policy.Evaluate(context);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Policies_AreSingletons()
    {
        var any1 = HealthCheckProbePolicy.AnySuccess;
        var any2 = HealthCheckProbePolicy.AnySuccess;
        Assert.NotNull(any1);
        Assert.Same(any1, any2);

        var all1 = HealthCheckProbePolicy.AllSuccess;
        var all2 = HealthCheckProbePolicy.AllSuccess;
        Assert.NotNull(all1);
        Assert.Same(all1, all2);

        var maj1 = HealthCheckProbePolicy.MajoritySuccess;
        var maj2 = HealthCheckProbePolicy.MajoritySuccess;
        Assert.NotNull(maj1);
        Assert.Same(maj1, maj2);
    }
}
