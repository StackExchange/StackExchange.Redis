using System;
using System.Collections.Immutable;

namespace StackExchange.Redis.Availability;

public abstract class CircuitBreaker
{
    public class Builder
    {
        /// <summary>
        /// Percentage of failures to trigger circuit breaker.
        /// </summary>
        /// <remarks>Failures are only included if they are of tracked exception types.</remarks>
        public double FailureRateThreshold { get; set; } = 10;

        /// <summary>
        /// Minimum failures before circuit breaker can open.
        /// </summary>
        public int MinimumNumberOfFailures { get; set; } = 1000;

        /// <summary>
        /// Time window for collecting metrics.
        /// </summary>
        public TimeSpan MetricsWindowSize { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Create a new circuit-breaker instance.
        /// </summary>
        public CircuitBreaker Create() => new DefaultCircuitBreaker(
            FailureRateThreshold, MinimumNumberOfFailures, MetricsWindowSize, TrackedExceptions);

        /// <summary>
        /// Create a new circuit-breaker instance.
        /// </summary>
        public static implicit operator CircuitBreaker(Builder builder) => builder.Create();

        /// <summary>
        /// Exceptions that count as failures.
        /// </summary>
        public ImmutableArray<Type> TrackedExceptions { get; set; } = TrackedExceptionsDefault;
    }

    // important: if this changes, update the _isDefaultExceptions logic
    private static readonly ImmutableArray<Type> TrackedExceptionsDefault =
        new[] { typeof(RedisConnectionException), typeof(RedisTimeoutException) }.ToImmutableArray();

    /// <summary>
    /// Create an object to collate observations for a connection.
    /// </summary>
    public abstract Accumulator CreateAccumulator();

    /// <summary>
    /// Collates observations for a connection.
    /// </summary>
    public abstract class Accumulator
    {
        /// <summary>
        /// Respond to a message outcome, and indicate whether the connection is considered healthy.
        /// </summary>
        /// <returns>True if the connection is still considered healthy.</returns>
        public abstract bool IsHealthy(in CircuitBreakerContext context);
    }

    /// <summary>
    /// Provides information about a circuit-breaker test.
    /// </summary>
    public readonly struct CircuitBreakerContext(bool success, Exception? fault)
    {
        /// <summary>
        /// Was the operation a success.
        /// </summary>
        public bool Success => success;

        /// <summary>
        /// The fault associated with the operation.
        /// </summary>
        public Exception? Fault => fault;
    }

    private enum ExceptionStrategy
    {
        Default,
        Any,
        None,
        CustomOpen,
        CustomSealed,
    }

    private sealed class DefaultCircuitBreaker : CircuitBreaker
    {
        private readonly ExceptionStrategy _trackingStrategy;
        private readonly ImmutableArray<Type> _trackedExceptions;

        public DefaultCircuitBreaker(
            double failureRateThreshold,
            int minimumNumberOfFailures,
            TimeSpan metricsWindowSize,
            ImmutableArray<Type> trackedExceptions)
        {
            _trackedExceptions = CheckExceptions(trackedExceptions, out _trackingStrategy);
        }

        public override Accumulator CreateAccumulator() => new DefaultAccumulator(this);

        internal class DefaultAccumulator(DefaultCircuitBreaker breaker) : Accumulator
        {
            public override bool IsHealthy(in CircuitBreakerContext context)
            {
                bool tracked = context.Fault is { } ex && breaker.IsTracked(ex);
                // if not tracked, it counts as success - probably the server telling them not to do silly things
                return true;
            }
        }

        private static ImmutableArray<Type> CheckExceptions(ImmutableArray<Type> tracked, out ExceptionStrategy strategy)
        {
            if (tracked.IsDefaultOrEmpty)
            {
                strategy = ExceptionStrategy.None;
                return default;
            }
            strategy = ExceptionStrategy.CustomOpen;

            if (tracked.Length is 1 && tracked[0] == typeof(Exception))
            {
                strategy = ExceptionStrategy.Any;
                return default;
            }

            if (tracked.Equals(TrackedExceptionsDefault)) // identity equality
            {
                strategy = ExceptionStrategy.Default;
                return default;
            }
            if (tracked.Length == TrackedExceptionsDefault.Length) // semantic equality
            {
                strategy = ExceptionStrategy.Default;
                // iterate defaultTrackedExceptions, because we know it isn't duplicated
                foreach (var exception in TrackedExceptionsDefault.AsSpan())
                {
                    if (!tracked.Contains(exception))
                    {
                        strategy = ExceptionStrategy.CustomOpen;
                        break;
                    }
                }
                if (strategy is ExceptionStrategy.Default)
                {
                    return default;
                }
            }

            // finally, see if they're all sealed types
            strategy = ExceptionStrategy.CustomSealed;
            foreach (var exception in tracked)
            {
                if (!exception.IsSealed)
                {
                    strategy = ExceptionStrategy.CustomOpen;
                    break;
                }
            }
            return tracked;
        }

        private bool IsTracked(Exception fault)
        {
            switch (_trackingStrategy)
            {
                case ExceptionStrategy.Any:
                    return true;
                case ExceptionStrategy.Default:
                    return fault is RedisTimeoutException or RedisConnectionException;
                case ExceptionStrategy.None:
                    return false;
            }

            var span = _trackedExceptions.AsSpan();
            var actualType = fault.GetType();
            // check for exact matches
            foreach (var testType in span)
            {
                if (ReferenceEquals(testType, actualType)) return true;
            }

            if (_trackingStrategy is ExceptionStrategy.CustomOpen)
            {
                // we need to check for subclasses (more expensive)
                foreach (var testType in span)
                {
                    if (!testType.IsSealed && testType.IsAssignableFrom(actualType)) return true;
                }
            }

            return false;
        }
    }
}


