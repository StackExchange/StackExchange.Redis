using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Holds the retry configuration derived from a <see cref="RetryPolicy"/> (attempt counts, delays,
/// failover gating) and makes the per-attempt retry decision. This is shared by <see cref="RetryDatabase"/>
/// and <see cref="RetryTransaction"/> so the two paths apply identical policy math.
/// </summary>
internal sealed class RetryController
{
    private readonly int _maxBeforeFailover, _maxAttempts, _delayMillis, _jitterMillis, _failoverMillis, _maxWatchAttempts;
    private readonly RetryPolicy _policy;

    public RetryController(RetryPolicy policy, DatabaseFeatureFlags features)
    {
        _policy = policy;

        // capture config locally rather than constant cross-object lookups; a RetryPolicy is immutable and
        // is validated by RetryPolicy.Builder, so no range checks are needed here
        _maxBeforeFailover = (features & DatabaseFeatureFlags.Failover) == 0 ? int.MaxValue : policy.MaxAttemptsBeforeFailover;
        _maxAttempts = policy.MaxAttempts;
        if (_maxBeforeFailover == _maxAttempts) _maxBeforeFailover = int.MaxValue; // then we'll never look

        _delayMillis = ToMilliseconds(policy.RetryDelay);
        _failoverMillis = ToMilliseconds(policy.FailoverDelay);
        _jitterMillis = ToMilliseconds(policy.JitterMax);
        _maxWatchAttempts = policy.MaxAttemptsOnWatchConflict;

        Debug.Assert(_maxAttempts >= 1 && _maxBeforeFailover >= 1, "attempt counts should be validated by RetryPolicy");
        Debug.Assert(_delayMillis >= 0 && _jitterMillis >= 0 && _failoverMillis >= 0, "delays should be validated by RetryPolicy");
        Debug.Assert(_maxWatchAttempts >= 1, "watch-conflict attempts should be validated by RetryPolicy");

        static int ToMilliseconds(TimeSpan value) => (int)(value.Ticks / TimeSpan.TicksPerMillisecond);
    }

    /// <summary>
    /// The policy this controller is applying; exposed for tests, which assert how a policy was resolved.
    /// </summary>
    public RetryPolicy Policy => _policy;

    /// <summary>
    /// How many times a conditional transaction may be attempted when the server keeps rejecting the
    /// <c>EXEC</c> due to watch contention; see <see cref="RetryPolicy.MaxAttemptsOnWatchConflict"/>.
    /// </summary>
    public int MaxWatchConflictAttempts => _maxWatchAttempts;

    /// <summary>
    /// The pause before re-attempting a transaction that lost a <c>WATCH</c> race. Contention, not a fault:
    /// no backoff, just jitter to avoid two callers colliding again in lock-step.
    /// </summary>
    public Task WatchConflictDelayAsync()
        => _jitterMillis is 0
            ? Task.CompletedTask
            : Task.Delay(ServerSelectionStrategy.SharedRandom.Next(_jitterMillis), CancellationToken.None);

    /// <summary>
    /// Whether it is ever worth capturing the next-failover token: only when there is more than one
    /// attempt and the failover threshold sits below the attempt cap.
    /// </summary>
    public bool TracksFailover => _maxAttempts > 1 & _maxBeforeFailover < _maxAttempts;

    public bool CanRetry(
        int attempt,
        Exception fault,
        ref CancellationToken failover,
        out CancellationToken delay)
    {
        delay = CancellationToken.None;
        if (attempt >= _maxAttempts)
        {
            // all used up
            return false;
        }

        // ask the retry policy for advice, and mask off the bits we know about
        FaultContext ctx = new(fault);
        var policy = _policy.CanRetry(ctx) &
                     (RetryResult.FailoverServer | RetryResult.SameServer);
        if (policy is 0)
        {
            // retry policy says: nope
            return false;
        }

        if (policy is RetryResult.FailoverServer)
        {
            // we can *only* retry on a different server; is failover available?
            delay = failover;
            failover = CancellationToken.None; // only failover once
            return delay.CanBeCanceled;
        }

        if (attempt == _maxBeforeFailover)
        {
            // by count, we should really switch over to the failover now; is failover available *and* are we allowed?
            delay = failover;
            failover = CancellationToken.None; // only failover once
            return delay.CanBeCanceled & (policy & RetryResult.FailoverServer) != 0;
        }

        // can we pause and retry on the same server?
        return (policy & RetryResult.SameServer) != 0;
    }

    public Task FailoverOrDelayAsync(CancellationToken delay)
    {
        if (delay.CanBeCanceled)
        {
            return AwaitFailover(delay);
        }

        // this is just a routine wait between operations; await delay+jitter
        return Task.Delay(_delayMillis + ServerSelectionStrategy.SharedRandom.Next(_jitterMillis), CancellationToken.None);
    }

    private async Task AwaitFailover(CancellationToken failover)
    {
        if (!failover.IsCancellationRequested)
        {
            // failover hasn't happened yet; allow up to "delay" time for that
            try
            {
                await Task.Delay(_failoverMillis, failover).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (failover.IsCancellationRequested)
            {
                // we observed a failover, nice!
            }
        }

        // either way, we need to add jitter onto that; we can't add in the original delay, because if the failover
        // happened before the timeout+jitter, all the awaiters would stampede
        await Task.Delay(ServerSelectionStrategy.SharedRandom.Next(_jitterMillis), CancellationToken.None).ConfigureAwait(false);
    }
}
