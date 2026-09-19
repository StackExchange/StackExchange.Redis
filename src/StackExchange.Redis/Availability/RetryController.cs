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

    /// <summary>
    /// Whether <see cref="CanRetry"/> could ever answer <see langword="true"/> for a command carrying
    /// <paramref name="flags"/>, decided before anything is sent.
    /// </summary>
    /// <param name="flags">The command's flags, whose retry category is the veto.</param>
    /// <remarks>
    /// <b>Both halves are decided without consulting the policy, and <see cref="CanRetry"/> decides them
    /// the same way</b> - which is what makes this a pre-check rather than a second opinion. The attempt
    /// cap is the obvious one. The retry category is the other: <see cref="CommandFlags.CommandRetryNever"/>
    /// is an absolute veto, and so is an unset category, because that is read as "assume the worst"
    /// wherever it is met - <see cref="FaultContext"/> substitutes <c>CommandRetryNever</c> for a fault
    /// that carries none.
    /// </remarks>
    public bool CanEverRetry(CommandFlags flags) => _maxAttempts > 1 && !IsVetoed(flags);

    /// <summary>
    /// Whether the command itself forbids replay, whatever the policy thinks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The caller's veto, and it outranks the policy.</b> <c>CommandRetryNever</c> means the command
    /// must not be replayed - either because this library categorised it that way or because the caller
    /// said so with <c>WithRetryCategory</c> - so asking a policy whether it would like to is asking the
    /// wrong question. The shipped <see cref="RetryPolicy.CanRetry"/> has always tested this first and
    /// returned <see cref="RetryResult.None"/>; hoisting it here makes it a property of the <i>controller</i>
    /// rather than of one policy implementation, so an override cannot quietly lose it - and lets the
    /// decision be taken before a send rather than only after a fault.
    /// </para>
    /// <para>
    /// <b>Fire-and-forget is the second veto, and it is about intent rather than safety.</b> Retry exists
    /// to improve an outcome somebody is waiting for; <see cref="CommandFlags.FireAndForget"/> declares
    /// that nobody is. Replaying one cannot be observed to have helped, and the cost - a backoff delay
    /// added to a call advertised as returning immediately - is observable. <c>RespBatchExecutor</c> makes
    /// the same point structurally: a fire-and-forget command there is answered <i>before</i> it is sent,
    /// so by the time anything could fail there is no longer anybody to tell.
    /// </para>
    /// <para>
    /// <b>The two habits compound, which is what makes this a safe default rather than a guess.</b>
    /// Fire-and-forget is reached for when a command is wanted for its side effect and its answer is not -
    /// a counter, a list push, a publish - and those are <see cref="CommandFlags.CommandRetryWriteAccumulating"/>,
    /// which sits <i>above</i> the default cap of <see cref="CommandFlags.CommandRetryWriteLastWins"/> and
    /// so is already refused. The commands people fire and forget and the commands the default policy
    /// would replay barely overlap to begin with.
    /// </para>
    /// <para>
    /// <b>Today it changes nothing at all, and that is worth knowing rather than discovering.</b> A
    /// fire-and-forget message has no result box, so <c>ConnectionMultiplexer.ThrowFailed</c> swallows its
    /// write failure and <c>ExecuteAsyncImpl</c> ignores the <c>WriteResult</c> on the synchronous path -
    /// a fire-and-forget send cannot fault, so no retry loop ever engages for one. This says so out loud
    /// instead of depending on it: an executor that did start faulting them would otherwise quietly begin
    /// adding retry delays to the one call shape chosen for not having any.
    /// </para>
    /// <para>
    /// <b>If it ever needs to be arguable, it becomes a policy setting</b> rather than a special case here
    /// - the same shape <see cref="RetryPolicy.MaxCommandRetryCategory"/> already has for the other veto.
    /// Not built until somebody wants it.
    /// </para>
    /// </remarks>
    private static bool IsVetoed(CommandFlags flags)
    {
        if ((flags & CommandFlags.FireAndForget) != 0) return true;

        var category = flags & Message.MaskRetryCategory;
        return category is 0 or CommandFlags.CommandRetryNever;
    }

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

        FaultContext ctx = new(fault);
        if (IsVetoed(ctx.Flags))
        {
            // the command says never; the policy is not asked, because it is not its call
            return false;
        }

        // ask the retry policy for advice, and mask off the bits we know about
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
