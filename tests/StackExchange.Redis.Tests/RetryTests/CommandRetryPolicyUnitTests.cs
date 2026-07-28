using System.Threading;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Interfaces;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

public class CommandRetryPolicyUnitTests
{
    // --- RetryPolicy.CanRetry: spoofed fault scenarios -------------------------------

    // Builds a FaultContext for a spoofed server error of the given kind, carrying the given
    // command-flags, and asks the policy whether it may be retried.
    private static RetryPolicy.RetryPolicyResult CanRetry(RedisErrorKind kind, CommandFlags flags, RetryPolicy? policy = null)
    {
        // the exception carries both the Kind and the command-flags; FaultContext reads them back
        var fault = new FaultContext(new RedisServerException(kind, flags, kind.ToString()));
        return (policy ?? new RetryPolicy()).CanRetry(in fault);
    }

    // The command's retry-category is checked against the policy's max category: the default max is
    // CommandRetryWriteLastWins, so anything at-or-below that is in-range, and anything with more
    // side-effects is not. Using a transient LOADING fault so the error-kind check permits a retry
    // whenever the category is in-range - isolating the category logic.
    [Theory]
    [InlineData(CommandFlags.CommandRetryAlways, true)]
    [InlineData(CommandFlags.CommandRetryConnection, true)]
    [InlineData(CommandFlags.CommandRetryReadOnly, true)]
    [InlineData(CommandFlags.CommandRetryWriteChecked, true)]
    [InlineData(CommandFlags.CommandRetryWriteLastWins, true)] // == default max
    [InlineData(CommandFlags.CommandRetryWriteAccumulating, false)] // beyond default max
    [InlineData(CommandFlags.CommandRetryServerAdmin, false)]
    [InlineData(CommandFlags.CommandRetryNever, false)]
    [InlineData(CommandFlags.None, false)] // unspecified => assume worst (accumulating) => beyond default max
    public void CanRetry_CategoryVersusDefaultMax(CommandFlags category, bool expectRetry)
    {
        var result = CanRetry(RedisErrorKind.Loading, category);
        Assert.Equal(expectRetry, result != RetryPolicy.RetryPolicyResult.None);
    }

    // With an in-range category (== default max), the outcome is decided purely by whether the error
    // is transient: LOADING is worth retrying, WRONGTYPE is an application error that will not fix itself.
    [Theory]
    [InlineData(RedisErrorKind.Loading, true)] // still loading the dataset - transient
    [InlineData(RedisErrorKind.ClusterDown, true)] // slot temporarily unserved - transient
    [InlineData(RedisErrorKind.WrongType, false)] // wrong data type - application error
    [InlineData(RedisErrorKind.NoPermission, false)] // ACL - application error
    public void CanRetry_ErrorKindGatesRetry_WhenInRange(RedisErrorKind kind, bool expectRetry)
    {
        var result = CanRetry(kind, CommandFlags.CommandRetryWriteLastWins);
        Assert.Equal(expectRetry, result != RetryPolicy.RetryPolicyResult.None);
    }

    // "never" and "always" adjust only the category range - they do not override the error-kind check:
    // an "always" command still won't retry an application error, and a "never" command won't retry even
    // a transient one.
    [Theory]
    [InlineData(CommandFlags.CommandRetryAlways, RedisErrorKind.Loading, true)]
    [InlineData(CommandFlags.CommandRetryAlways, RedisErrorKind.WrongType, false)]
    [InlineData(CommandFlags.CommandRetryNever, RedisErrorKind.Loading, false)]
    [InlineData(CommandFlags.CommandRetryNever, RedisErrorKind.WrongType, false)]
    public void CanRetry_NeverAndAlwaysAffectRangeNotErrorKind(CommandFlags category, RedisErrorKind kind, bool expectRetry)
    {
        var result = CanRetry(kind, category);
        Assert.Equal(expectRetry, result != RetryPolicy.RetryPolicyResult.None);
    }

    // When a retry is permitted, it normally offers both the same server and a failover server; but a
    // "server specific" (sticky) command must not move endpoints, so only the same-server option remains.
    [Theory]
    [InlineData(CommandFlags.None, RetryPolicy.RetryPolicyResult.SameServer | RetryPolicy.RetryPolicyResult.FailoverServer)]
    [InlineData(Message.CommandServerSpecific, RetryPolicy.RetryPolicyResult.SameServer)]
    public void CanRetry_ServerSpecificRestrictsToSameServer(CommandFlags extra, RetryPolicy.RetryPolicyResult expected)
    {
        // in-range category (== default max) + transient error => a retry is offered; the sticky flag
        // only changes *where* the retry may go, not *whether* it happens.
        var result = CanRetry(RedisErrorKind.Loading, CommandFlags.CommandRetryWriteLastWins | extra);
        Assert.Equal(expected, result);
    }

    // The sticky (server-specific) flag lives outside the retry-category region, so it is masked off
    // before the category-vs-max comparison and must not change the range verdict (retry-at-all vs none)
    // - it only affects the same/failover choice, covered above.
    [Theory]
    [InlineData(CommandFlags.CommandRetryReadOnly, true)] // in range
    [InlineData(CommandFlags.CommandRetryServerAdmin, false)] // beyond default max
    public void CanRetry_ServerSpecificDoesNotAffectRange(CommandFlags category, bool expectRetry)
    {
        var withoutFlag = CanRetry(RedisErrorKind.Loading, category);
        var withFlag = CanRetry(RedisErrorKind.Loading, category | Message.CommandServerSpecific);

        Assert.Equal(expectRetry, withoutFlag != RetryPolicy.RetryPolicyResult.None);
        Assert.Equal(expectRetry, withFlag != RetryPolicy.RetryPolicyResult.None);
    }

    // --- RetryDatabase.CanRetry: attempt accounting ----------------------------------

    // With max-attempts-before-failover pinned equal to max-attempts, the failover path is disabled, so
    // this exercises pure same-server attempt counting. A transient LOADING fault on an in-range command
    // means the policy would allow a retry; the only gate is the attempt counter: with MaxAttempts=3,
    // attempts 1 and 2 may retry, attempt 3 is exhausted. Because we never fail over, the out "delay" is
    // never cancellable (that is how "don't wait for failover" is expressed) and the ref "failover" is
    // left untouched.
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void RetryDatabase_CanRetry_MaxAttempts_NoFailover(int attempt, bool expected)
    {
        var policy = new RetryPolicy { MaxAttempts = 3, MaxAttemptsBeforeFailover = 3 };
        var controller = new RetryController(policy, DatabaseFeatureFlags.None); // CanRetry never touches any database

        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var failover = token;

        var fault = new RedisServerException(RedisErrorKind.Loading, CommandFlags.CommandRetryWriteLastWins, "LOADING");
        var result = controller.CanRetry(attempt, fault, ref failover, out var delay);

        Assert.Equal(expected, result);
        Assert.False(delay.CanBeCanceled); // never waiting for a failover
        Assert.Equal(token, failover); // ref failover untouched
    }

    // MaxAttempts=4 with failover enabled after 2 attempts. A single "failover" token is threaded through
    // the sequence to observe the state machine: attempts 1..3 return true, attempt 4 is exhausted. The
    // interesting step is attempt 2 (== MaxAttemptsBeforeFailover): it still returns true, but now hands the
    // failover token back as "delay" and clears the ref (we fail over only once); attempt 3 therefore sees
    // no failover token and drops back to a plain same-server retry.
    [Fact]
    public void RetryDatabase_CanRetry_FailoverAtThreshold()
    {
        var policy = new RetryPolicy { MaxAttempts = 4, MaxAttemptsBeforeFailover = 2 };
        // failover is only armed when the inner database advertises the feature; supply it explicitly
        var controller = new RetryController(policy, DatabaseFeatureFlags.Failover);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var failover = token;
        var fault = new RedisServerException(RedisErrorKind.Loading, CommandFlags.CommandRetryWriteLastWins, "LOADING");

        // attempt 1: plain same-server retry; failover token not yet consumed
        Assert.True(controller.CanRetry(1, fault, ref failover, out var delay));
        Assert.False(delay.CanBeCanceled);
        Assert.Equal(token, failover);

        // attempt 2 (== MaxAttemptsBeforeFailover): still a retry, but now fail over - "delay" becomes the
        // failover token and the ref is cleared to None so it only fires once
        Assert.True(controller.CanRetry(2, fault, ref failover, out delay));
        Assert.Equal(token, delay);
        Assert.True(delay.CanBeCanceled);
        Assert.Equal(CancellationToken.None, failover);

        // attempt 3: failover already spent (ref is None) -> back to a same-server retry
        Assert.True(controller.CanRetry(3, fault, ref failover, out delay));
        Assert.False(delay.CanBeCanceled);
        Assert.Equal(CancellationToken.None, failover);

        // attempt 4: no retries left
        Assert.False(controller.CanRetry(4, fault, ref failover, out delay));
        Assert.False(delay.CanBeCanceled);
    }

    // As above, but with the sticky (server-specific) flag set: the policy now permits only same-server
    // retries, so there is no failover option at the threshold. Current behaviour: CanRetry returns *false*
    // at attempt 2 - the command gives up rather than continuing on the same server - because the threshold
    // branch (attempt == MaxAttemptsBeforeFailover) requires FailoverServer permission and does not fall
    // back to a same-server retry. It also consumes the failover token as a side-effect of that branch.
    [Fact]
    public void RetryDatabase_CanRetry_ServerSpecific_CannotFailover()
    {
        var policy = new RetryPolicy { MaxAttempts = 4, MaxAttemptsBeforeFailover = 2 };
        var controller = new RetryController(policy, DatabaseFeatureFlags.Failover);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var failover = token;
        const CommandFlags flags = CommandFlags.CommandRetryWriteLastWins | Message.CommandServerSpecific;
        var fault = new RedisServerException(RedisErrorKind.Loading, flags, "LOADING");

        // attempt 1: same-server retry; failover token untouched
        Assert.True(controller.CanRetry(1, fault, ref failover, out var delay));
        Assert.False(delay.CanBeCanceled);
        Assert.Equal(token, failover);

        // attempt 2 (== MaxAttemptsBeforeFailover): sticky forbids failover -> gives up (false), even though
        // attempts remain; the failover token is still consumed to None as a side-effect of the branch
        Assert.False(controller.CanRetry(2, fault, ref failover, out delay));
        Assert.Equal(CancellationToken.None, failover);
    }
}
