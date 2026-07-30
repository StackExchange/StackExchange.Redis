using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Configures how messages can be retried due to connection / transient faults. Other faults (such as invalid
/// usage) are not retried.
/// </summary>
/// <remarks>
/// Instances are immutable and safe to share; use <see cref="Builder"/> to configure the standard policy, or
/// derive from this type and override <see cref="CanRetry"/> to make the decision yourself.
/// </remarks>
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public class RetryPolicy
{
    internal const int DefaultMaxAttempts = 3;
    internal const int DefaultMaxAttemptsBeforeFailover = 1;
    internal const int DefaultMaxAttemptsOnWatchConflict = 3;
    internal const CommandFlags DefaultMaxCommandRetryCategory = CommandFlags.CommandRetryWriteLastWins;
    internal static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan DefaultJitterMax = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan DefaultFailoverDelay = TimeSpan.FromSeconds(5);

    private static readonly RetryPolicy DefaultInstance = new();

    /// <summary>
    /// The default retry policy; retries transient faults up to <see cref="MaxAttempts"/> times, for commands
    /// at or below <see cref="CommandFlags.CommandRetryWriteLastWins"/>.
    /// </summary>
    public static RetryPolicy Default => DefaultInstance;

    /// <summary>
    /// Never retries anything; useful to disable retries without restructuring calling code.
    /// </summary>
    public static RetryPolicy None => NoRetryPolicy.Instance;

    /// <summary>
    /// Create a policy using the default settings; intended for use by derived types that override
    /// <see cref="CanRetry"/> - use <see cref="Default"/> to obtain the standard policy.
    /// </summary>
    protected RetryPolicy()
        : this(DefaultMaxAttempts, DefaultMaxAttemptsBeforeFailover, DefaultMaxAttemptsOnWatchConflict, DefaultRetryDelay, DefaultJitterMax, DefaultFailoverDelay, DefaultMaxCommandRetryCategory)
    {
    }

    /// <summary>
    /// Create a policy using the settings from the supplied <paramref name="builder"/>; intended for use by
    /// derived types that override <see cref="CanRetry"/>.
    /// </summary>
    protected RetryPolicy(Builder builder)
        : this(
            Validate(builder).MaxAttempts,
            builder.MaxAttemptsBeforeFailover,
            builder.MaxAttemptsOnWatchConflict,
            builder.RetryDelay,
            builder.JitterMax,
            builder.FailoverDelay,
            builder.MaxCommandRetryCategory)
    {
    }

    private RetryPolicy(
        int maxAttempts,
        int maxAttemptsBeforeFailover,
        int maxAttemptsOnWatchConflict,
        TimeSpan retryDelay,
        TimeSpan jitterMax,
        TimeSpan failoverDelay,
        CommandFlags maxCommandRetryCategory)
    {
        MaxAttempts = maxAttempts;
        MaxAttemptsBeforeFailover = maxAttemptsBeforeFailover;
        MaxAttemptsOnWatchConflict = maxAttemptsOnWatchConflict;
        RetryDelay = retryDelay;
        JitterMax = jitterMax;
        FailoverDelay = failoverDelay;
        MaxCommandRetryCategory = maxCommandRetryCategory;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name}: {MaxAttempts} attempt(s), up to {MaxCommandRetryCategory}";

    /// <summary>
    /// The maximum number of times an operation can be attempted. Defaults to 3.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// The maximum number of times to retry an operation before waiting for failover; this only currently
    /// applies to multi-group connections created via <c>ConnectionMultiplexer.ConnectGroupAsync</c>.
    /// Defaults to 1.
    /// </summary>
    public int MaxAttemptsBeforeFailover { get; }

    /// <summary>
    /// The maximum number of times a *conditional* transaction may be attempted when the only problem is
    /// that the server rejected the <c>EXEC</c> because a watched key changed underneath it. Defaults to 3;
    /// a value of 1 disables re-attempting such transactions.
    /// </summary>
    /// <remarks>
    /// <para>This is deliberately separate from <see cref="MaxAttempts"/>: a watch conflict is contention,
    /// not a fault. Nothing was applied, nothing is broken, and the right response is to re-read the
    /// conditions and try again immediately - so no <see cref="RetryDelay"/> is applied (only
    /// <see cref="JitterMax"/>), no failover is attempted, and <see cref="MaxCommandRetryCategory"/> does
    /// not apply. Each re-attempt re-issues the <c>WATCH</c> constraints, so a transaction whose condition
    /// has genuinely stopped holding converges on an ordinary elective abort rather than looping.</para>
    /// <para>Only transactions with conditions can be affected: without a condition there is no
    /// <c>WATCH</c>, so there is nothing to conflict.</para>
    /// </remarks>
    public int MaxAttemptsOnWatchConflict { get; }

    /// <summary>
    /// Gets the time to wait between retries that are *not* dependent on a failover happening. Defaults to 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; }

    /// <summary>
    /// Gets the time to wait for a failover, after <see cref="MaxAttemptsBeforeFailover"/> attempts. Only one
    /// failover attempt is awaited. When this applies, <see cref="RetryDelay"/> is ignored,
    /// but <see cref="JitterMax"/> is still respected. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan FailoverDelay { get; }

    /// <summary>
    /// Gets the upper bound for jitter - additional random delay between retries to prevent stampedes.
    /// Defaults to 0.5 seconds, meaning between 0 and 0.5 *additional* seconds on top of <see cref="RetryDelay"/>.
    /// </summary>
    public TimeSpan JitterMax { get; }

    /// <summary>
    /// Gets the max side-effect category that will be retried; defaults to <see cref="CommandFlags.CommandRetryWriteLastWins"/>.
    /// </summary>
    public CommandFlags MaxCommandRetryCategory { get; }

    /// <summary>
    /// Controls which operations can be repeated, optionally indicating that this should progress to
    /// a new server.
    /// </summary>
    public virtual RetryResult CanRetry(in FaultContext fault)
    {
        var actual = fault.Flags & Message.MaskRetryCategory;
        if (actual is 0) actual = CommandFlags.CommandRetryNever; // if not set, assume the worst (as FaultContext does)

        if (actual is CommandFlags.CommandRetryNever)
        {
            // explicitly disabled at command level
            return RetryResult.None;
        }

        // the category exists to price the *ambiguity* of a replay: if we know the operation never took
        // effect, re-issuing it is a first attempt rather than a repeat, so it cannot double-apply and the
        // side-effect scale is irrelevant. (CommandRetryNever above is still an absolute veto.)
        if (actual > MaxCommandRetryCategory && !fault.NotApplied)
        {
            // side-effects are beyond what the policy allows
            return RetryResult.None;
        }

        if (CircuitBreaker.DefaultIsFailure(in fault))
        {
            // assume we can send it everywhere
            var result = RetryResult.SameServer | RetryResult.FailoverServer;
            if ((fault.Flags & Message.CommandServerSpecific) != 0)
                result &= ~RetryResult.FailoverServer;
            return result;
        }

        // do not retry
        return RetryResult.None;
    }

    private static Builder Validate(Builder builder)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (builder.MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(builder.MaxAttempts), builder.MaxAttempts, "At least one attempt is required; use RetryPolicy.None to disable retries.");

        // values < 1 can never be hit by the attempt counter (which starts at 1), so they would *silently*
        // disable failover rather than erroring
        if (builder.MaxAttemptsBeforeFailover < 1) throw new ArgumentOutOfRangeException(nameof(builder.MaxAttemptsBeforeFailover), builder.MaxAttemptsBeforeFailover, "At least one attempt is required before failover.");

        // this one counts *attempts*, so 1 means "try once, do not re-attempt"; zero or negative is
        // meaningless rather than a way to say "never execute"
        if (builder.MaxAttemptsOnWatchConflict < 1) throw new ArgumentOutOfRangeException(nameof(builder.MaxAttemptsOnWatchConflict), builder.MaxAttemptsOnWatchConflict, "At least one attempt is required; use 1 to disable re-attempting on watch conflicts.");

        if (builder.RetryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(builder.RetryDelay), builder.RetryDelay, "A non-negative retry delay is required.");
        if (builder.JitterMax < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(builder.JitterMax), builder.JitterMax, "A non-negative jitter bound is required.");
        if (builder.FailoverDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(builder.FailoverDelay), builder.FailoverDelay, "A non-negative failover delay is required.");

        // the retry loop expresses all three delays in int milliseconds
        if (!IsExpressibleAsMilliseconds(builder.RetryDelay)) throw new ArgumentOutOfRangeException(nameof(builder.RetryDelay), builder.RetryDelay, "The retry delay is too large.");
        if (!IsExpressibleAsMilliseconds(builder.JitterMax)) throw new ArgumentOutOfRangeException(nameof(builder.JitterMax), builder.JitterMax, "The jitter bound is too large.");
        if (!IsExpressibleAsMilliseconds(builder.FailoverDelay)) throw new ArgumentOutOfRangeException(nameof(builder.FailoverDelay), builder.FailoverDelay, "The failover delay is too large.");

        var category = builder.MaxCommandRetryCategory;
        if ((category & Message.MaskRetryCategory) is 0 | (category & ~Message.MaskRetryCategory) is not 0)
        {
            throw new ArgumentException("A single valid CommandRetry* flag should be specified.", nameof(builder.MaxCommandRetryCategory));
        }

        return builder;

        static bool IsExpressibleAsMilliseconds(TimeSpan value) => value.Ticks / TimeSpan.TicksPerMillisecond <= int.MaxValue;
    }

    /// <summary>
    /// Allows configuration of the standard <see cref="RetryPolicy"/> implementation.
    /// </summary>
    [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
    public sealed class Builder
    {
        /// <summary>
        /// Create a builder pre-populated with the default values.
        /// </summary>
        public Builder()
        {
        }

        /// <summary>
        /// Create a builder pre-populated from an existing <see cref="RetryPolicy"/>.
        /// </summary>
        public Builder(RetryPolicy policy)
        {
            MaxAttempts = policy.MaxAttempts;
            MaxAttemptsBeforeFailover = policy.MaxAttemptsBeforeFailover;
            MaxAttemptsOnWatchConflict = policy.MaxAttemptsOnWatchConflict;
            RetryDelay = policy.RetryDelay;
            JitterMax = policy.JitterMax;
            FailoverDelay = policy.FailoverDelay;
            MaxCommandRetryCategory = policy.MaxCommandRetryCategory;
        }

        /// <summary>
        /// The maximum number of times an operation can be attempted.
        /// </summary>
        public int MaxAttempts { get; set; } = DefaultMaxAttempts;

        /// <summary>
        /// The maximum number of times to retry an operation before waiting for failover.
        /// </summary>
        public int MaxAttemptsBeforeFailover { get; set; } = DefaultMaxAttemptsBeforeFailover;

        /// <inheritdoc cref="RetryPolicy.MaxAttemptsOnWatchConflict"/>
        public int MaxAttemptsOnWatchConflict { get; set; } = DefaultMaxAttemptsOnWatchConflict;

        /// <summary>
        /// The time to wait between retries that are *not* dependent on a failover happening.
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = DefaultRetryDelay;

        /// <summary>
        /// The upper bound for jitter - additional random delay between retries to prevent stampedes.
        /// </summary>
        public TimeSpan JitterMax { get; set; } = DefaultJitterMax;

        /// <summary>
        /// The time to wait for a failover, after <see cref="MaxAttemptsBeforeFailover"/> attempts.
        /// </summary>
        public TimeSpan FailoverDelay { get; set; } = DefaultFailoverDelay;

        /// <summary>
        /// The max side-effect category that will be retried.
        /// </summary>
        public CommandFlags MaxCommandRetryCategory { get; set; } = DefaultMaxCommandRetryCategory;

        /// <summary>
        /// Create a new retry policy instance.
        /// </summary>
        public RetryPolicy Create()
        {
            Validate(this);

            // prefer the shared default instance when nothing has been customized
            if (MaxAttempts == DefaultMaxAttempts
                && MaxAttemptsBeforeFailover == DefaultMaxAttemptsBeforeFailover
                && MaxAttemptsOnWatchConflict == DefaultMaxAttemptsOnWatchConflict
                && RetryDelay == DefaultRetryDelay
                && JitterMax == DefaultJitterMax
                && FailoverDelay == DefaultFailoverDelay
                && MaxCommandRetryCategory == DefaultMaxCommandRetryCategory)
            {
                return DefaultInstance;
            }

            return new RetryPolicy(MaxAttempts, MaxAttemptsBeforeFailover, MaxAttemptsOnWatchConflict, RetryDelay, JitterMax, FailoverDelay, MaxCommandRetryCategory);
        }

        /// <summary>
        /// Create a new retry policy instance.
        /// </summary>
        public static implicit operator RetryPolicy(Builder builder) => builder.Create();
    }

    private sealed class NoRetryPolicy : RetryPolicy
    {
        public static readonly NoRetryPolicy Instance = new();

        // "never retries anything" has to include watch contention: that path does not consult CanRetry
        // (nothing was applied, so there is no fault to judge), it is bounded purely by the attempt count
        private NoRetryPolicy() : base(new Builder { MaxAttemptsOnWatchConflict = 1 })
        {
        }

        public override RetryResult CanRetry(in FaultContext fault) => RetryResult.None;
    }
}
