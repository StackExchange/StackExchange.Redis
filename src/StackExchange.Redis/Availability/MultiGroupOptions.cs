using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Configuration options for controlling connections to multiple groups.
/// </summary>
/// <remarks>
/// Instances are immutable; use <see cref="Builder"/> to configure. Every value here is a group-wide
/// default, and can be overridden per-member by the matching property on <see cref="ConnectionGroupMember"/>
/// (where one exists); the effective value is "member override, else group default".
/// </remarks>
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public sealed class MultiGroupOptions
{
    internal static readonly TimeSpan DefaultHealthCheckInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DefaultFailbackDelay = TimeSpan.Zero;

    private static readonly MultiGroupOptions DefaultInstance = new(
        HealthCheck.Default, CircuitBreaker.Default, RetryPolicy.Default, DefaultHealthCheckInterval, DefaultFailbackDelay);

    /// <summary>
    /// Default shared options.
    /// </summary>
    public static MultiGroupOptions Default => DefaultInstance;

    private MultiGroupOptions(
        HealthCheck healthCheck,
        CircuitBreaker circuitBreaker,
        RetryPolicy retryPolicy,
        TimeSpan healthCheckInterval,
        TimeSpan failbackDelay)
    {
        HealthCheck = healthCheck;
        CircuitBreaker = circuitBreaker;
        RetryPolicy = retryPolicy;
        HealthCheckInterval = healthCheckInterval;
        FailbackDelay = failbackDelay;
    }

    /// <inheritdoc/>
    public override string ToString() => $"health-check: {HealthCheck} every {HealthCheckInterval}; failback: {FailbackDelay}";

    /// <summary>
    /// The health check to use for members of the group when no per-member health check is specified.
    /// </summary>
    public HealthCheck HealthCheck { get; }

    /// <summary>
    /// The circuit-breaker to use for members of the group when no per-member circuit-breaker is specified.
    /// </summary>
    public CircuitBreaker CircuitBreaker { get; }

    /// <summary>
    /// The retry policy used by <see cref="DatabaseExtensions.WithRetry"/> for databases
    /// obtained from this group.
    /// </summary>
    public RetryPolicy RetryPolicy { get; }

    /// <summary>
    /// How frequently health checks are performed, and therefore how frequently the active member is
    /// re-evaluated. <see cref="TimeSpan.MaxValue"/> disables periodic checking entirely (the group is then
    /// only re-evaluated in response to connection events such as a tripped circuit-breaker).
    /// </summary>
    public TimeSpan HealthCheckInterval { get; }

    /// <summary>
    /// If a member has been marked unhealthy by a failing health-check or circuit-breaker, it will not be
    /// re-selected as the active member until it has remained healthy for this interval following its most
    /// recent failure. When <see cref="TimeSpan.Zero"/> (the default), failback is immediate; when
    /// <see cref="TimeSpan.MaxValue"/>, failback is not automatic and requires explicit use of
    /// <see cref="ConnectionGroupMember.ResetIsUnhealthy"/> or <see cref="IConnectionGroup.TryFailoverTo"/>.
    /// </summary>
    public TimeSpan FailbackDelay { get; }

    /// <summary>
    /// Allows configuration of <see cref="MultiGroupOptions"/>.
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
        /// Create a builder pre-populated from existing options.
        /// </summary>
        public Builder(MultiGroupOptions options)
        {
            HealthCheck = options.HealthCheck;
            CircuitBreaker = options.CircuitBreaker;
            RetryPolicy = options.RetryPolicy;
            HealthCheckInterval = options.HealthCheckInterval;
            FailbackDelay = options.FailbackDelay;
        }

        /// <summary>
        /// The health check to use for members of the group when no per-member health check is specified.
        /// </summary>
        public HealthCheck HealthCheck { get; set; } = HealthCheck.Default;

        /// <summary>
        /// The circuit-breaker to use for members of the group when no per-member circuit-breaker is specified.
        /// </summary>
        public CircuitBreaker CircuitBreaker { get; set; } = CircuitBreaker.Default;

        /// <summary>
        /// The retry policy used by <see cref="DatabaseExtensions.WithRetry"/> for databases
        /// obtained from this group.
        /// </summary>
        public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;

        /// <summary>
        /// How frequently health checks are performed, and therefore how frequently the active member is
        /// re-evaluated; <see cref="TimeSpan.MaxValue"/> disables periodic checking.
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = DefaultHealthCheckInterval;

        /// <summary>
        /// How long a member must remain healthy, following its most recent failure, before it is eligible to
        /// be selected again; <see cref="TimeSpan.MaxValue"/> requires explicit intervention.
        /// </summary>
        public TimeSpan FailbackDelay { get; set; } = DefaultFailbackDelay;

        /// <summary>
        /// Create a new options instance.
        /// </summary>
        public MultiGroupOptions Create()
        {
            if (HealthCheck is null) throw new ArgumentNullException(nameof(HealthCheck));
            if (CircuitBreaker is null) throw new ArgumentNullException(nameof(CircuitBreaker));
            if (RetryPolicy is null) throw new ArgumentNullException(nameof(RetryPolicy));
            if (HealthCheckInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(HealthCheckInterval), HealthCheckInterval, "A positive interval is required; use TimeSpan.MaxValue to disable periodic health checks.");
            if (FailbackDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(FailbackDelay), FailbackDelay, "A non-negative delay is required.");

            // prefer the shared default instance when nothing has been customized
            if (ReferenceEquals(HealthCheck, HealthCheck.Default)
                && ReferenceEquals(CircuitBreaker, CircuitBreaker.Default)
                && ReferenceEquals(RetryPolicy, RetryPolicy.Default)
                && HealthCheckInterval == DefaultHealthCheckInterval
                && FailbackDelay == DefaultFailbackDelay)
            {
                return DefaultInstance;
            }

            return new MultiGroupOptions(HealthCheck, CircuitBreaker, RetryPolicy, HealthCheckInterval, FailbackDelay);
        }

        /// <summary>
        /// Create a new options instance.
        /// </summary>
        public static implicit operator MultiGroupOptions(Builder builder) => builder.Create();
    }
}
