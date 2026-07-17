using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Configures how messages can be retried due to connection / transient faults. Other faults (such as invalid
/// usage) are not retried.
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
public class RetryPolicy
{
    /// <summary>
    /// The maximum number of times an operation can be attempted. Defaults to 3.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// The maximum number of times to retry an operation before waiting for failover; this only currently
    /// applies to multi-group connections created via <c>ConnectionMultiplexer.ConnectGroupAsync</c>.
    /// Defaults to 1.
    /// </summary>
    public int MaxAttemptsBeforeFailover { get; set; } = 1;

    private int _delayMillis = 1000, _jitterMillis = 500, _failoverMillis = 5000;

    /// <summary>
    /// Gets the time to wait between retries that are *not* dependent on a failover happening. Defaults to 1 second.
    /// </summary>
    public TimeSpan RetryDelay
    {
        get => TimeSpan.FromMilliseconds(_delayMillis);
        set => _delayMillis = checked((int)value.TotalMilliseconds);
    }

    /// <summary>
    /// Gets the time to wait for a failover, after <see cref="MaxAttemptsBeforeFailover"/> attempts. Only one
    /// failover attempt is awaited. When this applies, <see cref="RetryDelay"/> is ignored,
    /// but <see cref="JitterMax"/> is still respected. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan FailoverDelay
    {
        get => TimeSpan.FromMilliseconds(_failoverMillis);
        set => _failoverMillis = checked((int)value.TotalMilliseconds);
    }

    /// <summary>
    /// Gets or sets the upper bound for jitter - additional random delay between retries to prevent stampedes.
    /// Defaults to 0.5 seconds, meaning between 0 and 0.5 *additional* seconds on top of <see cref="RetryDelay"/>.
    /// </summary>
    public TimeSpan JitterMax
    {
        get => TimeSpan.FromMilliseconds(_jitterMillis);
        set => _jitterMillis = checked((int)value.TotalMilliseconds);
    }

    internal int DelayMilliseconds => _delayMillis;
    internal int JitterMilliseconds => _jitterMillis;
    internal int FailoverMilliseconds => _failoverMillis;

    /// <summary>
    /// Gets or sets the max side-effect category that will be retried; defaults to <see cref="CommandFlags.CommandRetryWriteLastWins"/>.
    /// </summary>
    public CommandFlags MaxCommandRetryCategory
    {
        get => _maxCommandRetryCategory;
        set
        {
            if ((value & Message.MaskRetryCategory) is 0 | (value & ~Message.MaskRetryCategory) is not 0)
                throw new InvalidOperationException("Valid CommandRetry* flags should be specified");
            _maxCommandRetryCategory = value;
        }
    }

    private CommandFlags _maxCommandRetryCategory = CommandFlags.CommandRetryWriteLastWins;

    /// <summary>
    /// Controls which operations can be repeated, optionally indicating that this should progress to
    /// a new server.
    /// </summary>
    public virtual RetryPolicyResult CanRetry(in FaultContext fault)
    {
        var actual = fault.Flags & Message.MaskRetryCategory;
        if (actual is 0) actual = CommandFlags.CommandRetryWriteAccumulating; // if not set, assume similar to INCR

        if (actual is CommandFlags.CommandRetryNever)
        {
            // explicitly disabled at command level
            return RetryPolicyResult.None;
        }

        if (actual > MaxCommandRetryCategory) // note this also covers CommandRetryAlways
        {
            // side-effects are beyond what the policy allows
            return RetryPolicyResult.None;
        }

        if (CircuitBreaker.DefaultIsFailure(in fault))
        {
            // assume we can send it everywhere
            var result = RetryPolicyResult.SameServer | RetryPolicyResult.FailoverServer;
            if ((fault.Flags & Message.CommandServerSpecific) != 0)
                result &= ~RetryPolicyResult.FailoverServer;
            return result;
        }

        // do not retry
        return RetryPolicyResult.None;
    }

    /// <summary>
    /// Indicates the result of a <see cref="RetryPolicy"/> query.
    /// </summary>
    [Flags]
    public enum RetryPolicyResult
    {
        /// <summary>
        /// None; the operation should not be retried.
        /// </summary>
        None = 0,

        /// <summary>
        /// The operation can be retried on the same server.
        /// </summary>
        SameServer = 1,

        /// <summary>
        /// The operation can be retried on a different server after a failover operation.
        /// </summary>
        FailoverServer = 2,
    }
}
